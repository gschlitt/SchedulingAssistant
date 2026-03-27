using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// Aggregates all entity lookup dictionaries that the schedule grid pipeline needs
/// to convert raw <see cref="Section"/> objects into positioned <see cref="GridBlock"/>
/// objects for a given day column.
///
/// Grouping these into a single record avoids long parameter lists on the individual
/// pipeline step methods (<see cref="ScheduleGridViewModel.BuildFilteredBlocks"/>,
/// <see cref="ScheduleGridViewModel.BuildOverlayBlocks"/>, etc.). If a new entity
/// type is added (e.g. room types), add it here and update
/// <see cref="ScheduleGridViewModel.BuildLookups"/> — the signatures of the pipeline
/// step methods themselves do not need to change.
///
/// All dictionaries are keyed by the entity's <c>string Id</c>.
/// </summary>
internal record GridLookups(
    /// <summary>All sections loaded for the currently selected semester(s).</summary>
    IReadOnlyList<Section> Sections,

    /// <summary>All courses (including inactive), keyed by course ID. Used for grid tile display.</summary>
    IReadOnlyDictionary<string, Course> Courses,

    /// <summary>
    /// Active courses only, keyed by course ID.
    /// Used exclusively for populating the filter options list so that inactive
    /// courses are never presented as filter choices, even if sections that use
    /// them are still visible on the grid.
    /// </summary>
    IReadOnlyDictionary<string, Course> ActiveCourses,

    /// <summary>All instructors, keyed by instructor ID.</summary>
    IReadOnlyDictionary<string, Instructor> Instructors,

    /// <summary>All rooms, keyed by room ID.</summary>
    IReadOnlyDictionary<string, Room> Rooms,

    /// <summary>All subjects, keyed by subject ID.</summary>
    IReadOnlyDictionary<string, Subject> Subjects,

    /// <summary>Campuses, keyed by campus ID.</summary>
    IReadOnlyDictionary<string, Campus> Campuses,

    /// <summary>Section-type property values, keyed by value ID.</summary>
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> SectionTypes,

    /// <summary>Tag property values, keyed by value ID.</summary>
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> Tags,

    /// <summary>Meeting-type property values, keyed by value ID.</summary>
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> MeetingTypes,

    /// <summary>
    /// Fixed set of course level strings ("0XX" through "5+XX"), keyed by those same
    /// strings. Exists so that the level filter can use the same dictionary-lookup
    /// pattern as the other filter dimensions rather than a special code path.
    /// </summary>
    IReadOnlyDictionary<string, string> Levels,

    /// <summary>
    /// Maps semester database ID to its display name (e.g. "fall-2025" → "Fall 2025").
    /// Used when constructing <see cref="GridBlock"/> objects so the renderer can look
    /// up semester colors by name without performing additional database calls per block.
    /// </summary>
    IReadOnlyDictionary<string, string> SemesterIdToName);

