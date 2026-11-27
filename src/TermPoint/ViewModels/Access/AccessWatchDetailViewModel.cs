using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>One flag line of a watch's detail: a mark and a sentence ("▲ BMEG 230 L1 is stranded: ...", "◆ ... carries a heavy load").</summary>
public sealed class AccessFlagViewModel
{
    /// <summary>"▲" for a problem, "◆" for a caution.</summary>
    public string Mark { get; }

    /// <summary>The sentence.</summary>
    public string Text { get; }

    /// <summary>True for a yellow caution (◆); false for a problem (▲).</summary>
    public bool IsCaution { get; }

    /// <summary>Creates a flag.</summary>
    /// <param name="text">The sentence.</param>
    /// <param name="isCaution">True for a caution.</param>
    public AccessFlagViewModel(string text, bool isCaution)
    {
        Text = text;
        IsCaution = isCaution;
        Mark = isCaution ? "◆" : "▲";
    }
}

/// <summary>A pinned section as a chip in the pin banner: its name and a command that unpins it.</summary>
public sealed class AccessPinChipViewModel
{
    /// <summary>The pinned section's id (clicking the name selects it).</summary>
    public string SectionId { get; }

    /// <summary>The section's name in a sentence ("CHEM 213 A").</summary>
    public string Label { get; }

    /// <summary>Unpins this section.</summary>
    public IRelayCommand UnpinCommand { get; }

    /// <summary>The chip's hover.</summary>
    public string UnpinTip => AccessText.UnpinText;

    /// <summary>Creates a chip.</summary>
    /// <param name="sectionId">The section id.</param>
    /// <param name="label">The section's name.</param>
    /// <param name="unpin">What the ✕ does.</param>
    public AccessPinChipViewModel(string sectionId, string label, Action unpin)
    {
        SectionId = sectionId;
        Label = label;
        UnpinCommand = new RelayCommand(unpin);
    }
}

/// <summary>One loaded semester the open watch can be looked at in (the detail's semester switch, Q43).</summary>
public sealed class AccessSemesterChoiceViewModel
{
    /// <summary>The semester's id.</summary>
    public string Id { get; }

    /// <summary>The semester's name.</summary>
    public string Name { get; }

    /// <summary>True for the semester being looked at.</summary>
    public bool IsSelected { get; }

    /// <summary>Shows the watch in this semester.</summary>
    public IRelayCommand ChooseCommand { get; }

    /// <summary>Creates a choice.</summary>
    /// <param name="id">The semester id.</param>
    /// <param name="name">The semester's name.</param>
    /// <param name="isSelected">Whether it is the one shown.</param>
    /// <param name="choose">What choosing does.</param>
    public AccessSemesterChoiceViewModel(string id, string name, bool isSelected, Action choose)
    {
        Id = id;
        Name = name;
        IsSelected = isSelected;
        ChooseCommand = new RelayCommand(choose);
    }
}

/// <summary>One phrase of a course row as the view draws it: a <see cref="CandidatePhrase"/> plus whether it is expanded.</summary>
/// <param name="Key">Identifies the phrase for expanding it.</param>
/// <param name="Label">The component's name and a colon ("Lab: "); empty for a course with fewer than two components.</param>
/// <param name="Text">The phrase ("all 4 sections", "only A", "4 of 8").</param>
/// <param name="IsExpandable">True for the "N of M" form, drawn as a button with a chevron.</param>
/// <param name="IsExpanded">True while its line is shown under the row (the chevron points down).</param>
/// <param name="IsNone">True when no section of the component is in the watch (drawn quietly).</param>
public sealed record AccessCandidatePhraseViewModel(string Key, string Label, string Text, bool IsExpandable, bool IsExpanded, bool IsNone);

/// <summary>
/// One course of a watch's detail: a tick box (never saved), the course's code and what a student taking it can pick
/// (<see cref="Phrases"/>, round 6c). Updated in place when the watch is redrawn, so a tick click never replaces the
/// row under the pointer.
/// </summary>
public sealed partial class AccessCourseRowViewModel : ObservableObject
{
    private readonly AccessPicture _picture;
    private readonly Action<string> _toggleExpanded;
    private bool _isTicked;

