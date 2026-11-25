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

    /// <summary>The watch's source line: the start, before the name (empty in check mode).</summary>
    [ObservableProperty] private string _sourceLead = string.Empty;

    /// <summary>The watch's source line: the name in bold (empty in check mode).</summary>
    [ObservableProperty] private string _sourceName = string.Empty;

    /// <summary>The watch's source line: the end, after the name. In check mode the whole source line.</summary>
    [ObservableProperty] private string _sourceTail = string.Empty;

    /// <summary>The Watching button's text.</summary>
    public string WatchingText => AccessText.WatchingText;

    /// <summary>True when the Watching button shows (a watch, not a checked course).</summary>
    [ObservableProperty] private bool _showWatching;

    /// <summary>True while this computer can save: the Watching button and the Multi-campus box work.</summary>
    [ObservableProperty] private bool _canChange;

    /// <summary>The Watching button's tooltip: what it does, or why it is off.</summary>
    [ObservableProperty] private string _watchingTip = AccessText.UnwatchTip;

    /// <summary>The Multi-campus box's tooltip.</summary>
    public string MultiCampusTip => CanChange ? AccessText.MultiCampusTip : AccessText.ReadOnlyTip;

    /// <summary>The semesters the open thing can be looked at in; shown only when there is more than one.</summary>
    public ObservableCollection<AccessSemesterChoiceViewModel> SemesterChoices { get; } = [];

    /// <summary>True when more than one loaded semester has the open thing (the switch shows).</summary>
    [ObservableProperty] private bool _hasSemesterChoice;

    /// <summary>The health line's phrases, in order (the view puts " · " between them).</summary>
    [ObservableProperty] private IReadOnlyList<string> _healthPhrases = [];

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

    /// <summary>True when the hint about pinning and stepping shows.</summary>
    [ObservableProperty] private bool _showStepHint;

    /// <summary>The hint about pinning and stepping.</summary>
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

    /// <summary>Goes back (to the list, or to Check all courses in check mode).</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Stops watching the open watch's tag (the Watching button). The tag stays.</summary>
    public IRelayCommand UnwatchCommand { get; }

    /// <summary>
    /// Creates the detail and fills it from the picture.
    /// </summary>
    /// <param name="picture">The open watch, ticks and pins.</param>
    /// <param name="state">The access results (the detail redraws when they change).</param>
    /// <param name="toggler">Stops watching a tag and saves the Multi-campus box; says whether this computer can save.</param>
    /// <param name="semesters">The loaded semesters (their names, for the switch).</param>
    /// <param name="campusRepo">Campus names for the sentences.</param>
    public AccessWatchDetailViewModel(
        AccessPicture picture, AccessState state, AccessWatchToggler toggler, SemesterContext semesters, ICampusRepository campusRepo)
    {
        _picture = picture;
        _state = state;
        _toggler = toggler;
        _semesters = semesters;
        _campusRepo = campusRepo;
        BackCommand = new RelayCommand(() => BackRequested?.Invoke());
        UnwatchCommand = new RelayCommand(Unwatch);
        UnpinAllCommand = new RelayCommand(_picture.ClearPins);
        TickAllCommand = new RelayCommand(_picture.TickAll);
        ToggleFoldCommand = new RelayCommand(() => { IsFoldOpen = !IsFoldOpen; RefreshFold(); });
        // Never unsubscribed: singletons that live as long as the services of one database.
        _picture.Changed += Refresh;
        _state.Changed += Refresh;
        _toggler.CanToggleChanged += Refresh;
        Refresh();
    }

    /// <summary>Stops watching the open watch's tag; a refused or failed save is shown inline.</summary>
    private void Unwatch()
    {
        if (_picture.TagId is not { } tagId) return;
        try
        {
            LastErrorMessage = null;
            _toggler.SetWatched(tagId, false);
        }
        catch (InvalidOperationException)
        {
            LastErrorMessage = AccessText.ReadOnlyTip;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessWatchDetailViewModel.Unwatch");
            LastErrorMessage = "The watch couldn't be changed. Your work is safe; try again.";
        }
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
        SourceLead = check ? string.Empty : AccessText.SourceWatchLead;
        SourceName = check ? string.Empty : name;
        SourceTail = check ? AccessText.SourceCheckText(name) : AccessText.SourceWatchTail;
        WatchingTip = CanChange ? AccessText.UnwatchTip : AccessText.ReadOnlyTip;
        RebuildSemesterChoices();

        bool tooLarge = a.Health == AccessHealth.TooLarge;
        var cautions = tooLarge ? [] : Cautions.For(a);
        var phrases = new List<string>();
        if (a.IsSubset) phrases.Add(AccessText.SubsetText(a));
        if (a.PinnedSectionIds.Count > 0)
            phrases.Add($"{AccessText.Plural(a.PinnedSectionIds.Count, "section", "sections")} pinned");
        phrases.AddRange(AccessText.HealthPhrases(inputs, a, CampusName, cautions));
        HealthPhrases = phrases;
        HealthLine = string.Join(" · ", phrases);
        IsHealthAttention = tooLarge || !a.HasSlate || a.MissingComponents.Count > 0;

        RebuildPins(inputs);
        RebuildFlags(inputs, a, cautions, name, check, CampusName, tooLarge);
        ShowUntagged = !check && !tooLarge && a.HasUntaggedSections;
        RebuildCourses(inputs, a);

        ShowMultiCampus = !check && _picture.Watch is { } w && (a.SpansCampuses || w.Watch.MultiCampus);
        _multiCampus = _picture.Watch?.Watch.MultiCampus ?? false;
        OnPropertyChanged(nameof(MultiCampus));
        ShowStepHint = _picture.CanStep;

        Facts = tooLarge ? [] : AccessText.QuieterFacts(inputs, a);
        HasFacts = Facts.Count > 0;
        RefreshFold();
    }

    /// <summary>Empties every part of the detail (nothing is open).</summary>
    private void ClearAll()
    {
        _openKey = null;
        Name = string.Empty;
        SourceLead = SourceName = SourceTail = string.Empty;
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
            PinChips.Add(new AccessPinChipViewModel(id, AccessText.SectionLabel(inputs, section), () => _picture.TogglePin(id)));
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
