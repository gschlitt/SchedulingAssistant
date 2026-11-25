using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The open watch (or checked course) of the Access tab and everything the user does to look at it: which courses
/// are ticked, which sections are pinned, whether the grid steps through slates and at which one, and whether the
/// grid also shows other courses. One singleton that the Access tab's detail, the grid's legend bar, the grid's
/// tiles and the right-click menu all read; each redraws on <see cref="Changed"/>. Nothing here is saved (ticks and
/// pins are for looking, never stored).
///
/// <para><b>What is open.</b> Either a watch (a tag, in one semester) or, from Check all courses, one course alone
/// (every section of the course in a semester, the one-campus rule, no tag). With several semesters loaded each is
/// analysed on its own (Q43); <see cref="OpenWatch"/> opens the worst one and <see cref="SetSemester"/> switches.</para>
///
/// <para><b>Analysis.</b> <see cref="Analysis"/> is the engine's analysis of the open thing with the ticked courses
/// and the pins, worked out on first use and kept until <see cref="AccessState.Changed"/> or this class's own change.
/// The slate list (<see cref="Slates"/>) is worked out only when somebody steps.</para>
///
/// <para><b>When the world changes.</b> On <see cref="AccessState.Changed"/> (a save, a reader's refresh, a semester
/// switch, the eye) the cache is dropped and the slate index goes back to the first slate; ticks and pins stay (pins
/// for sections that no longer exist are dropped when the analysis is next made). If the watch is gone (unwatched,
/// tag deleted, no section carries it in any loaded semester) or the checked course has no sections left, the
/// picture closes itself and raises <see cref="Changed"/>. This class subscribes to the state before the views are
/// built, so its cache is dropped before the grid reloads.</para>
///
/// <para>Runs on the UI thread; not thread-safe. Lives as long as the services of one database.</para>
/// </summary>
public sealed class AccessPicture
{
    private readonly AccessState _state;

    private string? _tagId;
    private string? _courseId;
    private string? _semesterId;
    private HashSet<string>? _ticked;
    private readonly List<string> _pins = [];
    private bool _stepping;
    private int _slateIndex;

    private Analysis? _analysis;
    private IReadOnlySet<string>? _coveredIds;
    private IReadOnlyList<IReadOnlyList<Section>>? _slates;

    /// <summary>Raised when anything the picture holds changed: what is open, the ticks, the pins, stepping, Show other courses.</summary>
    public event Action? Changed;

    /// <summary>Creates the picture and subscribes to the state (never unsubscribed: both live as long as one database's services).</summary>
    /// <param name="state">The access results.</param>
    public AccessPicture(AccessState state)
    {
        _state = state;
        _state.Changed += OnStateChanged;
    }

    // ── What is open ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>True while a watch or a course is open.</summary>
    public bool IsOpen => _semesterId is not null && (_tagId is not null || _courseId is not null);

    /// <summary>True when one course is open alone (Check all courses); false for a watch.</summary>
    public bool IsCourseOnly => IsOpen && _courseId is not null;

    /// <summary>The open watch's tag id; null when none, or when a course is open alone.</summary>
    public string? TagId => IsCourseOnly ? null : _tagId;

    /// <summary>The checked course's id; null unless a course is open alone.</summary>
    public string? CourseId => _courseId;

    /// <summary>The semester the open thing is analysed in; null when nothing is open.</summary>
    public string? SemesterId => IsOpen ? _semesterId : null;

    /// <summary>The semester's results (inputs and watches) for the open thing; null when nothing is open or the semester is not loaded.</summary>
    public SemesterAccess? Semester =>
        IsOpen && _state.Current.Semesters.TryGetValue(_semesterId!, out var s) ? s : null;

    /// <summary>The semester view the open thing is analysed over; null when nothing is open.</summary>
    public AccessInputs? Inputs => Semester?.Inputs;

    /// <summary>The open watch's results (its tag and watch); null when nothing is open or a course is open alone.</summary>
    public WatchAccess? Watch => IsCourseOnly ? null : Semester?.Watches.FirstOrDefault(w => w.Tag.Id == _tagId);

