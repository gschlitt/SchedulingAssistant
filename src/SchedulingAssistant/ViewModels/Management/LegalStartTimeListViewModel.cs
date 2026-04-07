using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents one item in the "Preferred block length" ComboBox.</summary>
public record NullableBlockLengthOption(double? Value, string Label);

/// <summary>
/// Display wrapper for a <see cref="LegalStartTime"/> row in the DataGrid.
/// Carries the block length formatted in the current unit so the grid reflects the active preference.
/// </summary>
public record LegalStartTimeRow(LegalStartTime Entry, string BlockLengthDisplay)
{
    /// <summary>Start times forwarded from the underlying entry.</summary>
    public string StartTimesDisplay => Entry.StartTimesDisplay;
}

public partial class LegalStartTimeListViewModel : ViewModelBase, IDisposable
{
    private readonly ILegalStartTimeRepository _repo;
    private readonly SemesterContext _semesterContext;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;
    private readonly SectionListViewModel _sectionListVm;
    private readonly SectionChangeNotifier _changeNotifier;
    private string? _currentAcademicYearId;

    /// <summary>True when the current user holds the write lock; controls whether CRUD and settings controls are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<LegalStartTime> _entries = new();

    /// <summary>
    /// Unit-formatted display rows shown in the DataGrid.
    /// Rebuilt whenever entries or the block-length unit changes.
    /// </summary>
    public ObservableCollection<LegalStartTimeRow> DisplayEntries { get; } = new();

    /// <summary>The row currently selected in the DataGrid.</summary>
    [ObservableProperty] private LegalStartTimeRow? _selectedRow;

    /// <summary>The underlying entry for the selected row; null when nothing is selected.</summary>
    private LegalStartTime? SelectedEntry => SelectedRow?.Entry;

    [ObservableProperty] private LegalStartTimeEditViewModel? _editVm;

    // ── Include Saturday / Sunday settings ───────────────────────────────────

    private bool _includeSaturday;

    /// <summary>
    /// Whether Saturday is available as a scheduling day.
    /// Writes through immediately to AppSettings on change.
    /// </summary>
    public bool IncludeSaturday
    {
        get => _includeSaturday;
        set
        {
            if (_includeSaturday == value) return;
            _includeSaturday = value;
            OnPropertyChanged();

            var settings = AppSettings.Current;
            settings.IncludeSaturday = value;
            settings.Save();

            // Immediately repaint both views so the Saturday column appears/disappears
            // without requiring the user to close and reopen anything.
            _sectionListVm.Reload();
            _changeNotifier.NotifySectionChanged();
        }
    }

    private bool _includeSunday;

    /// <summary>
    /// Whether Sunday is available as a scheduling day.
    /// Writes through immediately to AppSettings on change.
    /// </summary>
    public bool IncludeSunday
    {
        get => _includeSunday;
        set
        {
            if (_includeSunday == value) return;
            _includeSunday = value;
            OnPropertyChanged();

            var settings = AppSettings.Current;
            settings.IncludeSunday = value;
            settings.Save();

            // Immediately repaint both views so the Sunday column appears/disappears
            // without requiring the user to close and reopen anything.
            _sectionListVm.Reload();
            _changeNotifier.NotifySectionChanged();
        }
    }

    // ── Block length unit ─────────────────────────────────────────────────────

    private BlockLengthUnit _blockLengthUnit;

    /// <summary>
    /// Controls whether block lengths are displayed and entered in hours or minutes.
    /// Writes through immediately to AppSettings on change and refreshes all dependent labels.
    /// </summary>
    public BlockLengthUnit BlockLengthUnit
    {
        get => _blockLengthUnit;
        set
        {
            if (_blockLengthUnit == value) return;
            _blockLengthUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHoursUnit));
            OnPropertyChanged(nameof(IsMinutesUnit));
            OnPropertyChanged(nameof(BlockLengthColumnHeader));

            var settings = AppSettings.Current;
            settings.BlockLengthUnit = value;
            settings.Save();