    /// <summary>The course's id.</summary>
    public string CourseId { get; }

    /// <summary>The course's calendar code ("BMEG 230").</summary>
    public string Code { get; }

    /// <summary>
    /// Whether the course is ticked, so part of the slates. Setting it ticks or unticks the course in the
    /// <see cref="AccessPicture"/>; the last ticked course cannot be unticked (<see cref="IsTickEnabled"/>).
    /// </summary>
    public bool IsTicked
    {
        get => _isTicked;
        set
        {
            if (_isTicked == value) return;
            // the picture raises Changed, and the detail redraws this row with the truth (a refused tick stays ticked)
            _picture.SetCourseTicked(CourseId, value);
            OnPropertyChanged();
        }
    }

    /// <summary>False for the only ticked course: it cannot be unticked.</summary>
    [ObservableProperty] private bool _isTickEnabled = true;

    /// <summary>The tick box's tooltip: "At least one course stays ticked." for the last one; empty otherwise.</summary>
    [ObservableProperty] private string _tickTip = string.Empty;

    /// <summary>What a student can pick from each component, in order.</summary>
    [ObservableProperty] private IReadOnlyList<AccessCandidatePhraseViewModel> _phrases = [];

    /// <summary>The expanded lines (one per expanded "N of M" phrase), shown under the row.</summary>
    [ObservableProperty] private IReadOnlyList<string> _expandedLines = [];

    /// <summary>Expands or collapses a phrase, by its <see cref="CandidatePhrase.Key"/>.</summary>
    public IRelayCommand<string> TogglePhraseCommand { get; }

    /// <summary>Creates a row.</summary>
    /// <param name="picture">Where ticks are made.</param>
    /// <param name="courseId">The course's id.</param>
    /// <param name="code">The course's calendar code.</param>
    /// <param name="toggleExpanded">Expands or collapses a phrase by key.</param>
    public AccessCourseRowViewModel(AccessPicture picture, string courseId, string code, Action<string> toggleExpanded)
    {
        _picture = picture;
        _toggleExpanded = toggleExpanded;
        CourseId = courseId;
        Code = code;
        TogglePhraseCommand = new RelayCommand<string>(key => { if (key is not null) _toggleExpanded(key); });
    }

    /// <summary>Sets the row's state from the picture without ticking anything.</summary>
    /// <param name="ticked">Whether the course is ticked.</param>
    /// <param name="isLastTicked">Whether it is the only ticked course.</param>
    /// <param name="phrases">The component phrases.</param>
    /// <param name="expandedLines">The lines of the expanded phrases.</param>
    public void Update(bool ticked, bool isLastTicked, IReadOnlyList<AccessCandidatePhraseViewModel> phrases, IReadOnlyList<string> expandedLines)
    {
        _isTicked = ticked;
        OnPropertyChanged(nameof(IsTicked));
        IsTickEnabled = !(ticked && isLastTicked);
        TickTip = ticked && isLastTicked ? AccessText.LastTickTip : string.Empty;
        Phrases = phrases;
        ExpandedLines = expandedLines;
    }
}