/// <summary>
/// Immutable snapshot of the grid filter state, captured once at the start of each
/// reload pass and shared across all pipeline step methods.
///
/// <b>Why a snapshot?</b><br/>
/// <see cref="GridFilterViewModel"/> computes its selection sets on each property
/// access. Capturing a snapshot here means:
/// <list type="number">
///   <item>
///     The pipeline step methods receive plain data and have no runtime dependency on
///     the live <see cref="GridFilterViewModel"/>, making them independently testable.
///   </item>
///   <item>
///     Sentinel values are stripped once here, rather than inside every predicate.
///   </item>
/// </list>
///
/// <b>Sentinel handling</b><br/>
/// The instructor filter list contains two special sentinel entries:
/// <list type="bullet">
///   <item>
///     <see cref="GridFilterViewModel.NotStaffedId"/> — shows ONLY sections with no
///     instructor assignment (<see cref="NotStaffedSelected"/>).
///   </item>
///   <item>
///     <see cref="GridFilterViewModel.EmphasizeUnstaffedId"/> — all sections pass, but
///     staffed sections are visually de-emphasised with a strikethrough in the grid
///     (<see cref="EmphasizeUnstaffedSelected"/>). Does NOT activate
///     <see cref="FilterInstructor"/>.
///   </item>
/// </list>
/// The room filter has one sentinel:
/// <list type="bullet">
///   <item>
///     <see cref="GridFilterViewModel.UnroomedId"/> — matches meetings that have
///     <em>no</em> room assigned (<see cref="UnroomedSelected"/>).
///   </item>
/// </list>
/// These IDs are removed from <see cref="NamedInstructorIds"/> and
/// <see cref="NamedRoomIds"/> respectively. Their presence is stored as separate
/// booleans so that filter predicates can honour them without treating them as real
/// entity IDs.
/// </summary>
internal record FilterSnapshot(
    /// <summary>
    /// Real instructor IDs selected in the filter (sentinel already removed).
    /// Will be empty if only the "Not staffed" sentinel was selected.
    /// </summary>
    HashSet<string> NamedInstructorIds,

    /// <summary>
    /// Real room IDs selected in the filter (sentinel already removed).
    /// Will be empty if only the "Unroomed" sentinel was selected.
    /// </summary>
    HashSet<string> NamedRoomIds,

    /// <summary>Subject IDs selected in the filter. Empty when the subject dimension is inactive.</summary>
    HashSet<string> SubjectIds,

    /// <summary>Campus property-value IDs selected in the filter.</summary>
    HashSet<string> CampusIds,

    /// <summary>Section-type property-value IDs selected in the filter.</summary>
    HashSet<string> SectionTypeIds,

    /// <summary>
    /// Tag property-value IDs selected in the filter.
    /// Tag filtering uses AND logic: a section must carry ALL selected tags to pass.
    /// </summary>
    HashSet<string> TagIds,

    /// <summary>Meeting-type property-value IDs selected in the filter.</summary>
    HashSet<string> MeetingTypeIds,

    /// <summary>Course level strings selected in the filter, e.g. "1XX", "2XX".</summary>
    HashSet<string> LevelIds,

    /// <summary>Course IDs selected in the filter. Empty when the course dimension is inactive.</summary>
    HashSet<string> CourseIds,

    /// <summary>
    /// True when the "Show Unstaffed" sentinel was selected in the instructor filter.
    /// Only sections with no instructor assignments are included when this is true.
    /// </summary>
    bool NotStaffedSelected,

    /// <summary>
    /// True when the "Emphasize Unstaffed" sentinel was selected in the instructor filter.
    /// All sections pass the instructor filter, but sections that ARE staffed receive
    /// <see cref="TileEntry.IsDeemphasized"/> = true so the renderer can apply a
    /// strikethrough to them. Does NOT activate <see cref="FilterInstructor"/>.
    /// </summary>
    bool EmphasizeUnstaffedSelected,

    /// <summary>
    /// True when the "Unroomed" sentinel was selected in the room filter.
    /// Meetings with no room assigned are included when this is true.
    /// </summary>
    bool UnroomedSelected,

    /// <summary>True when any overlay (instructor, room, or tag) is currently active.</summary>
    bool HasOverlay,

    /// <summary>
    /// Which kind of overlay is active: "Instructor", "Room", "Tag", or <c>null</c>
    /// when <see cref="HasOverlay"/> is false.
    /// </summary>
    string? OverlayType,

    /// <summary>
    /// The database ID of the specific entity being highlighted by the overlay
    /// (an instructor ID, a room ID, or a tag ID). <c>null</c> when no overlay is active.
    /// </summary>
    string? SelectedOverlayId)
{
    // ── Derived "is this dimension active?" flags ──────────────────────────────────
    // Computed properties so callers do not need to repeat "Count > 0 || sentinel"
    // throughout the pipeline. Each returns true when at least one criterion is active
    // in that dimension.

    /// <summary>True when the instructor filter dimension has at least one criterion active.</summary>
    public bool FilterInstructor => NamedInstructorIds.Count > 0 || NotStaffedSelected;

    /// <summary>True when the room filter dimension has at least one criterion active.</summary>
    public bool FilterRoom => NamedRoomIds.Count > 0 || UnroomedSelected;

    /// <summary>True when one or more subjects are selected.</summary>
    public bool FilterSubject => SubjectIds.Count > 0;

    /// <summary>True when one or more campuses are selected.</summary>
    public bool FilterCampus => CampusIds.Count > 0;

    /// <summary>True when one or more section types are selected.</summary>
    public bool FilterSectionType => SectionTypeIds.Count > 0;

    /// <summary>
    /// True when one or more tags are selected.
    /// A section must carry ALL selected tags (AND logic) to pass the tag filter.
    /// </summary>
    public bool FilterTag => TagIds.Count > 0;

    /// <summary>True when one or more meeting types are selected.</summary>
    public bool FilterMeetingType => MeetingTypeIds.Count > 0;

    /// <summary>True when one or more course levels are selected.</summary>
    public bool FilterLevel => LevelIds.Count > 0;

    /// <summary>True when one or more courses are selected.</summary>
    public bool FilterCourse => CourseIds.Count > 0;
}
