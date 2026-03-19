using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents one item in the "Preferred block length" ComboBox.</summary>
public record NullableBlockLengthOption(double? Value, string Label);

public partial class LegalStartTimeListViewModel : ViewModelBase, IDisposable
{
    private readonly ILegalStartTimeRepository _repo;
    private readonly SemesterContext _semesterContext;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;
    private string? _currentAcademicYearId;

    /// <summary>True when the current user holds the write lock; controls whether CRUD and settings controls are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<LegalStartTime> _entries = new();
    [ObservableProperty] private LegalStartTime? _selectedEntry;
    [ObservableProperty] private LegalStartTimeEditViewModel? _editVm;

    // ── Include Saturday setting ──────────────────────────────────────────────

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
        }
    }

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

    public LegalStartTimeListViewModel(ILegalStartTimeRepository repo, SemesterContext semesterContext, IDialogService dialog, WriteLockService lockService)
    {
        _repo = repo;
        _semesterContext = semesterContext;
        _dialog = dialog;
        _lockService = lockService;
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
        RebuildPreferredOptions();
        _includeSaturday = AppSettings.Current.IncludeSaturday;
        OnPropertyChanged(nameof(IncludeSaturday));
    }

    private void RebuildPreferredOptions()
    {
        var saved = AppSettings.Current.PreferredBlockLength;

        PreferredBlockLengthOptions.Clear();

        var none = new NullableBlockLengthOption(null, "(none)");
        PreferredBlockLengthOptions.Add(none);

        foreach (var entry in Entries)
            PreferredBlockLengthOptions.Add(new NullableBlockLengthOption(entry.BlockLength, $"{entry.BlockLength:0.#} hrs"));

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
        EditVm = new LegalStartTimeEditViewModel(entry, isNew: true,
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
        EditVm = new LegalStartTimeEditViewModel(copy, isNew: false,
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