/// <summary>
/// The detail of one access watch (or of one course checked alone) in the Access tab: a port of the r9 wireframe's
/// <c>detailView</c>. Reads the open thing from <see cref="AccessPicture"/> and says what the engine found, in the
/// approved words of <see cref="AccessText"/>; it holds no analysis of its own and no .axaml knowledge. Singleton.
///
/// <para><b>What it shows.</b> The header (name, the Watching button that stops watching the tag, or in check mode
/// "‹ Check all courses" and a source line); a semester switch when several semesters are loaded; the health line;
/// the pin banner; the flags (stranded sections, cautions, missing components, sections with no section type, rule 8a
/// mismatches); the quiet "not tagged" line; one row per course with a tick box and what a student takes; the
/// Multi-campus box; the hint about pinning and stepping; and a fold of quieter facts. Ticks, pins and stepping
/// work read-only (they save nothing); the Watching button and the Multi-campus box need the write lock.</para>
///
/// <para>Redrawn whenever the picture or the access state changes (the picture subscribed to the state first, so its
/// cache is fresh), and when the write lock changes.</para>
/// </summary>
public sealed partial class AccessWatchDetailViewModel : ViewModelBase
{
    private readonly AccessPicture _picture;
    private readonly AccessState _state;
    private readonly AccessWatchToggler _toggler;
    private readonly SemesterContext _semesters;
    private readonly ICampusRepository _campusRepo;
    private readonly SectionStore _sectionStore;
    private readonly HashSet<string> _expanded = [];
    private string? _openKey;
    private bool _multiCampus;

    /// <summary>Raised when the user asks to go back (to the list, or to Check all courses in check mode).</summary>
    public event Action? BackRequested;

    /// <summary>True while a watch or a course is open.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>True for a course checked on its own (not a watch; nothing is saved).</summary>
    [ObservableProperty] private bool _isCheckMode;

    /// <summary>The watch's name, or the checked course's code.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>The back button's text: "All watches", or "Check all courses" in check mode (no chevron; the view draws it).</summary>
    [ObservableProperty] private string _backText = AccessText.BackToWatchesText;

    /// <summary>
    /// What a course checked on its own covers ("A checked on its own: ..."); empty for a watch, whose name in the
    /// header says it (Greg, 2026-11-26: the watch's name once, not again in a "Watching the tag" line).
    /// </summary>
    [ObservableProperty] private string _sourceText = string.Empty;

    /// <summary>The bold title above the courses ("Sections Watched"; Greg, 2026-11-26).</summary>
    public string SectionsWatchedText => AccessText.SectionsWatchedText;

    /// <summary>True for a watch, not a course checked alone: the title and Mark all conflicts show. (Stopping a watch is the eye on the list.)</summary>
    [ObservableProperty] private bool _showWatching;

    /// <summary>True while this computer can save: the Watching button and the Multi-campus box work.</summary>
    [ObservableProperty] private bool _canChange;

    /// <summary>The Multi-campus box's tooltip.</summary>
    public string MultiCampusTip => CanChange ? AccessText.MultiCampusTip : AccessText.ReadOnlyTip;

    /// <summary>The semesters the open thing can be looked at in; shown only when there is more than one.</summary>
    public ObservableCollection<AccessSemesterChoiceViewModel> SemesterChoices { get; } = [];

    /// <summary>True when more than one loaded semester has the open thing (the switch shows).</summary>
    [ObservableProperty] private bool _hasSemesterChoice;

    /// <summary>The health line's phrases, in order, each carrying the " · " that follows it.</summary>
    [ObservableProperty] private IReadOnlyList<HealthPhraseItem> _healthPhrases = [];

    /// <summary>The health line as one text.</summary>
    [ObservableProperty] private string _healthLine = string.Empty;

    /// <summary>True when the health line is drawn as needing attention: no full slate, or a component missing.</summary>
    [ObservableProperty] private bool _isHealthAttention;

    /// <summary>True when some section is pinned: the pin banner shows.</summary>
    [ObservableProperty] private bool _hasPins;

    /// <summary>The pinned sections as chips.</summary>
    public ObservableCollection<AccessPinChipViewModel> PinChips { get; } = [];

    /// <summary>The start of the pin banner (the chips follow).</summary>
    public string PinBannerLead => AccessText.PinBannerLead;

    /// <summary>The end of the pin banner.</summary>
    public string PinBannerTail => AccessText.PinBannerTail;

    /// <summary>The pin banner's Unpin all button text.</summary>
    public string UnpinAllText => AccessText.UnpinAllText;

    /// <summary>Unpins every section (the pin banner's Unpin all).</summary>
    public IRelayCommand UnpinAllCommand { get; }