            RebuildDisplayEntries();
            RebuildPreferredOptions();
        }
    }

    /// <summary>True when the unit is Hours; used by RadioButton IsChecked binding.</summary>
    public bool IsHoursUnit
    {
        get => _blockLengthUnit == BlockLengthUnit.Hours;
        set { if (value) BlockLengthUnit = BlockLengthUnit.Hours; }
    }

    /// <summary>True when the unit is Minutes; used by RadioButton IsChecked binding.</summary>
    public bool IsMinutesUnit
    {
        get => _blockLengthUnit == BlockLengthUnit.Minutes;
        set { if (value) BlockLengthUnit = BlockLengthUnit.Minutes; }
    }

    /// <summary>DataGrid column header text, updated when the unit changes.</summary>
    public string BlockLengthColumnHeader => BlockLengthFormatter.BlockLengthColumnHeader(_blockLengthUnit);

    // ── Preferred block length ────────────────────────────────────────────────

    /// <summary>Options list for the preferred-block-length ComboBox, rebuilt when entries change.</summary>
    public ObservableCollection<NullableBlockLengthOption> PreferredBlockLengthOptions { get; } = new();

    private NullableBlockLengthOption? _selectedPreferredOption;

    /// <summary>
    /// The currently selected preferred-block-length option.
    /// Writes through immediately to AppSettings on change.
    /// </summary>
    public NullableBlockLengthOption? SelectedPreferredOption
    {
        get => _selectedPreferredOption;
        set
        {
            if (_selectedPreferredOption == value) return;
            _selectedPreferredOption = value;
            OnPropertyChanged();

            var settings = AppSettings.Current;
            settings.PreferredBlockLength = value?.Value;
            settings.Save();
        }
    }

    public LegalStartTimeListViewModel(
        ILegalStartTimeRepository repo,
        SemesterContext semesterContext,
        IDialogService dialog,
        WriteLockService lockService,
        SectionListViewModel sectionListVm,
        SectionChangeNotifier changeNotifier)
    {
        _repo            = repo;
        _semesterContext = semesterContext;
        _dialog          = dialog;
        _lockService     = lockService;
        _sectionListVm   = sectionListVm;
        _changeNotifier  = changeNotifier;
        _lockService.LockStateChanged += OnLockStateChanged;
        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        Load();
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnSemesterContextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
            Load();
    }

    private void Load()
    {
        var ayId = _semesterContext.SelectedSemesterDisplay?.Semester.AcademicYearId;
        if (string.IsNullOrEmpty(ayId)) return;

        _currentAcademicYearId = ayId;
        Entries = new ObservableCollection<LegalStartTime>(_repo.GetAll(ayId));

        _blockLengthUnit = AppSettings.Current.BlockLengthUnit;
        OnPropertyChanged(nameof(BlockLengthUnit));
        OnPropertyChanged(nameof(IsHoursUnit));
        OnPropertyChanged(nameof(IsMinutesUnit));
        OnPropertyChanged(nameof(BlockLengthColumnHeader));

        RebuildDisplayEntries();
        RebuildPreferredOptions();
        _includeSaturday = AppSettings.Current.IncludeSaturday;
        OnPropertyChanged(nameof(IncludeSaturday));
        _includeSunday = AppSettings.Current.IncludeSunday;
        OnPropertyChanged(nameof(IncludeSunday));
    }

    private void RebuildDisplayEntries()
    {
        DisplayEntries.Clear();
        foreach (var e in Entries)
            DisplayEntries.Add(new LegalStartTimeRow(e,
                BlockLengthFormatter.FormatBlockLength(e.BlockLength, _blockLengthUnit)));
    }

    private void RebuildPreferredOptions()
    {
        var saved = AppSettings.Current.PreferredBlockLength;

        PreferredBlockLengthOptions.Clear();

        var none = new NullableBlockLengthOption(null, "(none)");
        PreferredBlockLengthOptions.Add(none);

        foreach (var entry in Entries)
            PreferredBlockLengthOptions.Add(new NullableBlockLengthOption(entry.BlockLength,
                BlockLengthFormatter.LabelFor(entry.BlockLength, _blockLengthUnit)));

        _selectedPreferredOption = saved.HasValue
            ? PreferredBlockLengthOptions.FirstOrDefault(o => o.Value.HasValue && Math.Abs(o.Value.Value - saved.Value) < 0.01)
              ?? none
            : none;
        OnPropertyChanged(nameof(SelectedPreferredOption));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        if (string.IsNullOrEmpty(_currentAcademicYearId)) return;
        var entry = new LegalStartTime();
        EditVm = new LegalStartTimeEditViewModel(entry, isNew: true, _blockLengthUnit,
            onSave: async e =>
            {
                try { _repo.Insert(e, _currentAcademicYearId); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "LegalStartTimeListViewModel.Add"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(_currentAcademicYearId)) return;
        var copy = new LegalStartTime { BlockLength = SelectedEntry.BlockLength, StartTimes = new List<int>(SelectedEntry.StartTimes) };
        EditVm = new LegalStartTimeEditViewModel(copy, isNew: false, _blockLengthUnit,
            onSave: async e =>
            {
                try { _repo.Update(e, _currentAcademicYearId); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "LegalStartTimeListViewModel.Edit"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(_currentAcademicYearId)) return;
        try
        {
            _repo.Delete(_currentAcademicYearId, SelectedEntry.BlockLength);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "LegalStartTimeListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }

    public void Dispose()
    {
        _semesterContext.PropertyChanged -= OnSemesterContextChanged;
    }
}
