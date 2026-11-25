using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The Access tab beside Workload: one row per access watch with its mark and health line, an "Add a watch" button
/// and the Mark all conflicts setting. Singleton. Rows are rebuilt whenever <see cref="AccessState.Changed"/> fires
/// (every section reload, grid-content change and eye click, so a semester switch is covered too).
///
/// <para><b>Several semesters (Q43).</b> Each semester is analysed on its own. A watch has one row, taken from its
/// worst semester (attention, then fragile, then ok); when more than one semester is loaded the health line is
/// prefixed with that semester's name so the user knows which one the row speaks of.</para>
///
/// <para><b>Read-only.</b> Looking at watches and the Mark all conflicts setting (a per-computer view setting) work
/// without the write lock; adding a watch does not (<see cref="CanAddWatch"/>).</para>
/// </summary>
public sealed partial class AccessTabViewModel : ViewModelBase
{
    private readonly AccessState _state;
    private readonly SemesterContext _semesters;
    private readonly ICampusRepository _campusRepo;
    private readonly AccessWatchToggler _toggler;
    private readonly AccessSetupService _setup;
    private bool _isSetUp;

    /// <summary>The tooltip of the Add a watch button when it is enabled (approved wording).</summary>
    // approved wording
    public const string AddWatchEnabledTip = "Add a watch";

    /// <summary>The tooltip of the Add a watch button when this computer cannot save (approved wording).</summary>
    // approved wording
    public const string AddWatchReadOnlyTip = "Read-only: you can look at watches but not change them.";

    /// <summary>Raised when the user clicks Add a watch; the main window opens the filter's Tags popup with its note.</summary>
    public event Action? AddWatchRequested;

    /// <summary>The tab's title: "Access · " and the loaded semester(s), or just "Access" when none is loaded.</summary>
    [ObservableProperty] private string _title = "Access";

    /// <summary>The watches, attention first, then by name (ordinal, ignoring case).</summary>
    public ObservableCollection<AccessWatchRowViewModel> Rows { get; } = new();

    /// <summary>True when at least one watch exists.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>True when there are no watches.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>True when any watch needs a look (drives the dot on the tab).</summary>
    [ObservableProperty] private bool _needsALook;

    /// <summary>
    /// Whether Mark all conflicts is on: every conflict between two courses of a watch is marked on the grid, not
    /// only those that cost students something. Two-way onto <see cref="AccessState.MarkAllConflicts"/>, a
    /// per-computer view setting, so it works read-only too.
    /// </summary>
    public bool MarkAllConflicts
    {
        get => _state.MarkAllConflicts;
        set
        {
            if (_state.MarkAllConflicts == value) return;
            _state.MarkAllConflicts = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The Add a watch command; raises <see cref="AddWatchRequested"/>. Disabled while this computer cannot save.</summary>
    public IRelayCommand AddWatchCommand { get; }

    /// <summary>True only while the write lock is ours.</summary>
    public bool CanAddWatch => _toggler.CanToggle;

    /// <summary>The Add a watch button's tooltip: the button's name, or why it is off.</summary>
    public string AddWatchTip => CanAddWatch ? AddWatchEnabledTip : AddWatchReadOnlyTip;

    /// <summary>The Set up command: asks the main window to open the setup wizard (the invitation's button).</summary>
    public IRelayCommand SetUpCommand { get; }

    /// <summary>
    /// True once access watches are set up. Until then Add a watch and Mark all conflicts are hidden (Greg,
    /// 2026-11-25): watches start from the setup.
    /// </summary>
    public bool IsSetUp => _isSetUp;

    /// <summary>True when the invitation shows: access watches are not set up and this computer can save.</summary>
    public bool ShowSetupInvite => !_isSetUp && _setup.CanSave;

    /// <summary>True when the quiet read-only line shows instead: not set up, and this computer cannot save.</summary>
    public bool ShowSetupReadOnlyLine => !_isSetUp && !_setup.CanSave;

    /// <summary>The invitation's title.</summary>
    public string SetupInviteTitle => AccessText.SetupInviteTitle;

    /// <summary>The invitation's button text.</summary>
    public string SetupInviteButton => AccessText.SetupInviteButton;

    /// <summary>The invitation's link text: "What's an access watch?".</summary>
    public string WhatIsAWatchLink => AccessText.WhatIsAWatchLink;

    /// <summary>Opens the help page or video on access watches in the browser.</summary>
    public IRelayCommand WhatIsAWatchCommand { get; } = new RelayCommand(() => PlatformProcess.OpenUri(AccessText.WhatIsAWatchUri));

    /// <summary>The read-only line's sentence, naming the section types that look like components.</summary>
    public string SetupReadOnlyText => AccessText.SetupReadOnlyText(_setup.LikelyComponentNames());

    /// <summary>Creates the tab and fills it from the current access state.</summary>
    /// <param name="state">The access results.</param>
    /// <param name="semesters">The loaded semesters (their names).</param>
    /// <param name="campusRepo">Campus names for the health lines.</param>
    /// <param name="toggler">Says whether this computer may change watches.</param>
    /// <param name="setup">The access setup: whether it is done, whether this computer can save, and how to open the wizard.</param>
    public AccessTabViewModel(
        AccessState state, SemesterContext semesters, ICampusRepository campusRepo, AccessWatchToggler toggler,
        AccessSetupService setup)
    {
        _setup = setup;
        _state = state;
        _semesters = semesters;
        _campusRepo = campusRepo;
        _toggler = toggler;
        AddWatchCommand = new RelayCommand(() => AddWatchRequested?.Invoke(), () => CanAddWatch);
        SetUpCommand = new RelayCommand(() => _setup.RequestSetupWizard(), () => _setup.CanSave);
        _setup.CanSaveChanged += OnCanToggleChanged;
        // Never unsubscribed: singletons that live as long as the services of one database.
        _state.Changed += Refresh;
        _toggler.CanToggleChanged += OnCanToggleChanged;
        _semesters.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(SemesterContext.SelectedSemesters)) UpdateTitle(); };
        Refresh();
    }

