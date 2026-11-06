using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Answers the two questions the Section list and the Schedule view both ask about subject colours (colour by
/// subject, <c>designs/subject-colours.md</c> Phase 5): <b>are colours on?</b> (<see cref="IsOn"/>) and <b>what is
/// this section's colour?</b> (<see cref="ColorFor"/>). Both views ask this one object, so they always agree (Phase 2,
/// rule 3). A singleton in the per-database service provider. Everything runs on the UI thread; not thread-safe.
///
/// <para><b>Colours on?</b> (Phase 2) Off when none of this database's subjects has a colour, whatever the display
/// mode (rule 1: nothing set, nothing shown). Otherwise <see cref="SubjectColorDisplay.Always"/> is on,
/// <see cref="SubjectColorDisplay.Never"/> is off, and <see cref="SubjectColorDisplay.OnlyWhenSeveral"/> is on when
/// the in-focus sections of <see cref="SectionStore.Sections"/> (the open semester(s), tested with
/// <see cref="FocusService.Matches"/>) span two or more subjects. Grid filters play no part.</para>
///
/// <para><b>A section's colour.</b> Section → its course → <see cref="Course.SubjectId"/> → that subject's
/// <see cref="Subject.CalendarAbbreviation"/> → the colour store. Any missing link means no colour.</para>
///
/// <para><b>Caching and <see cref="Changed"/>.</b> The course → subject map, the on/off answer and each course's
/// colour are cached. They are dropped on the store's <see cref="SubjectColorStore.Changed"/>,
/// <see cref="FocusService.FocusChanged"/> and <see cref="SectionStore.SectionsChanged"/>, and <see cref="Changed"/>
/// is raised only when an answer could differ: a colour or display change while colours are (or become) on; a focus
/// change that turns colours on or off; a section reload that turns them on or off, or (while on) changes which
/// subject a course belongs to or a subject's abbreviation. So saving one section does not repaint every card.</para>
///
/// <para><b>Event ordering.</b> <see cref="FocusService"/> is a constructor dependency, so dependency injection builds
/// it first and its <see cref="SectionStore.SectionsChanged"/> handler (which drops its own caches) runs before this
/// presenter asks it <see cref="FocusService.Matches"/>. Views that take this presenter as a constructor dependency
/// are likewise answered from fresh caches in their own <c>SectionsChanged</c> handlers.</para>
///
/// <para>No Avalonia types: colours are handed out as <c>#RRGGBB</c> strings and the views turn them into brushes.</para>
/// </summary>
public sealed class SubjectColorPresenter : IDisposable
{
    private readonly SubjectColorStore _store;
    private readonly FocusService _focus;
    private readonly SectionStore _sectionStore;
    private readonly ICourseRepository _courseRepo;
    private readonly ISubjectRepository _subjectRepo;

    /// <summary>
    /// Course id → (subject id, trimmed subject abbreviation), built lazily from the repositories; null when dropped.
    /// A course whose subject no longer exists maps to an empty abbreviation.
    /// </summary>
    private Dictionary<string, CourseSubject>? _courseSubjects;

    /// <summary>The abbreviations of every subject in this database, built with <see cref="_courseSubjects"/>.</summary>
    private List<string>? _abbreviations;

    /// <summary>The cached on/off answer; null when it must be worked out again.</summary>
    private bool? _isOn;

    /// <summary>Course id → its colour (<c>#RRGGBB</c>, or empty for none), filled as cards and tiles ask.</summary>
    private readonly Dictionary<string, string> _colorByCourseId = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the presenter and subscribes to the three events that can change its answers.
    /// </summary>
    /// <param name="store">The subject colours and display mode.</param>
    /// <param name="focus">
    /// The subject focus. Taken here (not looked up later) so it is constructed, and subscribes to
    /// <see cref="SectionStore.SectionsChanged"/>, before this presenter does.
    /// </param>
    /// <param name="sectionStore">The open semester(s)' sections; its reloads drop the caches.</param>
    /// <param name="courseRepo">Courses, for section → course → subject.</param>
    /// <param name="subjectRepo">Subjects, for subject → abbreviation and this database's set of abbreviations.</param>
    public SubjectColorPresenter(
        SubjectColorStore store,
        FocusService focus,
        SectionStore sectionStore,
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo)
    {
        _store = store;
        _focus = focus;
        _sectionStore = sectionStore;
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;

        _store.Changed += OnStoreChanged;
        _focus.FocusChanged += OnFocusChanged;
        _sectionStore.SectionsChanged += OnSectionsChanged;
    }

    /// <summary>
    /// Raised (on the UI thread) after the caches were dropped, when <see cref="IsOn"/> or some section's
    /// <see cref="ColorFor"/> answer may now be different. Views refresh or reload their chips.
    /// </summary>
    public event Action? Changed;

    // ── The two questions ──────────────────────────────────────────────────────

    /// <summary>Whether subject colours are shown right now (see the class remarks). Cached.</summary>
    public bool IsOn => _isOn ??= ComputeIsOn();

    /// <summary>
    /// The chip colour for a section.
    /// </summary>
    /// <param name="section">The section to colour.</param>
    /// <returns>
    /// <c>#RRGGBB</c>, or an empty string when colours are off, the section has no course, the course has no subject,
    /// or the subject's abbreviation has no colour.
    /// </returns>
    public string ColorFor(Section section)
    {
        if (!IsOn || string.IsNullOrEmpty(section.CourseId)) return "";

        if (_colorByCourseId.TryGetValue(section.CourseId, out var cached)) return cached;

        var color = CourseSubjects.TryGetValue(section.CourseId, out var cs) && cs.Abbreviation.Length > 0
            ? _store.GetColor(cs.Abbreviation) ?? ""
            : "";
        _colorByCourseId[section.CourseId] = color;
        return color;
    }

    /// <summary>
    /// The marker colour for a subject, by its calendar abbreviation (the Schedule View header's subject chips,
    /// spec 1.2.4 item 2.14). Same on/off rule as <see cref="ColorFor"/>, so the header agrees with the tiles.
    /// </summary>
    /// <param name="abbreviation">The subject's calendar abbreviation; null or blank means no colour.</param>
    /// <returns><c>#RRGGBB</c>, or an empty string when colours are off or the abbreviation has no colour.</returns>
    public string ColorForSubject(string? abbreviation)
    {
        var abbr = abbreviation?.Trim() ?? "";
        if (abbr.Length == 0 || !IsOn) return "";
        return _store.GetColor(abbr) ?? "";
    }

    /// <summary>
    /// Drops every cache and raises <see cref="Changed"/> if colours were or now are on. For code that changes a
    /// course's subject or a subject's abbreviation without reloading the <see cref="SectionStore"/> (the reload
    /// already does this).
    /// </summary>
    public void Invalidate()
    {
        var wasOn = _isOn == true;
        DropAll();
        if (wasOn || IsOn) Changed?.Invoke();
    }

    /// <summary>Unsubscribes from the store, the focus and the section store. Safe to call more than once.</summary>
    public void Dispose()
    {
        _store.Changed -= OnStoreChanged;
        _focus.FocusChanged -= OnFocusChanged;
        _sectionStore.SectionsChanged -= OnSectionsChanged;
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    /// <summary>
    /// A colour or the display mode changed. Raises <see cref="Changed"/> unless colours were off before and are
    /// still off (then nothing on screen can differ).
    /// </summary>
    private void OnStoreChanged()
    {
        var wasOn = _isOn == true;
        _isOn = null;
        _colorByCourseId.Clear();
        if (wasOn || IsOn) Changed?.Invoke();
    }

    /// <summary>
    /// The focus changed, which can only change the on/off answer (a course's colour does not depend on the focus).
    /// Raises <see cref="Changed"/> when that answer flips.
    /// </summary>
    private void OnFocusChanged()
    {
        var wasOn = _isOn == true;
        _isOn = null;
        _colorByCourseId.Clear();
        if (wasOn != IsOn) Changed?.Invoke();
    }

    /// <summary>
    /// The section cache reloaded (a save, a semester change, a course edit that reloads it). Rebuilds the course →
    /// subject map and raises <see cref="Changed"/> when the on/off answer flips, or when colours are on and some
    /// course's subject or abbreviation differs from before.
    /// </summary>
    private void OnSectionsChanged()
    {
        var wasOn = _isOn == true;
        var oldMap = _courseSubjects;
        DropAll();

        var isOn = IsOn;
        if (wasOn != isOn || (isOn && !SameMap(oldMap, CourseSubjects)))
            Changed?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>One course's subject id and the subject's trimmed abbreviation (empty when the subject is gone).</summary>
    private readonly record struct CourseSubject(string SubjectId, string Abbreviation);

    /// <summary>The course → subject map, built when absent.</summary>
    private Dictionary<string, CourseSubject> CourseSubjects
    {
        get
        {
            if (_courseSubjects is null) BuildMaps();
            return _courseSubjects!;
        }
    }

    /// <summary>This database's subject abbreviations, built when absent.</summary>
    private List<string> Abbreviations
    {
        get
        {
            if (_abbreviations is null) BuildMaps();
            return _abbreviations!;
        }
    }

    /// <summary>Drops every cache so each answer is worked out again on next use.</summary>
    private void DropAll()
    {
        _courseSubjects = null;
        _abbreviations = null;
        _isOn = null;
        _colorByCourseId.Clear();
    }

    /// <summary>
    /// Reads courses and subjects into <see cref="_courseSubjects"/> and <see cref="_abbreviations"/>. If the read
    /// fails, both are left empty (so colours read as off) and the failure is logged; the next event retries.
    /// </summary>
    private void BuildMaps()
    {
        var map = new Dictionary<string, CourseSubject>(StringComparer.Ordinal);
        var abbreviations = new List<string>();
        try
        {
            var abbrBySubjectId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var subject in _subjectRepo.GetAll())
            {
                var abbr = subject.CalendarAbbreviation?.Trim() ?? "";
                abbrBySubjectId.TryAdd(subject.Id, abbr);
                if (abbr.Length > 0) abbreviations.Add(abbr);
            }

            foreach (var course in _courseRepo.GetAll())
            {
                var abbr = course.SubjectId is not null && abbrBySubjectId.TryGetValue(course.SubjectId, out var a)
                    ? a
                    : "";
                map.TryAdd(course.Id, new CourseSubject(course.SubjectId ?? "", abbr));
            }
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[SubjectColorPresenter] Could not read courses or subjects; colours off: {ex.Message}");
            map.Clear();
            abbreviations.Clear();
        }

        _courseSubjects = map;
        _abbreviations = abbreviations;
    }

    /// <summary>Works out <see cref="IsOn"/> from scratch (see the class remarks).</summary>
    private bool ComputeIsOn()
    {
        if (!_store.HasAnyColor(Abbreviations)) return false;

        return _store.Display switch
        {
            SubjectColorDisplay.Always => true,
            SubjectColorDisplay.Never => false,
            _ => SpansSeveralSubjects(),
        };
    }

    /// <summary>
    /// Whether the in-focus sections of the open semester(s) belong to two or more subjects. Stops at the second.
    /// Sections with no course, or a course with no subject, count for none.
    /// </summary>
    private bool SpansSeveralSubjects()
    {
        string? first = null;
        foreach (var section in _sectionStore.Sections)
        {
            if (string.IsNullOrEmpty(section.CourseId)
                || !CourseSubjects.TryGetValue(section.CourseId, out var cs)
                || cs.SubjectId.Length == 0
                || !_focus.Matches(section))
                continue;

            if (first is null) first = cs.SubjectId;
            else if (!string.Equals(first, cs.SubjectId, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Whether two course → subject maps hold exactly the same entries. A null map never matches.</summary>
    private static bool SameMap(Dictionary<string, CourseSubject>? a, Dictionary<string, CourseSubject> b)
    {
        if (a is null || a.Count != b.Count) return false;
        foreach (var (courseId, cs) in a)
            if (!b.TryGetValue(courseId, out var other) || other != cs) return false;
        return true;
    }
}