    /// <summary>The name to show: the watch's (its tag's), or the checked course's calendar code; empty when nothing is open.</summary>
    public string Name
    {
        get
        {
            if (!IsOpen) return string.Empty;
            if (IsCourseOnly) return Inputs?.CourseCode(_courseId!) ?? string.Empty;
            return Watch?.Tag.Name ?? string.Empty;
        }
    }

    /// <summary>
    /// The ids of the loaded semesters the open thing exists in, in snapshot order (a watch present in the semester, a
    /// course with sections in it). The detail shows a switch when there is more than one.
    /// </summary>
    public IReadOnlyList<string> SemesterIds =>
        !IsOpen ? [] : _state.Current.Semesters.Values.Where(Has).Select(s => s.SemesterId).ToList();

    /// <summary>The sections the open thing covers: the watch's tagged sections, or all the course's sections.</summary>
    public IReadOnlyList<Section> CoveredSections
    {
        get
        {
            var semester = Semester;
            if (semester is null) return [];
            if (IsCourseOnly)
                return semester.Inputs.SectionsByCourse.TryGetValue(_courseId!, out var mine) ? mine : [];
            return Watch is { } w ? w.Watch.CoveredSections(semester.Inputs) : [];
        }
    }

    /// <summary>Whether a section is one the open thing covers.</summary>
    /// <param name="sectionId">The section id.</param>
    /// <returns><c>true</c> when it is covered; always <c>false</c> when nothing is open.</returns>
    public bool Covers(string sectionId)
    {
        if (!IsOpen) return false;
        _coveredIds ??= CoveredSections.Select(s => s.Id).ToHashSet();
        return _coveredIds.Contains(sectionId);
    }

    /// <summary>
    /// Opens a watch. The semester is the given one when the watch exists in it, else the one where the watch needs
    /// the most attention. Ticks, pins and stepping start afresh; Show other courses stays as it was.
    /// </summary>
    /// <param name="tagId">The watched tag's id.</param>
    /// <param name="semesterId">The semester to open it in, or null for the worst.</param>
    /// <returns><c>true</c> when opened; <c>false</c> (and nothing changes) when the tag is not a watch in any loaded semester.</returns>
    public bool OpenWatch(string tagId, string? semesterId = null)
    {
        var candidates = _state.Current.Semesters.Values
            .Where(s => s.Watches.Any(w => w.Tag.Id == tagId)).ToList();
        if (candidates.Count == 0) return false;
        var chosen = semesterId is not null ? candidates.FirstOrDefault(s => s.SemesterId == semesterId) : null;
        chosen ??= candidates.OrderByDescending(s => Rank(s.Watches.First(w => w.Tag.Id == tagId).Analysis.Health)).First();
        Begin(tagId, null, chosen.SemesterId);
        return true;
    }

    /// <summary>
    /// Opens one course alone (check mode): every section of the course in the semester, the one-campus rule, no tag.
    /// </summary>
    /// <param name="courseId">The course id.</param>
    /// <param name="semesterId">The semester to open it in, or null for the first loaded semester the course has sections in.</param>
    /// <returns><c>true</c> when opened; <c>false</c> (nothing changes) when the course has no sections in a loaded semester.</returns>
    public bool OpenCourse(string courseId, string? semesterId = null)
    {
        var candidates = _state.Current.Semesters.Values
            .Where(s => s.Inputs.SectionsByCourse.ContainsKey(courseId)).ToList();
        if (candidates.Count == 0) return false;
        var chosen = semesterId is not null ? candidates.FirstOrDefault(s => s.SemesterId == semesterId) : null;
        Begin(null, courseId, (chosen ?? candidates[0]).SemesterId);
        return true;
    }

    /// <summary>Closes the open watch or course. Does nothing when nothing is open.</summary>
    public void Close()
    {
        if (!IsOpen && _tagId is null && _courseId is null) return;
        _tagId = null;
        _courseId = null;
        _semesterId = null;
        ResetLooking();
        Changed?.Invoke();
    }

    /// <summary>Looks at the open thing in another loaded semester. Ticks, pins and stepping start afresh.</summary>
    /// <param name="semesterId">The semester; ignored when the open thing is not in it.</param>
    public void SetSemester(string semesterId)
    {
        if (!IsOpen || semesterId == _semesterId) return;
        if (!_state.Current.Semesters.TryGetValue(semesterId, out var s) || !Has(s)) return;
        _semesterId = semesterId;
        ResetLooking();
        Changed?.Invoke();
    }