    /// <summary>Re-reads the write lock for the Add a watch button.</summary>
    private void OnCanToggleChanged()
    {
        OnPropertyChanged(nameof(CanAddWatch));
        OnPropertyChanged(nameof(AddWatchTip));
        AddWatchCommand.NotifyCanExecuteChanged();
        SetUpCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowSetupInvite));
        OnPropertyChanged(nameof(ShowSetupReadOnlyLine));
    }

    /// <summary>Sets <see cref="Title"/> from the loaded semesters.</summary>
    private void UpdateTitle()
    {
        var names = _semesters.SelectedSemesters.Select(s => s.DisplayName).ToList();
        Title = names.Count == 0 ? "Access" : $"Access · {string.Join(", ", names)}";
    }

    /// <summary>
    /// Rebuilds <see cref="Rows"/> from <see cref="AccessState.Current"/> (the analysis is worked out here, once per
    /// state, for every watch) and the properties that follow them. A failure is logged and leaves the rows as they
    /// were: it must never break the reload that raised the change.
    /// </summary>
    public void Refresh()
    {
        try
        {
            _isSetUp = _setup.IsSetUp;
            OnPropertyChanged(nameof(IsSetUp));
            OnPropertyChanged(nameof(ShowSetupInvite));
            OnPropertyChanged(nameof(ShowSetupReadOnlyLine));
            OnPropertyChanged(nameof(SetupReadOnlyText));
            RebuildRows();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessTabViewModel.Refresh");
        }
        UpdateTitle();
        OnCanToggleChanged();
        OnPropertyChanged(nameof(MarkAllConflicts));
    }

    /// <summary>Builds one row per watch from its worst semester and replaces <see cref="Rows"/>' contents.</summary>
    private void RebuildRows()
    {
        var snapshot = _state.Current;
        bool several = snapshot.Semesters.Count > 1;
        var campuses = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
        string CampusName(string? id) => id is not null && campuses.TryGetValue(id, out var n) ? n : "another campus";
        var semesterNames = _semesters.SelectedSemesters.ToDictionary(s => s.Semester.Id, s => s.Semester.Name);

        var rows = new List<AccessWatchRowViewModel>();
        var byTag = snapshot.Semesters.Values
            .SelectMany(s => s.Watches.Select(w => (Semester: s, Watch: w)))
            .GroupBy(x => x.Watch.Tag.Id);
        foreach (var group in byTag)
        {
            var (semester, watch) = group.OrderByDescending(x => Rank(x.Watch.Analysis.Health)).First();
            var analysis = watch.Analysis;
            var line = AccessText.HealthLine(semester.Inputs, analysis, CampusName);
            if (several)
                line = $"{(semesterNames.TryGetValue(semester.SemesterId, out var sn) ? sn : semester.SemesterId)}: {line}";
            rows.Add(new AccessWatchRowViewModel(watch.Tag.Name, line, analysis.Health));
        }

        var sorted = rows.OrderBy(r => r.IsAttention ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Rows.Clear();
        foreach (var r in sorted) Rows.Add(r);
        NeedsALook = sorted.Any(r => r.IsAttention);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>How bad a health is, for picking a watch's worst semester: attention (and too large) over fragile over ok.</summary>
    private static int Rank(AccessHealth health) =>
        health is AccessHealth.Attention or AccessHealth.TooLarge ? 2 : health == AccessHealth.Fragile ? 1 : 0;
}
