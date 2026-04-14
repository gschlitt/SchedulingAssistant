using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Section Prefixes management flyout.
/// Provides a list of section prefixes with inline Add/Edit/Delete support,
/// plus a toggle for whether the institution uses section prefixes at all.
/// </summary>
public partial class SectionPrefixListViewModel : ViewModelBase, IDismissableEditor
{
    private readonly ISectionPrefixRepository _repo;
    private readonly ICampusRepository _campusRepo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;
    private readonly AppConfigurationService _appConfig;

    /// <summary>The list of section prefixes shown in the data grid.</summary>
    [ObservableProperty] private ObservableCollection<SectionPrefixRow> _items = new();

    /// <summary>The currently highlighted row in the data grid.</summary>
    [ObservableProperty] private SectionPrefixRow? _selectedItem;

    /// <summary>
    /// Non-null while the Add/Edit form is visible; null when showing the list.
    /// </summary>
    [ObservableProperty] private SectionPrefixEditViewModel? _editVm;

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (EditVm is null) return false;
        EditVm.CancelCommand.Execute(null);
        return true;
    }

    /// <summary>
    /// Whether the institution uses section prefixes.  When false the prefix list
    /// is hidden and the bare <see cref="GlobalDesignatorType"/> is used instead.
    /// Persisted in the DB via <see cref="AppConfigurationService"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrefixListVisible))]
    private bool _useSectionPrefixes;

    /// <summary>
    /// True when the global designator type is <see cref="DesignatorType.Letter"/>.
    /// Bound to the Letter radio button when <see cref="UseSectionPrefixes"/> is false.
    /// </summary>
    [ObservableProperty] private bool _isLetterDesignator;

    /// <summary>
    /// Whether the prefix list and CRUD toolbar should be visible.
    /// True only when <see cref="UseSectionPrefixes"/> is enabled.
    /// </summary>
    public bool IsPrefixListVisible => UseSectionPrefixes;

    /// <summary>True when the current user holds the write lock; controls whether CRUD buttons are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    /// <param name="repo">Repository for section prefix CRUD operations.</param>
    /// <param name="campusRepo">Repository used to load campus options for the dropdown.</param>
    /// <param name="dialog">Service for confirmation and error dialogs.</param>
    /// <param name="lockService">Write lock service; gates edit operations in read-only mode.</param>
    /// <param name="appConfig">DB-persisted application configuration (prefix opt-out and global designator type).</param>
    public SectionPrefixListViewModel(
        ISectionPrefixRepository repo,
        ICampusRepository campusRepo,
        IDialogService dialog,
        WriteLockService lockService,
        AppConfigurationService appConfig)
    {
        _repo = repo;
        _campusRepo = campusRepo;
        _dialog = dialog;
        _lockService = lockService;
        _appConfig = appConfig;
        _lockService.LockStateChanged += OnLockStateChanged;

        // Load DB-persisted settings without triggering the Changed partial methods.
        _useSectionPrefixes = _appConfig.UseSectionPrefixes;
        _isLetterDesignator = _appConfig.GlobalDesignatorType == DesignatorType.Letter;

        Load();
    }

    /// <summary>
    /// Persists a change to the UseSectionPrefixes toggle.
    /// </summary>
    partial void OnUseSectionPrefixesChanged(bool value) =>
        _appConfig.SetUseSectionPrefixes(value);

    /// <summary>
    /// Persists a change to the global designator type (Number vs. Letter).
    /// </summary>
    partial void OnIsLetterDesignatorChanged(bool value) =>
        _appConfig.SetGlobalDesignatorType(value ? DesignatorType.Letter : DesignatorType.Number);

    /// <summary>
    /// Reloads the list from the database, resolving campus names for display.
    /// </summary>
    public void Load()
    {
        var prefixes = _repo.GetAll();
        var campuses = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);

        Items = new ObservableCollection<SectionPrefixRow>(
            prefixes.Select(p => new SectionPrefixRow(p, ResolveCampusName(p.CampusId, campuses))));
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

    /// <summary>Builds the campus option list for the edit form dropdown.</summary>
    private List<CampusOption> BuildCampusOptions()
    {
        var campuses = _campusRepo.GetAll();
        var options = new List<CampusOption> { new(null, "(none)") };
        options.AddRange(campuses.Select(c => new CampusOption(c.Id, c.Name)));
        return options;
    }

    private static string ResolveCampusName(string? campusId, Dictionary<string, string> lookup) =>
        campusId is not null && lookup.TryGetValue(campusId, out var name) ? name : string.Empty;

    /// <summary>Opens the Add form for a new prefix.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var model = new SectionPrefix();
        EditVm = new SectionPrefixEditViewModel(
            target: model,
            isNew: true,
            campusOptions: BuildCampusOptions(),
            onSave: v => { _repo.Insert(v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            prefixExists: text => _repo.ExistsByPrefix(text));
    }

    /// <summary>Opens the Edit form for the currently selected prefix.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedItem is null) return;

        // Work on a copy so the grid row is not mutated until save.
        var copy = new SectionPrefix
        {
            Id            = SelectedItem.Prefix.Id,
            Prefix        = SelectedItem.Prefix.Prefix,
            CampusId      = SelectedItem.Prefix.CampusId,
            DesignatorType = SelectedItem.Prefix.DesignatorType,
        };

        EditVm = new SectionPrefixEditViewModel(
            target: copy,
            isNew: false,
            campusOptions: BuildCampusOptions(),
            onSave: v => { _repo.Update(v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            prefixExists: text => _repo.ExistsByPrefix(text, excludeId: copy.Id));
    }

    /// <summary>
    /// Prompts for confirmation then deletes the currently selected prefix.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedItem is null) return;

        var prefixText = SelectedItem.Prefix.Prefix;
        if (!await _dialog.Confirm($"Delete the prefix \"{prefixText}\"?"))
            return;

        _repo.Delete(SelectedItem.Prefix.Id);
        Load();
    }
}

/// <summary>
/// Display-ready row for the Section Prefixes data grid.
/// Pairs a <see cref="SectionPrefix"/> model with its resolved campus display name.
/// </summary>
/// <param name="Prefix">The underlying model object.</param>
/// <param name="CampusName">Campus display name, or empty string if no campus is associated.</param>
public record SectionPrefixRow(SectionPrefix Prefix, string CampusName)
{
    /// <summary>Human-readable designator type for the data grid ("Number" or "Letter").</summary>
    public string DesignatorTypeDisplay =>
        Prefix.DesignatorType == DesignatorType.Letter ? "Letter" : "Number";
}