    /// <summary>The flags: stranded sections, cautions, missing components, sections with no type, mismatches.</summary>
    public ObservableCollection<AccessFlagViewModel> Flags { get; } = [];

    /// <summary>True when a watched course has sections without the tag (decision 8g): the quiet line shows.</summary>
    [ObservableProperty] private bool _showUntagged;

    /// <summary>The very quiet line about untagged sections.</summary>
    public string UntaggedText => AccessText.UntaggedLine;

    /// <summary>One row per course the open thing offers.</summary>
    public ObservableCollection<AccessCourseRowViewModel> Courses { get; } = [];

    /// <summary>True when a subset is ticked: the Tick all link shows.</summary>
    [ObservableProperty] private bool _showTickAll;

    /// <summary>The Tick all link's text.</summary>
    public string TickAllText => AccessText.TickAllText;

    /// <summary>Ticks every course again.</summary>
    public IRelayCommand TickAllCommand { get; }

    /// <summary>True when the Multi-campus box shows: the watch's in-person sections span campuses, or the box is ticked.</summary>
    [ObservableProperty] private bool _showMultiCampus;

    /// <summary>The Multi-campus box's label.</summary>
    public string MultiCampusLabel => AccessText.MultiCampusLabel;

    /// <summary>
    /// The watch's Multi-campus box. Setting it saves the tag through <see cref="AccessWatchToggler.SetMultiCampus"/>
    /// (refused without the write lock, which leaves the box as it was).
    /// </summary>
    public bool MultiCampus
    {
        get => _multiCampus;
        set
        {
            if (_multiCampus == value) return;
            var tagId = _picture.TagId;
            if (tagId is null) return;
            try
            {
                LastErrorMessage = null;
                _toggler.SetMultiCampus(tagId, value);
            }
            catch (InvalidOperationException)
            {
                LastErrorMessage = AccessText.ReadOnlyTip;
            }
            catch (Exception ex)
            {
                App.Logger.LogError(ex, "AccessWatchDetailViewModel.MultiCampus");
                LastErrorMessage = "The Multi-campus setting couldn't be saved. Your work is safe; try again.";
            }
            // the save redraws everything; this puts the box back when the save was refused or failed
            Refresh();
        }
    }

    /// <summary>
    /// True when there is more than one slate: the stepper, the slate's sections and the pinning hint show (Greg,
    /// 2026-11-26: they move here from the grid's banner, and only appear when there is something to step through).
    /// </summary>
    [ObservableProperty] private bool _isStepping;

    /// <summary>"Slate 3 of 12", or "Slate 3 of more than 10,000".</summary>
    [ObservableProperty] private string _positionText = string.Empty;

    /// <summary>True when there is a previous slate.</summary>
    [ObservableProperty] private bool _canGoPrevious;

    /// <summary>True when there is a next slate.</summary>
    [ObservableProperty] private bool _canGoNext;

    /// <summary>Goes to the previous slate.</summary>
    public IRelayCommand PreviousCommand { get; }

    /// <summary>Goes to the next slate.</summary>
    public IRelayCommand NextCommand { get; }

    /// <summary>The stepper's ‹ hover.</summary>
    public string PreviousSlateTip => AccessText.PreviousSlateTip;

    /// <summary>The stepper's › hover.</summary>
    public string NextSlateTip => AccessText.NextSlateTip;

    /// <summary>
    /// The slate being shown as plain lines, one per course ("CHEM 213  A + B"), for the view to show in selectable
    /// text so it can be copied and pasted elsewhere.
    /// </summary>
    [ObservableProperty] private string _slateText = string.Empty;

    /// <summary>True when the slate being shown puts in-person sections on more than one campus.</summary>
    [ObservableProperty] private bool _mixesCampuses;

    /// <summary>The "mixes campuses" tag's text.</summary>
    public string MixesCampusesText => AccessText.MixesCampusesText;

    /// <summary>The tag's tooltip, naming the campuses.</summary>
    [ObservableProperty] private string _mixesCampusesTip = string.Empty;