    // ── Ticks ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ticked courses; <c>null</c> means all of the watch's courses (the default).</summary>
    public IReadOnlySet<string>? TickedCourseIds => _ticked;

    /// <summary>
    /// Ticks or unticks a course (rule 17: ticks are for looking, never saved). At least one course stays ticked: the
    /// last ticked course cannot be unticked. Ticking every course goes back to "all". Slate stepping starts again at
    /// the first slate.
    /// </summary>
    /// <param name="courseId">The course id.</param>
    /// <param name="ticked">The new state.</param>
    /// <returns><c>true</c> when something changed; <c>false</c> when the request was refused or already so.</returns>
    public bool SetCourseTicked(string courseId, bool ticked)
    {
        var offered = FullAnalysisCourseIds();
        if (!offered.Contains(courseId)) return false;
        var now = _ticked is null ? offered.ToHashSet() : _ticked.Where(offered.Contains).ToHashSet();
        if (now.Contains(courseId) == ticked) return false;
        if (ticked) now.Add(courseId);
        else if (now.Count <= 1) return false;
        else now.Remove(courseId);
        _ticked = now.Count == offered.Count ? null : now;
        InvalidateAnalysis();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ticks every course again.</summary>
    public void TickAll()
    {
        if (_ticked is null) return;
        _ticked = null;
        InvalidateAnalysis();
        Changed?.Invoke();
    }

    /// <summary>The courses the open thing offers, whatever is ticked (the ticks' universe).</summary>
    private IReadOnlyList<string> FullAnalysisCourseIds() => Analysis?.OfferedCourseIds ?? [];

    // ── Pins ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The pinned section ids, in the order they were pinned.</summary>
    public IReadOnlyList<string> PinnedSectionIds => _pins;

    /// <summary>Whether a section is pinned.</summary>
    /// <param name="sectionId">The section id.</param>
    /// <returns><c>true</c> when pinned.</returns>
    public bool IsPinned(string sectionId) => _pins.Contains(sectionId);

    /// <summary>
    /// Pins a section, or unpins it when it is pinned (rule 18): only slates that use it count. Only a section the open
    /// thing covers can be pinned. Stepping starts again at the first slate.
    /// </summary>
    /// <param name="sectionId">The section id.</param>
    /// <returns><c>true</c> when something changed.</returns>
    public bool TogglePin(string sectionId)
    {
        if (!_pins.Remove(sectionId))
        {
            if (!Covers(sectionId)) return false;
            _pins.Add(sectionId);
        }
        InvalidateAnalysis();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Unpins every section.</summary>
    public void ClearPins()
    {
        if (_pins.Count == 0) return;
        _pins.Clear();
        InvalidateAnalysis();
        Changed?.Invoke();
    }

    // ── The analysis and the slates ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The analysis of the open thing with the ticked courses and the pins; null when nothing is open or its semester
    /// is not loaded. Worked out on first use and kept.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Never in practice: the default cap is used.</exception>
    public Analysis? Analysis
    {
        get
        {
            if (_analysis is not null) return _analysis;
            var semester = Semester;
            if (semester is null) return null;
            var inputs = semester.Inputs;
            var present = inputs.Sections.Select(s => s.Id).ToHashSet();
            _pins.RemoveAll(id => !present.Contains(id));
            IReadOnlySet<string> pins = _pins.ToHashSet();
            if (IsCourseOnly)
            {
                var covered = inputs.SectionsByCourse.TryGetValue(_courseId!, out var mine) ? mine : [];
                _analysis = AccessAnalyzer.Analyze(inputs, covered, false, null, pins);
            }
            else if (Watch is { } w)
            {
                _analysis = AccessAnalyzer.Analyze(inputs, w.Watch, _ticked, pins);
            }
            return _analysis;
        }
    }

    /// <summary>
    /// Whether the grid can step through slates: there is a full slate and the watch is not the one-section-per-
    /// component case (which has one slate or none, and says whether the sections fit together instead).
    /// </summary>
    public bool CanStep => Analysis is { HasSlate: true, IsSingleSectionCase: false, HasNoGroups: false };

    /// <summary>Whether the grid is stepping through slates (<see cref="CanStep"/> and started).</summary>
    public bool IsStepping => _stepping && CanStep;

    /// <summary>
    /// The slates in the stable order of Q34, at most <see cref="AccessAnalyzer.DefaultCap"/> of them; empty when
    /// there is no slate or nothing is open. Worked out only when asked for, and kept.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Section>> Slates
    {
        get
        {
            if (_slates is not null) return _slates;
            var a = Analysis;
            _slates = a is null || !a.HasSlate || a.HasNoGroups
                ? []
                : new SlateSearch(a.Groups, a.OneCampusRule).List(AccessAnalyzer.DefaultCap);
            return _slates;
        }
    }

    /// <summary>The slate being shown, counted from zero (always within <see cref="Slates"/> while stepping).</summary>
    public int SlateIndex => Slates.Count == 0 ? 0 : Math.Min(_slateIndex, Slates.Count - 1);

    /// <summary>The slate being shown while stepping; null when not stepping.</summary>
    public IReadOnlyList<Section>? CurrentSlate => IsStepping && Slates.Count > 0 ? Slates[SlateIndex] : null;

    /// <summary>Starts stepping through slates at the first one, when that can be done.</summary>
    public void StartStepping()
    {
        if (!CanStep || _stepping) return;
        _stepping = true;
        _slateIndex = 0;
        Changed?.Invoke();
    }

    /// <summary>Stops stepping (the legend bar's Clear).</summary>
    public void StopStepping()
    {
        if (!_stepping) return;
        _stepping = false;
        Changed?.Invoke();
    }

    /// <summary>Moves to another slate, staying within the list.</summary>
    /// <param name="delta">Minus one for the previous slate, one for the next.</param>
    public void Step(int delta)
    {
        if (!IsStepping) return;
        int next = Math.Clamp(SlateIndex + delta, 0, Slates.Count - 1);
        if (next == _slateIndex) return;
        _slateIndex = next;
        Changed?.Invoke();
    }

    // ── Internals ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Starts looking at something afresh (ticks, pins, stepping reset) and tells the views.</summary>
    private void Begin(string? tagId, string? courseId, string semesterId)
    {
        _tagId = tagId;
        _courseId = courseId;
        _semesterId = semesterId;
        ResetLooking();
        Changed?.Invoke();
    }

    /// <summary>Forgets ticks, pins and stepping (what the user did to look at one thing).</summary>
    private void ResetLooking()
    {
        _ticked = null;
        _pins.Clear();
        _stepping = false;
        _slateIndex = 0;
        InvalidateAnalysis();
    }

    /// <summary>Drops the analysis and the slate list (the stepping position goes back to the first slate).</summary>
    private void InvalidateAnalysis()
    {
        _analysis = null;
        _coveredIds = null;
        _slates = null;
        _slateIndex = 0;
    }

    /// <summary>Whether the open thing exists in a semester's results.</summary>
    private bool Has(SemesterAccess semester) =>
        IsCourseOnly
            ? semester.Inputs.SectionsByCourse.ContainsKey(_courseId!)
            : semester.Watches.Any(w => w.Tag.Id == _tagId);

    /// <summary>How bad a health is, for picking the worst semester.</summary>
    private static int Rank(AccessHealth health) =>
        health is AccessHealth.Attention or AccessHealth.TooLarge ? 2 : health == AccessHealth.Fragile ? 1 : 0;

    /// <summary>
    /// The access state changed: the cache goes and the slate index starts again; the picture follows the open thing
    /// to another loaded semester when its own is gone, and closes when it exists nowhere any more.
    /// </summary>
    private void OnStateChanged()
    {
        InvalidateAnalysis();
        if (!IsOpen) return;
        try
        {
            var semesters = _state.Current.Semesters;
            if (semesters.TryGetValue(_semesterId!, out var here) && Has(here)) return;
            var other = semesters.Values.FirstOrDefault(Has);
            if (other is not null)
            {
                _semesterId = other.SemesterId;
                ResetLooking();
                Changed?.Invoke();
            }
            else Close();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessPicture.OnStateChanged");
            Close();
        }
    }
}