    /// <summary>
    /// The quiet line when the filters or the subject focus hide some of the open watch's sections, e.g. "2 of
    /// Cohort 1's sections are hidden by the filters."; empty when none is hidden.
    /// </summary>
    [ObservableProperty] private string _hiddenSectionsText = string.Empty;

    /// <summary>True while <see cref="HiddenSectionsText"/> has something to say.</summary>
    public bool HasHiddenSections => HiddenSectionsText.Length > 0;

    partial void OnHiddenSectionsTextChanged(string value) => OnPropertyChanged(nameof(HasHiddenSections));

    /// <summary>Rebuilds the hidden-sections line from what the grid reported (the grid draws after the picture changes).</summary>
    private void RefreshHiddenSections() =>
        HiddenSectionsText = !_picture.IsOpen || _picture.HiddenSectionCount == 0 ? string.Empty
            : AccessText.HiddenWatchSectionsText(_picture.HiddenSectionCount, _picture.Name, _picture.HiddenByFocus);

    /// <summary>Fills the stepper from the picture: the position, the slate's lines and whether it mixes campuses.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="a">The analysis.</param>
    private void RebuildStepper(AccessInputs inputs, Analysis a)
    {
        IsStepping = _picture.IsStepping;
        if (!IsStepping || _picture.CurrentSlate is not { } slate)
        {
            IsStepping = false;
            PositionText = SlateText = string.Empty;
            CanGoPrevious = CanGoNext = MixesCampuses = false;
            return;
        }
        int index = _picture.SlateIndex;
        PositionText = AccessText.SlatePosition(index, _picture.Slates.Count, a.SlateCountCapped);
        CanGoPrevious = index > 0;
        CanGoNext = index < _picture.Slates.Count - 1;
        SlateText = string.Join(Environment.NewLine, a.ChosenCourseIds
            .Select(c => (Course: c, Mine: slate.Where(s => s.CourseId == c).ToList()))
            .Where(x => x.Mine.Count > 0)
            .Select(x => $"{inputs.CourseCode(x.Course)}  {string.Join(" + ", x.Mine.Select(s => s.SectionCode))}"));
        var campusIds = slate.Where(CampusRule.IsInPerson).Select(s => s.CampusId).Distinct().ToList();
        MixesCampuses = campusIds.Count > 1;
        if (MixesCampuses)
        {
            var names = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
            MixesCampusesTip = AccessText.MixesCampusesTip(
                campusIds.Select(id => id is not null && names.TryGetValue(id, out var n) ? n : "another campus")
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    /// <summary>True when the hint about pinning shows (with the stepper: there is more than one slate).</summary>
    [ObservableProperty] private bool _showStepHint;

    /// <summary>The hint about pinning.</summary>
    public string StepHint => AccessText.StepHint;

    /// <summary>The quieter facts, shown under the fold.</summary>
    [ObservableProperty] private IReadOnlyList<string> _facts = [];

    /// <summary>True when there are quieter facts (the fold shows).</summary>
    [ObservableProperty] private bool _hasFacts;

    /// <summary>The fold's label without a chevron: "More about this watch (2)" or "Less" when open.</summary>
    [ObservableProperty] private string _foldLabel = string.Empty;

    /// <summary>True while the fold is open (the facts show, the chevron points down).</summary>
    [ObservableProperty] private bool _isFoldOpen;

    /// <summary>Opens or closes the fold of quieter facts.</summary>
    public IRelayCommand ToggleFoldCommand { get; }

    /// <summary>
    /// Selects a section named in the detail (a section link's target, <c>section:id</c>), as clicking it on the grid
    /// would: Schedule View and the section list show it and its meetings selected.
    /// </summary>
    public IRelayCommand<string?> SelectSectionCommand { get; }

    /// <summary>Goes back (to the list, or to Check all courses in check mode).</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>
    /// Creates the detail and fills it from the picture.
    /// </summary>
    /// <param name="picture">The open watch, ticks and pins.</param>
    /// <param name="state">The access results (the detail redraws when they change).</param>
    /// <param name="toggler">Stops watching a tag and saves the Multi-campus box; says whether this computer can save.</param>
    /// <param name="semesters">The loaded semesters (their names, for the switch).</param>
    /// <param name="campusRepo">Campus names for the sentences.</param>
    /// <param name="sectionStore">Selects a section when its name is clicked in the detail.</param>
    public AccessWatchDetailViewModel(
        AccessPicture picture, AccessState state, AccessWatchToggler toggler, SemesterContext semesters, ICampusRepository campusRepo,
        SectionStore sectionStore)
    {
        _sectionStore = sectionStore;
        SelectSectionCommand = new RelayCommand<string?>(SelectSection);
        _picture = picture;
        _state = state;
        _toggler = toggler;
        _semesters = semesters;
        _campusRepo = campusRepo;
        BackCommand = new RelayCommand(() => BackRequested?.Invoke());
        UnpinAllCommand = new RelayCommand(_picture.ClearPins);
        PreviousCommand = new RelayCommand(() => _picture.Step(-1));
        NextCommand = new RelayCommand(() => _picture.Step(1));
        TickAllCommand = new RelayCommand(_picture.TickAll);
        ToggleFoldCommand = new RelayCommand(() => { IsFoldOpen = !IsFoldOpen; RefreshFold(); });
        // Never unsubscribed: singletons that live as long as the services of one database.
        _picture.Changed += Refresh;
        _picture.HiddenSectionsChanged += RefreshHiddenSections;
        _state.Changed += Refresh;
        _toggler.CanToggleChanged += Refresh;
        Refresh();
    }

    /// <summary>Selects the section a clicked section link names; ignores any other target.</summary>
    /// <param name="target">The link's target, <c>section:id</c>.</param>
    private void SelectSection(string? target)
    {
        if (target is null || !target.StartsWith(AccessText.SectionLinkScheme, StringComparison.Ordinal)) return;
        _sectionStore.SetSelection(target[AccessText.SectionLinkScheme.Length..]);
    }

    /// <summary>
    /// Redraws everything from the picture. A failure is logged and leaves the detail as it was: it must never break
    /// the change that raised it.
    /// </summary>
    public void Refresh()
    {
        try
        {
            Rebuild();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessWatchDetailViewModel.Refresh");
        }
    }

    /// <summary>Reads the picture and sets every property.</summary>
    private void Rebuild()
    {
        var inputs = _picture.Inputs;
        var a = _picture.Analysis;
        IsOpen = _picture.IsOpen && inputs is not null && a is not null;
        CanChange = _toggler.CanToggle;
        OnPropertyChanged(nameof(MultiCampusTip));
        if (!IsOpen || inputs is null || a is null)
        {
            ClearAll();
            return;
        }

        // Section names in the detail are links that select the section (the mock's sref); hovers stay plain
        using var links = AccessText.LinkSections();

        // A different watch or semester opened: forget what was expanded and the fold
        string key = $"{_picture.TagId}|{_picture.CourseId}|{_picture.SemesterId}";
        if (key != _openKey)
        {
            _openKey = key;
            _expanded.Clear();
            IsFoldOpen = false;
            LastErrorMessage = null;
        }

        var campuses = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
        string CampusName(string? id) => id is not null && campuses.TryGetValue(id, out var n) ? n : "another campus";
        bool check = _picture.IsCourseOnly;
        string name = _picture.Name;

        IsCheckMode = check;
        Name = name;
        BackText = check ? AccessText.BackToCheckText : AccessText.BackToWatchesText;
        ShowWatching = !check;
        SourceText = check ? AccessText.SourceCheckText(name) : string.Empty;
        RebuildSemesterChoices();

        bool tooLarge = a.Health == AccessHealth.TooLarge;
        var cautions = tooLarge ? [] : Cautions.For(a);
        var phrases = new List<HealthPhrase>();
        if (a.IsSubset) phrases.Add(AccessText.SubsetText(a));
        if (a.PinnedSectionIds.Count > 0)
            phrases.Add($"{AccessText.Plural(a.PinnedSectionIds.Count, "section", "sections")} pinned");
        phrases.AddRange(AccessText.HealthPhrases(inputs, a, CampusName, cautions, _state.KeptInPlace));
        IsHealthAttention = tooLarge || !a.HasSlate || a.MissingComponents.Count > 0;
        HealthPhrases = phrases.Select((p, i) => new HealthPhraseItem(p.Text, AccessText.Plain(p.Tip), p.Warn, IsHealthAttention,
            i < phrases.Count - 1 ? " · " : string.Empty)).ToList();
        HealthLine = AccessText.Plain(string.Join(" · ", phrases.Select(p => p.Text)));

        RebuildPins(inputs);
        RebuildFlags(inputs, a, cautions, name, check, CampusName, tooLarge);
        ShowUntagged = !check && !tooLarge && a.HasUntaggedSections;
        RebuildCourses(inputs, a);

        ShowMultiCampus = !check && _picture.Watch is { } w && (a.SpansCampuses || w.Watch.MultiCampus);
        _multiCampus = _picture.Watch?.Watch.MultiCampus ?? false;
        OnPropertyChanged(nameof(MultiCampus));
        RebuildStepper(inputs, a);
        ShowStepHint = IsStepping;
        RefreshHiddenSections();

        Facts = tooLarge ? [] : AccessText.QuieterFacts(inputs, a);
        HasFacts = Facts.Count > 0;
        RefreshFold();
    }

    /// <summary>Empties every part of the detail (nothing is open).</summary>
    private void ClearAll()
    {
        _openKey = null;
        Name = string.Empty;
        SourceText = string.Empty;
        SemesterChoices.Clear();
        HasSemesterChoice = false;
        HealthPhrases = [];
        HealthLine = string.Empty;
        PinChips.Clear();
        HasPins = false;
        Flags.Clear();
        ShowUntagged = false;
        Courses.Clear();
        ShowTickAll = false;
        ShowMultiCampus = false;
        ShowStepHint = false;
        IsStepping = MixesCampuses = CanGoPrevious = CanGoNext = false;
        PositionText = SlateText = HiddenSectionsText = string.Empty;
        Facts = [];
        HasFacts = false;
    }

    /// <summary>Fills the semester switch (shown when more than one loaded semester has the open thing).</summary>
    private void RebuildSemesterChoices()
    {
        var names = _semesters.SelectedSemesters.ToDictionary(s => s.Semester.Id, s => s.Semester.Name);
        var ids = _picture.SemesterIds;
        SemesterChoices.Clear();
        if (ids.Count > 1)
            foreach (var id in ids)
                SemesterChoices.Add(new AccessSemesterChoiceViewModel(id, names.TryGetValue(id, out var n) ? n : id,
                    id == _picture.SemesterId, () => _picture.SetSemester(id)));
        HasSemesterChoice = SemesterChoices.Count > 1;
    }

    /// <summary>Fills the pin banner's chips.</summary>
    private void RebuildPins(AccessInputs inputs)
    {
        PinChips.Clear();
        foreach (var id in _picture.PinnedSectionIds)
        {
            var section = inputs.Sections.FirstOrDefault(s => s.Id == id);
            if (section is null) continue;
            PinChips.Add(new AccessPinChipViewModel(id, AccessText.Plain(AccessText.SectionLabel(inputs, section)), () => _picture.TogglePin(id)));
        }
        HasPins = PinChips.Count > 0;
    }

    /// <summary>Fills the flags: stranded sections, cautions, missing components, sections with no section type, mismatches.</summary>
    private void RebuildFlags(
        AccessInputs inputs, Analysis a, IReadOnlyList<Caution> cautions, string name, bool check,
        Func<string?, string> campusName, bool tooLarge)
    {
        Flags.Clear();
        if (tooLarge) return;
        foreach (var s in a.StrandedSections)
            Flags.Add(new AccessFlagViewModel(AccessText.StrandedFlagText(inputs, a, s, name, check, campusName), false));
        foreach (var c in cautions)
            Flags.Add(new AccessFlagViewModel(AccessText.CautionText(inputs, c), true));
        foreach (var m in a.MissingComponents)
            Flags.Add(new AccessFlagViewModel(AccessText.MissingComponentFlagText(inputs, m), false));
        // A section whose component type its course doesn't list is a mismatch (below), not "not counted"
        foreach (var s in a.NotCountedSections.Where(s => s.SectionTypeId is null || !inputs.IsComponentType(s.SectionTypeId)))
            Flags.Add(new AccessFlagViewModel(AccessText.NotCountedFlagText(inputs, s), false));
        var chosen = a.ChosenCourseIds.ToHashSet();
        foreach (var m in a.Mismatches.Where(m => chosen.Contains(m.CourseId)))
        {
            var typeName = inputs.SectionTypes.FirstOrDefault(t => t.Id == m.SectionTypeId)?.Name ?? string.Empty;
            Flags.Add(new AccessFlagViewModel(
                AccessText.MismatchSentence(inputs.CourseCode(m.CourseId), typeName, m.Sections.Select(s => s.SectionCode).ToList()), false));
        }
    }

    /// <summary>Fills the course rows, updating them in place when the courses are the same as before.</summary>
    private void RebuildCourses(AccessInputs inputs, Analysis a)
    {
        var covered = _picture.CoveredSections.Select(s => s.Id).ToHashSet();
        var chosen = a.ChosenCourseIds.ToHashSet();
        bool lastOne = chosen.Count == 1;
        var ids = a.OfferedCourseIds;
        if (!Courses.Select(r => r.CourseId).SequenceEqual(ids))
        {
            Courses.Clear();
            foreach (var id in ids)
                Courses.Add(new AccessCourseRowViewModel(_picture, id, inputs.CourseCode(id), ToggleExpanded));
        }
        foreach (var row in Courses)
        {
            var phrases = AccessText.CandidatePhrases(inputs, row.CourseId, covered);
            var lines = phrases.Where(p => p.IsExpandable && _expanded.Contains(p.Key)).Select(p => p.ExpandedLine!).ToList();
            var shown = phrases.Select(p => new AccessCandidatePhraseViewModel(
                p.Key, p.Label, p.Text, p.IsExpandable, p.IsExpandable && _expanded.Contains(p.Key), p.IsNone)).ToList();
            row.Update(chosen.Contains(row.CourseId), lastOne, shown, lines);
        }
        ShowTickAll = a.IsSubset;
    }

    /// <summary>Expands or collapses a course's "N of M" phrase and redraws the rows.</summary>
    /// <param name="key">The phrase's key.</param>
    private void ToggleExpanded(string key)
    {
        if (!_expanded.Remove(key)) _expanded.Add(key);
        Refresh();
    }

    /// <summary>Sets the fold's label from its state.</summary>
    private void RefreshFold() =>
        FoldLabel = IsFoldOpen ? AccessText.FoldLessText : AccessText.FoldMoreText(IsCheckMode, Facts.Count);
}

/// <summary>
/// One phrase of the watch detail's health line as the view draws it (the mock's <c>segsHtml</c>): a phrase with a
/// hover gets a dotted underline, a warning phrase the warning colour, and every phrase the attention colour when
/// the whole line needs attention. The " · " rides on the phrase before it, so a wrapped line never starts with one.
/// </summary>
/// <param name="Text">The phrase.</param>
/// <param name="Tip">Its hover; null for none.</param>
/// <param name="Warn">Whether it is drawn in the warning colour.</param>
/// <param name="Attention">Whether the whole line is drawn in the attention colour.</param>
/// <param name="Trail">" · " after every phrase but the last; empty for the last.</param>
public sealed record HealthPhraseItem(string Text, string? Tip, bool Warn, bool Attention, string Trail)
{
    /// <summary>True when the phrase has a hover (the view underlines it).</summary>
    public bool HasTip => !string.IsNullOrEmpty(Tip);
}
