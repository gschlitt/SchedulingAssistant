using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// Represents one colored segment in the semester line display, e.g. "Fall" with orange background.
/// </summary>
public record SemesterLineSegment(string DisplayText, IBrush? Background, IBrush? Border);

public partial class ScheduleGridViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly SectionPropertyRepository _propertyRepo;
    private readonly SemesterContext _semesterContext;
    private readonly AcademicUnitService _academicUnitService;
    private readonly SectionStore _sectionStore;
    private readonly SectionChangeNotifier _changeNotifier;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly WriteLockService _lockService;

    /// <summary>
    /// Tracks the set of semester IDs from the last reload so <see cref="ReloadCore"/>
    /// can detect when the semester selection has changed and clear the filter state.
    /// </summary>
    private string _lastSemesterKey = string.Empty;

    [ObservableProperty] private GridData _gridData = GridData.Empty;
    [ObservableProperty] private string? _selectedSectionId;
    [ObservableProperty] private string? _lastErrorMessage;

    /// <summary>Display name of the selected semester, e.g. "2025-2026 — Fall"</summary>
    [ObservableProperty] private string _semesterLine = string.Empty;

    /// <summary>Colored semester segments for the semester line (each semester with its background color).</summary>
    [ObservableProperty] private List<SemesterLineSegment> _semesterLineSegments = [];

    /// <summary>Selected subject filter names, e.g. "History · Mathematics". Empty when no subject filter active.</summary>
    [ObservableProperty] private string _subjectFilterSummary = string.Empty;

    /// <summary>e.g. "12 sections · 28 meetings shown"</summary>
    [ObservableProperty] private string _statsLine = string.Empty;

    /// <summary>Academic Unit name, e.g. "College of Arts & Sciences"</summary>
    [ObservableProperty] private string _academicUnitName = string.Empty;

    /// <summary>Filter state. Exposed so the view can bind to it.</summary>
    public GridFilterViewModel Filter { get; } = new();

    /// <summary>State for the right-click context menu on section tiles.</summary>
    public SectionContextMenuViewModel ContextMenu { get; }

    /// <summary>
    /// True when the write lock is held by this instance and write-capable UI
    /// (such as the right-click context menu) should be available.
    /// </summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    public ScheduleGridViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        SubjectRepository subjectRepo,
        SectionPropertyRepository propertyRepo,
        SemesterContext semesterContext,
        AcademicUnitService academicUnitService,
        SectionStore sectionStore,
        SectionChangeNotifier changeNotifier,
        InstructorCommitmentRepository commitmentRepo,
        WriteLockService lockService)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _subjectRepo = subjectRepo;
        _propertyRepo = propertyRepo;
        _semesterContext = semesterContext;
        _academicUnitService = academicUnitService;
        _sectionStore = sectionStore;
        _changeNotifier = changeNotifier;
        _commitmentRepo = commitmentRepo;
        _lockService = lockService;

        // After a context-menu save, refresh the shared section cache so all views
        // (including this one via SectionsChanged below) reload in one shot.
        ContextMenu = new SectionContextMenuViewModel(sectionRepo, () =>
        {
            var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
            _sectionStore.Reload(_sectionRepo, semIds);
        }, lockService);

        LoadAcademicUnitName();

        Filter.FilterChanged += Reload;

        // Reload whenever sections change (inserts, updates, deletes from any source).
        _sectionStore.SectionsChanged += Reload;

        // SectionChangeNotifier is now used exclusively for commitment CRUD reloads.
        // Commitment changes are not section-data changes and are not cached in SectionStore.
        _changeNotifier.SectionChanged += Reload;

        // Keep SelectedSectionId in sync with the store's single source of truth.
        _sectionStore.SelectionChanged += id => SelectedSectionId = id;

        Reload();
    }

    private void LoadAcademicUnitName()
    {
        var unit = _academicUnitService.GetUnit();
        AcademicUnitName = unit?.Name ?? string.Empty;
    }

    /// <summary>
    /// Called by the view when a tile is clicked.
    /// Pushes the selection to <see cref="SectionStore"/> so all views sync to the
    /// same selection simultaneously via <see cref="SectionStore.SelectionChanged"/>.
    /// </summary>
    [RelayCommand]
    public void SelectSection(string sectionId) => _sectionStore.SetSelection(sectionId);

    /// <summary>
    /// Invoked by the view when an entry is double-clicked.
    /// Set by SectionListViewModel to open the section editor.
    /// </summary>
    public Action<string>? EditRequested { get; set; }

    /// <summary>
    /// Loads context menu data for the right-clicked tile entry.
    /// Called from the view's PointerPressed handler before opening the popup.
    /// </summary>
    public void PrepareContextMenu(string sectionId, int day, int startMinutes)
    {
        var section = _sectionRepo.GetById(sectionId);
        if (section is null) return;

        var instructors = _instructorRepo.GetAll();
        var rooms       = _roomRepo.GetAll();
        var tags        = _propertyRepo.GetAll(SectionPropertyTypes.Tag);

        ContextMenu.Load(section, day, startMinutes, instructors, rooms, tags);
    }

    [RelayCommand]
    public void DismissError() => LastErrorMessage = null;

#if DEBUG
    /// <summary>
    /// DEV ONLY — forces a reload failure so the error banner can be tested visually.
    /// Remove before shipping.
    /// </summary>
    [RelayCommand]
    public void SimulateReloadError()
    {
        App.Logger.LogError(new InvalidOperationException("Simulated grid reload error"), "SimulateReloadError");
        GridData = GridData.Empty;
        LastErrorMessage = "An error occurred loading the schedule grid. See logs for details.";
    }
#endif

    public void Reload()
    {
        try
        {
            ReloadCore();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "ScheduleGridViewModel.Reload");
            GridData = GridData.Empty;
            StatsLine = string.Empty;
            LastErrorMessage = "An error occurred loading the schedule grid. See logs for details.";
        }
    }

    /// <summary>
    /// Reloads the schedule grid from the currently selected semester(s) and filter state.
    /// This is the orchestrator for the grid pipeline. It delegates each distinct step to
    /// a focused helper method, making the overall flow easy to read and each step easy
    /// to test or replace independently.
    ///
    /// Pipeline steps:
    ///   1. Guard       — if no semesters are selected, clear the display and return early.
    ///   2. BuildLookups — load all entity dictionaries (courses, instructors, rooms, etc.)
    ///                     from the database into a single <see cref="GridLookups"/> record.
    ///   3. PopulateFilterOptions — rebuild the filter drop-down lists from the new lookups
    ///                     (preserves existing checkbox/selection state by ID).
    ///   4. TakeFilterSnapshot — snapshot the current filter selections into a
    ///                     <see cref="FilterSnapshot"/>; sentinel items are stripped here.
    ///   5. ComputeOverlayMatchedSectionIds — pre-pass that identifies which sections
    ///                     match the active section-level overlay (instructor or tag).
    ///                     Room overlays are resolved per-meeting inside Pass 1 instead.
    ///   6. BuildFilteredBlocks (Pass 1) — iterate sections and emit one
    ///                     <see cref="SectionMeetingBlock"/> per meeting that passes all
    ///                     active filter predicates.
    ///   7. BuildOverlayBlocks (Pass 2) — add any overlay-matched meetings that were
    ///                     excluded by the filters in Pass 1, so overlays always show up
    ///                     regardless of other active filters.
    ///   8. BuildCommitmentBlocks (Pass 3) — if an instructor overlay is active, inject
    ///                     that instructor's time commitments as <see cref="CommitmentBlock"/>
    ///                     objects.
    ///   9. DeduplicateBlocks — remove any block that appears more than once (a meeting can
    ///                     end up in both Pass 1 and Pass 2), keeping the first occurrence
    ///                     to preserve the correct <c>IsOverlay</c> flag.
    ///  10. UpdateDisplayProperties — assemble <see cref="GridData"/> from the processed
    ///                     blocks and update all display-facing observable properties
    ///                     (semester line, colored segments, subject filter summary, stats).
    /// </summary>
    private void ReloadCore()
    {
        var semesters = _semesterContext.SelectedSemesters.ToList();
        if (semesters.Count == 0) { _sectionStore.SetFilteredSectionIds(null); ClearDisplay(); return; }

        // Detect semester changes and clear filter state when they occur.
        // Doing this here (rather than in a separate PropertyChanged subscription) ensures
        // the correct order: filters are always cleared before the new data is processed.
        var semesterKey = string.Join(",", semesters.Select(s => s.Semester.Id).OrderBy(x => x));
        if (_lastSemesterKey != semesterKey)
        {
            Filter.FilterChanged -= Reload;
            Filter.ClearAll();
            Filter.FilterChanged += Reload;
            _lastSemesterKey = semesterKey;
        }

        var lookups = BuildLookups(semesters);
        PopulateFilterOptions(lookups);
        var snap = TakeFilterSnapshot();

        var overlayMatchedIds = ComputeOverlayMatchedSectionIds(lookups.Sections, snap);
        var filtered    = BuildFilteredBlocks(lookups.Sections, snap, lookups, overlayMatchedIds);
        var overlayOnly = BuildOverlayBlocks(lookups.Sections, snap, lookups, filtered, overlayMatchedIds);

        // Push filtered section IDs to SectionStore so the section list can highlight matching cards.
        // Only publish when a real (non-overlay) filter is active; null signals "no highlighting".
        //
        // We check the snapshot rather than Filter.HasRegularFilter because "Emphasize Unstaffed"
        // sets HasRegularFilter=true (it is an active filter state) but does not exclude any
        // sections — publishing all section IDs would light up every card in the section list,
        // which would be misleading. The snap.FilterX properties are false for that sentinel.
        bool hasActualFilter = snap.FilterInstructor || snap.FilterRoom || snap.FilterSubject
                            || snap.FilterCampus    || snap.FilterSectionType || snap.FilterTag
                            || snap.FilterMeetingType || snap.FilterLevel;
        IReadOnlySet<string>? filteredIds = hasActualFilter
            ? filtered.OfType<SectionMeetingBlock>().Select(b => b.SectionId).ToHashSet()
            : null;
        _sectionStore.SetFilteredSectionIds(filteredIds);

        string? overlayInstructorId = snap.HasOverlay && snap.OverlayType == "Instructor"
            ? snap.SelectedOverlayId : null;
        var commitments = BuildCommitmentBlocks(semesters, overlayInstructorId);

        var allBlocks = DeduplicateBlocks(filtered.Concat(overlayOnly).Concat(commitments));
        UpdateDisplayProperties(semesters, allBlocks);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Pipeline step methods
    //
    // Steps that rely only on their parameters (no instance state) are marked
    // "internal static" so unit tests can call them directly without instantiating
    // a full ScheduleGridViewModel.
    //
    // Steps that read from repositories or write to observable properties are private
    // instance methods.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all display-facing observable properties to their empty/default state.
    /// Called by <see cref="ReloadCore"/> when no semesters are selected (e.g. the user
    /// has not yet opened a database or all semester checkboxes are cleared). Ensures the
    /// grid and header area show blank content rather than stale data from a prior load.
    /// </summary>
    private void ClearDisplay()
    {
        GridData             = GridData.Empty;
        SemesterLine         = string.Empty;
        SemesterLineSegments = [];
        SubjectFilterSummary = string.Empty;
        StatsLine            = string.Empty;
    }

    /// <summary>
    /// Loads all entity lookup dictionaries from the database for the given semester(s)
    /// and bundles them into a <see cref="GridLookups"/> record.
    ///
    /// All selected semesters must belong to the same academic year (enforced by the UI),
    /// so course, instructor, room, and subject lookups are built once and shared.
    /// Sections, however, are loaded per-semester and concatenated.
    /// </summary>
    /// <param name="semesters">
    /// The selected semesters. Must contain at least one entry; caller is responsible
    /// for the early-return guard.
    /// </param>
    /// <returns>
    /// A <see cref="GridLookups"/> containing all sections for the semester(s) and
    /// dictionaries for every supporting entity type.
    /// </returns>
    private GridLookups BuildLookups(IReadOnlyList<SemesterDisplay> semesters)
    {
        // Read sections from the shared cache — no DB query here.
        var sections = semesters
            .SelectMany(sd => _sectionStore.SectionsBySemester.TryGetValue(sd.Semester.Id, out var cached)
                ? cached
                : Array.Empty<Section>())
            .ToList();

        var courses     = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructors = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var rooms       = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var subjects    = _subjectRepo.GetAll().ToDictionary(s => s.Id);

        var campuses     = _propertyRepo.GetAll(SectionPropertyTypes.Campus).ToDictionary(v => v.Id);
        var sectionTypes = _propertyRepo.GetAll(SectionPropertyTypes.SectionType).ToDictionary(v => v.Id);
        var tags         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var meetingTypes = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType).ToDictionary(v => v.Id);

        // The level "lookup" is a fixed dictionary of the six level strings mapped to
        // themselves. This lets the level filter use the same dictionary-lookup pattern
        // as all other filter dimensions rather than needing a special code path.
        var levels = new Dictionary<string, string>
        {
            { "0XX", "0XX" }, { "1XX", "1XX" }, { "2XX", "2XX" },
            { "3XX", "3XX" }, { "4XX", "4XX" }, { "5+XX", "5+XX" }
        };

        // Maps semester DB ID → display name so GridBlocks carry the semester name
        // without the renderer needing to perform extra lookups.
        var semesterIdToName = semesters.ToDictionary(sd => sd.Semester.Id, sd => sd.Semester.Name);

        return new GridLookups(
            sections, courses, instructors, rooms, subjects,
            campuses, sectionTypes, tags, meetingTypes, levels, semesterIdToName);
    }

    /// <summary>
    /// Rebuilds all filter drop-down option lists from the current lookup data.
    /// Existing checkbox selections are preserved: <see cref="GridFilterViewModel.PopulateOptions"/>
    /// matches by entity ID rather than by list position.
    ///
    /// This must be called after <see cref="BuildLookups"/> and before
    /// <see cref="TakeFilterSnapshot"/> so that the filter state is up-to-date before
    /// the snapshot is taken.
    /// </summary>
    /// <param name="lookups">Entity lookups built by <see cref="BuildLookups"/>.</param>
    private void PopulateFilterOptions(GridLookups lookups)
    {
        Filter.PopulateOptions(
            lookups.Sections,
            lookups.Instructors,
            lookups.Rooms,
            lookups.Subjects,
            lookups.Courses,
            lookups.Campuses,
            lookups.SectionTypes,
            lookups.Tags,
            lookups.MeetingTypes,
            lookups.Levels);
    }

    /// <summary>
    /// Captures the current filter state into an immutable <see cref="FilterSnapshot"/>.
    /// The sentinel entries ("Not staffed" / "Unroomed") are stripped from the ID sets
    /// during capture; their presence is recorded separately as boolean flags.
    ///
    /// The snapshot is passed to the static pipeline methods that perform the actual
    /// filtering, keeping those methods free of any dependency on the live
    /// <see cref="GridFilterViewModel"/> instance.
    /// </summary>
    /// <returns>
    /// An immutable snapshot of the filter state at the moment of the call, with
    /// sentinel IDs removed from the instructor and room sets.
    /// </returns>
    private FilterSnapshot TakeFilterSnapshot()
    {
        var instructorIds = Filter.SelectedInstructorIds;
        var roomIds       = Filter.SelectedRoomIds;

        // Strip sentinels from the ID sets and record their presence as booleans.
        // SelectedInstructorIds / SelectedRoomIds return newly-allocated HashSets on
        // each call, so Remove() here does not mutate any live filter collection.
        bool notStaffed          = instructorIds.Remove(GridFilterViewModel.NotStaffedId);
        bool emphasizeUnstaffed  = instructorIds.Remove(GridFilterViewModel.EmphasizeUnstaffedId);
        bool unroomed            = roomIds.Remove(GridFilterViewModel.UnroomedId);

        return new FilterSnapshot(
            NamedInstructorIds: instructorIds,
            NamedRoomIds:       roomIds,
            SubjectIds:         Filter.SelectedSubjectIds,
            CampusIds:          Filter.SelectedCampusIds,
            SectionTypeIds:     Filter.SelectedSectionTypeIds,
            TagIds:             Filter.SelectedTagIds,
            MeetingTypeIds:     Filter.SelectedMeetingTypeIds,
            LevelIds:           Filter.SelectedLevelIds,
            NotStaffedSelected:         notStaffed,
            EmphasizeUnstaffedSelected: emphasizeUnstaffed,
            UnroomedSelected:           unroomed,
            HasOverlay:         Filter.HasOverlay,
            OverlayType:        Filter.OverlayType,
            SelectedOverlayId:  Filter.SelectedOverlayId);
    }

    /// <summary>
    /// Builds the display label and instructor initials string for a section tile.
    ///
    /// Label format: "[CalendarCode] [SectionCode]" when a matching course is found
    /// (e.g. "HIST101 A"). Falls back to "[SectionCode]" alone if the section has no
    /// course or the course ID cannot be resolved from the lookup.
    ///
    /// Initials format: space-joined initials of all assigned instructors in the order
    /// they appear in <see cref="Section.InstructorIds"/> (e.g. "JRS MKL"). Returns
    /// an empty string when no instructor assignments exist or none resolve in the lookup.
    ///
    /// This method exists to eliminate the repetition of the same three-line label-
    /// building block that appeared in Pass 1 (filtered sections), Pass 2 instructor/tag
    /// overlay, and Pass 2 room overlay in the original monolithic ReloadCore().
    /// </summary>
    /// <param name="section">The section whose display text should be built.</param>
    /// <param name="courses">Course lookup keyed by course ID.</param>
    /// <param name="instructors">Instructor lookup keyed by instructor ID.</param>
    /// <returns>
    /// A tuple <c>(Label, Initials)</c> where Label is the tile heading line and
    /// Initials is the instructor text appended after it on the same line.
    /// </returns>
    internal static (string Label, string Initials) BuildSectionLabel(
        Section section,
        IReadOnlyDictionary<string, Course> courses,
        IReadOnlyDictionary<string, Instructor> instructors)
    {
        var calCode = section.CourseId is not null && courses.TryGetValue(section.CourseId, out var course)
            ? course.CalendarCode : null;

        var initials = string.Join(" ", section.InstructorIds
            .Where(id => instructors.ContainsKey(id))
            .Select(id => instructors[id].Initials));

        var label = calCode is not null
            ? $"{calCode} {section.SectionCode}"
            : section.SectionCode;

        return (label, initials);
    }

    /// <summary>
    /// Pre-pass: identifies which sections match the active section-level overlay
    /// (instructor or tag). Room overlays are resolved per-meeting inside
    /// <see cref="BuildFilteredBlocks"/> and are not handled here.
    ///
    /// The returned set is used in two ways:
    /// <list type="number">
    ///   <item>
    ///     Inside <see cref="BuildFilteredBlocks"/>: each block for a section in this
    ///     set is tagged <c>IsOverlay=true</c>.
    ///   </item>
    ///   <item>
    ///     Inside <see cref="BuildOverlayBlocks"/>: sections in this set whose meetings
    ///     were not already emitted by Pass 1 are added as overlay-only blocks.
    ///   </item>
    /// </list>
    /// Returns an empty set when no section-level overlay is active (i.e. when
    /// <see cref="FilterSnapshot.HasOverlay"/> is false or the overlay type is "Room").
    /// </summary>
    /// <param name="sections">All sections loaded for the selected semester(s).</param>
    /// <param name="snap">Snapshot of the current filter and overlay state.</param>
    /// <returns>
    /// Set of section IDs that match the active instructor or tag overlay.
    /// Empty when no section-level overlay is active.
    /// </returns>
    internal static HashSet<string> ComputeOverlayMatchedSectionIds(
        IReadOnlyList<Section> sections,
        FilterSnapshot snap)
    {
        var matched = new HashSet<string>();
        if (!snap.HasOverlay || snap.OverlayType == "Room")
            return matched;

        var overlayId = snap.SelectedOverlayId ?? string.Empty;
        foreach (var section in sections)
        {
            bool matches = snap.OverlayType switch
            {
                "Instructor" => section.InstructorIds.Contains(overlayId),
                "Tag"        => section.TagIds.Contains(overlayId),
                _            => false
            };
            if (matches)
                matched.Add(section.Id);
        }
        return matched;
    }

    /// <summary>
    /// Pass 1 of block collection: iterates all sections and emits one
    /// <see cref="SectionMeetingBlock"/> for every meeting that passes all active
    /// filter dimensions.
    ///
    /// Filtering is applied in two stages:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Section-level</b> — instructor, subject, course level, campus,
    ///     section-type, tags. If any section-level predicate fails, all of that
    ///     section's meetings are skipped.
    ///   </item>
    ///   <item>
    ///     <b>Meeting-level</b> — room and meeting-type. If a meeting-level predicate
    ///     fails, only that one slot is skipped.
    ///   </item>
    /// </list>
    ///
    /// Overlay marking: blocks for sections in <paramref name="overlayMatchedIds"/> are
    /// emitted with <c>IsOverlay=true</c>. For room overlays the meeting's
    /// <c>RoomId</c> is compared against <see cref="FilterSnapshot.SelectedOverlayId"/>
    /// directly, per-meeting.
    /// </summary>
    /// <param name="sections">All sections for the selected semester(s).</param>
    /// <param name="snap">Snapshot of the current filter and overlay state.</param>
    /// <param name="lookups">Entity lookup dictionaries (courses, instructors, etc.).</param>
    /// <param name="overlayMatchedIds">
    /// Pre-computed set of section IDs that match the section-level overlay, as returned
    /// by <see cref="ComputeOverlayMatchedSectionIds"/>. Pass an empty set when no
    /// section-level overlay is active.
    /// </param>
    /// <returns>
    /// All <see cref="SectionMeetingBlock"/> objects for meetings that survived all
    /// filter predicates. Not yet deduplicated.
    /// </returns>
    internal static List<GridBlock> BuildFilteredBlocks(
        IReadOnlyList<Section> sections,
        FilterSnapshot snap,
        GridLookups lookups,
        HashSet<string> overlayMatchedIds)
    {
        var blocks = new List<GridBlock>();

        foreach (var section in sections)
        {
            // ── Section-level filters ──────────────────────────────────────────────

            if (snap.FilterInstructor)
            {
                // OR within the instructor dimension:
                //   "Not staffed" sentinel → section must have no instructor assignments.
                //   Named instructors      → section must be assigned to at least one.
                // The two are mutually exclusive in the UI, but the OR handles both for
                // robustness (e.g. if filter state is restored from a saved preset).
                bool passes = (snap.NotStaffedSelected && !section.InstructorIds.Any())
                           || (snap.NamedInstructorIds.Count > 0
                               && section.InstructorIds.Any(snap.NamedInstructorIds.Contains));
                if (!passes) continue;
            }

            if (snap.FilterSubject)
            {
                if (section.CourseId is null || !lookups.Courses.TryGetValue(section.CourseId, out var c))
                    continue;
                if (!snap.SubjectIds.Contains(c.SubjectId))
                    continue;
            }

            if (snap.FilterLevel)
            {
                if (section.CourseId is null || !lookups.Courses.TryGetValue(section.CourseId, out var c))
                    continue;
                if (!snap.LevelIds.Contains(c.Level))
                    continue;
            }

            if (snap.FilterCampus && !snap.CampusIds.Contains(section.CampusId ?? string.Empty))
                continue;

            if (snap.FilterSectionType && !snap.SectionTypeIds.Contains(section.SectionTypeId ?? string.Empty))
                continue;

            // Tags use AND logic: the section must carry ALL selected tags to pass.
            if (snap.FilterTag && !snap.TagIds.IsSubsetOf(section.TagIds))
                continue;

            // ── Build display label (DRY helper used by all three passes) ──────────
            var (label, initials) = BuildSectionLabel(section, lookups.Courses, lookups.Instructors);
            bool sectionIsOverlay  = overlayMatchedIds.Contains(section.Id);
            // "Emphasize Unstaffed" mode: staffed sections are de-emphasised with a
            // strikethrough in the grid. Unstaffed sections render normally.
            bool isDeemphasized    = snap.EmphasizeUnstaffedSelected && section.InstructorIds.Any();

            // ── Meeting-level filters and block emission ───────────────────────────
            foreach (var slot in section.Schedule)
            {
                if (snap.FilterRoom)
                {
                    // OR within the room dimension: "Unroomed" sentinel → meeting has
                    // no room assigned; named rooms → meeting is in one of those rooms.
                    bool passes = (snap.UnroomedSelected && string.IsNullOrEmpty(slot.RoomId))
                               || (snap.NamedRoomIds.Count > 0
                                   && snap.NamedRoomIds.Contains(slot.RoomId ?? string.Empty));
                    if (!passes) continue;
                }

                if (snap.FilterMeetingType && !snap.MeetingTypeIds.Contains(slot.MeetingTypeId ?? string.Empty))
                    continue;

                // For room overlays, IsOverlay is determined per-meeting; for section-level
                // overlays it was already resolved above by checking overlayMatchedIds.
                bool isOverlay = sectionIsOverlay;
                if (!isOverlay && snap.HasOverlay && snap.OverlayType == "Room")
                    isOverlay = slot.RoomId == snap.SelectedOverlayId;

                var semName = lookups.SemesterIdToName.TryGetValue(section.SemesterId, out var n) ? n : string.Empty;
                blocks.Add(new SectionMeetingBlock(
                    slot.Day, slot.StartMinutes, slot.EndMinutes,
                    isOverlay, label, initials, section.Id, section.SemesterId, semName,
                    SectionDaySchedule.FormatFrequency(slot.Frequency),
                    IsDeemphasized: isDeemphasized));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Pass 2 of block collection: injects overlay-matched meetings that were NOT
    /// already emitted by Pass 1 (<see cref="BuildFilteredBlocks"/>).
    ///
    /// Without this pass, an overlay would only highlight sections that also pass all
    /// active filters — sections hidden by a filter would not appear on the grid at all.
    /// This pass ensures overlay matches are always visible regardless of other filters.
    ///
    /// Behaviour by overlay type:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Instructor / Tag overlay</b> — adds all meetings for every section in
    ///     <paramref name="overlayMatchedIds"/> that was not already added by Pass 1.
    ///   </item>
    ///   <item>
    ///     <b>Room overlay</b> — adds only the specific meetings whose <c>RoomId</c>
    ///     equals <see cref="FilterSnapshot.SelectedOverlayId"/> and that were not
    ///     already added.
    ///   </item>
    /// </list>
    ///
    /// All blocks emitted here carry <c>IsOverlay=true</c>. Returns an empty list when
    /// no overlay is active.
    /// </summary>
    /// <param name="sections">All sections for the selected semester(s).</param>
    /// <param name="snap">Snapshot of the current filter and overlay state.</param>
    /// <param name="lookups">Entity lookup dictionaries (courses, instructors, etc.).</param>
    /// <param name="filteredBlocks">
    /// The blocks already collected by Pass 1. Used to determine which meetings have
    /// already been added so this pass does not produce duplicates.
    /// </param>
    /// <param name="overlayMatchedIds">
    /// Pre-computed set of section IDs that match the section-level overlay.
    /// Only used for Instructor and Tag overlay types.
    /// </param>
    /// <returns>
    /// A list of <see cref="SectionMeetingBlock"/> objects that are in the overlay but
    /// were absent from <paramref name="filteredBlocks"/>. Not yet deduplicated.
    /// </returns>
    internal static List<GridBlock> BuildOverlayBlocks(
        IReadOnlyList<Section> sections,
        FilterSnapshot snap,
        GridLookups lookups,
        IReadOnlyList<GridBlock> filteredBlocks,
        HashSet<string> overlayMatchedIds)
    {
        var blocks = new List<GridBlock>();
        if (!snap.HasOverlay) return blocks;

        if (snap.OverlayType != "Room")
        {
            // Instructor or Tag overlay: add all meetings for matched sections whose
            // section ID was not already written by Pass 1. Build a HashSet for O(1)
            // lookups rather than iterating filteredBlocks O(n) per section.
            var alreadyAddedSectionIds = filteredBlocks
                .OfType<SectionMeetingBlock>()
                .Select(b => b.SectionId)
                .ToHashSet();

            foreach (var sectionId in overlayMatchedIds)
            {
                if (alreadyAddedSectionIds.Contains(sectionId)) continue;

                var section = sections.FirstOrDefault(s => s.Id == sectionId);
                if (section is null) continue;

                var (label, initials) = BuildSectionLabel(section, lookups.Courses, lookups.Instructors);
                var semName = lookups.SemesterIdToName.TryGetValue(section.SemesterId, out var n) ? n : string.Empty;

                foreach (var slot in section.Schedule)
                {
                    blocks.Add(new SectionMeetingBlock(
                        slot.Day, slot.StartMinutes, slot.EndMinutes,
                        true, label, initials, section.Id, section.SemesterId, semName,
                        SectionDaySchedule.FormatFrequency(slot.Frequency)));
                }
            }
        }
        else
        {
            // Room overlay: add only the meetings in the overlay room that were not
            // already added by Pass 1. Build a HashSet of (sectionId, day, start, end)
            // tuples from Pass 1 for O(1) duplicate checking.
            var overlayRoomId = snap.SelectedOverlayId ?? string.Empty;
            var alreadyAdded = filteredBlocks
                .OfType<SectionMeetingBlock>()
                .Select(b => (b.SectionId, b.Day, b.StartMinutes, b.EndMinutes))
                .ToHashSet();

            foreach (var section in sections)
            {
                var (label, initials) = BuildSectionLabel(section, lookups.Courses, lookups.Instructors);
                var semName = lookups.SemesterIdToName.TryGetValue(section.SemesterId, out var n) ? n : string.Empty;

                foreach (var slot in section.Schedule)
                {
                    if (slot.RoomId != overlayRoomId) continue;
                    if (alreadyAdded.Contains((section.Id, slot.Day, slot.StartMinutes, slot.EndMinutes))) continue;

                    blocks.Add(new SectionMeetingBlock(
                        slot.Day, slot.StartMinutes, slot.EndMinutes,
                        true, label, initials, section.Id, section.SemesterId, semName,
                        SectionDaySchedule.FormatFrequency(slot.Frequency)));
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// Pass 3 of block collection: loads instructor commitments from the database and
    /// converts them into <see cref="CommitmentBlock"/> objects.
    ///
    /// Commitments are independent of sections — an instructor's meetings and office-hours
    /// obligations can appear side-by-side on the same grid regardless of what sections
    /// that instructor teaches. They participate in the same overlap/column-split layout
    /// engine as section meetings. If a commitment shares the exact same time span as a
    /// section meeting on the same day, the two entries are stacked inside one tile.
    ///
    /// Commitments are only injected under an instructor overlay (not room or tag overlays)
    /// because commitments belong to an instructor, not to a room or tag.
    ///
    /// <see cref="CommitmentBlock.IsOverlay"/> is hardcoded <c>true</c>, so commitments
    /// always render with the overlay (red) style.
    ///
    /// No deduplication guard is needed: commitments are fetched once per semester for
    /// this specific instructor, so the repository cannot return the same record twice.
    /// </summary>
    /// <param name="semesters">The selected semesters; each is queried independently.</param>
    /// <param name="overlayInstructorId">
    /// The instructor ID whose commitments should be loaded.
    /// Pass <c>null</c> or an empty string when no instructor overlay is active; the
    /// method returns an empty list immediately without touching the database.
    /// </param>
    /// <returns>
    /// A list of <see cref="CommitmentBlock"/> objects for the overlay instructor across
    /// all selected semesters. Empty when <paramref name="overlayInstructorId"/> is null.
    /// </returns>
    private List<GridBlock> BuildCommitmentBlocks(
        IReadOnlyList<SemesterDisplay> semesters,
        string? overlayInstructorId)
    {
        var blocks = new List<GridBlock>();
        if (string.IsNullOrEmpty(overlayInstructorId)) return blocks;

        foreach (var sd in semesters)
        {
            var commitments = _commitmentRepo.GetByInstructor(sd.Semester.Id, overlayInstructorId);
            foreach (var c in commitments)
                blocks.Add(new CommitmentBlock(c.Day, c.StartMinutes, c.EndMinutes, c.Name, c.Id, sd.Semester.Id, sd.Semester.Name));
        }
        return blocks;
    }

    /// <summary>
    /// Removes duplicate blocks from the combined block list, keeping the first occurrence.
    ///
    /// A block is a duplicate when another block with the same
    /// (entity-id, semester-id, day, start-time, end-time) tuple has already been seen.
    /// The entity ID is the <c>SectionId</c> for <see cref="SectionMeetingBlock"/> and
    /// the <c>CommitmentId</c> for <see cref="CommitmentBlock"/>.
    ///
    /// Why duplicates can arise: the same section meeting can appear in both Pass 1
    /// (filtered blocks) and Pass 2 (overlay-only blocks). Pass 1 sets <c>IsOverlay</c>
    /// correctly based on the overlay match status; Pass 2 always sets it <c>true</c>.
    /// Keeping the first occurrence (Pass 1) preserves the correct overlay flag.
    ///
    /// The <c>SemesterId</c> is included in the key to guard against the unlikely but
    /// theoretically possible case where two semesters share a section ID.
    ///
    /// <see cref="CommitmentBlock"/> objects are fetched once per instructor per semester
    /// by design, so they cannot be duplicated; they pass through unchanged.
    /// </summary>
    /// <param name="blocks">
    /// The combined output of Pass 1, Pass 2, and Pass 3 (in that order). The ordering
    /// matters: when duplicates exist, the first occurrence is kept.
    /// </param>
    /// <returns>
    /// A new <see cref="List{T}"/> containing only the first occurrence of each
    /// unique block.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if an unknown <see cref="GridBlock"/> subtype is encountered. This would
    /// indicate that a new GridBlock subtype was added without updating this method.
    /// </exception>
    internal static List<GridBlock> DeduplicateBlocks(IEnumerable<GridBlock> blocks)
    {
        // Local helper extracts the entity ID regardless of GridBlock subtype.
        // If a new subtype is added, the exhaustive switch will throw at runtime,
        // which is preferable to silently dropping data.
        static string EntityId(GridBlock b) => b switch
        {
            SectionMeetingBlock s => s.SectionId,
            CommitmentBlock c     => c.CommitmentId,
            _ => throw new InvalidOperationException($"Unknown GridBlock subtype: {b.GetType().Name}")
        };

        var seen   = new HashSet<(string, string, int, int, int)>();
        var result = new List<GridBlock>();

        foreach (var block in blocks)
        {
            var key = (EntityId(block), block.SemesterId, block.Day, block.StartMinutes, block.EndMinutes);
            if (seen.Add(key))
                result.Add(block);
        }
        return result;
    }

    /// <summary>
    /// Assembles the final <see cref="GridData"/> structure and updates all display-facing
    /// observable properties from the processed block list. This is the last step of the
    /// reload pipeline, called after deduplication.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    ///   <item>
    ///     Builds per-day (and per-semester sub-column in multi-semester mode) tile layouts
    ///     by calling <see cref="ComputeTiles"/> for each day/semester combination.
    ///   </item>
    ///   <item>
    ///     Assigns <see cref="GridData"/> so the view renders the updated grid.
    ///   </item>
    ///   <item>
    ///     Updates <see cref="SemesterLine"/> and <see cref="SemesterLineSegments"/> for
    ///     the header.
    ///   </item>
    ///   <item>
    ///     Updates <see cref="SubjectFilterSummary"/> (e.g. "History · Mathematics" when
    ///     subject filters are active).
    ///   </item>
    ///   <item>
    ///     Updates <see cref="StatsLine"/> (e.g. "12 sections · 28 meetings shown").
    ///     Only <see cref="SectionMeetingBlock"/> objects are counted; commitment blocks
    ///     are excluded because including them would make the counts misleading.
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="semesters">The selected semesters; used for column headers and line building.</param>
    /// <param name="allBlocks">The fully-processed, deduplicated block list from all three passes.</param>
    private void UpdateDisplayProperties(
        IReadOnlyList<SemesterDisplay> semesters,
        IReadOnlyList<GridBlock> allBlocks)
    {
        // Fixed grid time range: 08:30 to 22:00.
        const int firstRow = 8 * 60 + 30;
        const int lastRow  = 22 * 60;

        var includeSaturday = AppSettings.Current.IncludeSaturday;
        var dayNumbers = new List<int> { 1, 2, 3, 4, 5 };
        if (includeSaturday) dayNumbers.Add(6);
        var dayNames = new Dictionary<int, string>
        {
            [1] = "Monday", [2] = "Tuesday",  [3] = "Wednesday",
            [4] = "Thursday", [5] = "Friday", [6] = "Saturday"
        };

        // In single-semester mode: one column per day.
        // In multi-semester mode: N sub-columns per day (one per semester), ordered as
        // [Mon/Sem1, Mon/Sem2, Tue/Sem1, Tue/Sem2, ...]. The renderer uses SemesterCount
        // to know how many consecutive columns belong to the same day group.
        bool isMultiSemester = semesters.Count > 1;
        var dayColumns = new List<GridDayColumn>();

        foreach (var dayNum in dayNumbers)
        {
            foreach (var sd in semesters)
            {
                // In multi-semester mode, each sub-column contains only that semester's blocks.
                // In single-semester mode, SemesterId filtering is omitted for robustness
                // (all blocks should share the single semester ID, but we don't depend on it).
                var dayBlocks = isMultiSemester
                    ? allBlocks
                        .Where(b => b.Day == dayNum && b.SemesterId == sd.Semester.Id)
                        .OrderBy(b => b.StartMinutes).ThenBy(b => b.EndMinutes)
                        .ToList()
                    : allBlocks
                        .Where(b => b.Day == dayNum)
                        .OrderBy(b => b.StartMinutes).ThenBy(b => b.EndMinutes)
                        .ToList();

                dayColumns.Add(new GridDayColumn(dayNames[dayNum], ComputeTiles(dayBlocks, sd.Semester.Name), sd.Semester.Name));
            }
        }

        GridData = new GridData(firstRow, lastRow, dayColumns, semesters.Count);

        // ── Semester line and colored segment chips ────────────────────────────
        // Single-semester mode: "Year — Semester" (e.g. "2025-2026 — Fall")
        // Multi-semester mode:  "Year — Sem1, Sem2" plus colored segment chips.
        SemesterLine = semesters.Count == 1
            ? semesters[0].DisplayName
            : $"{_semesterContext.SelectedAcademicYear?.Name} — " +
              string.Join(", ", semesters.Select(s => s.Semester.Name));

        BuildSemesterLineSegments(semesters);

        // ── Subject filter summary ─────────────────────────────────────────────
        // Shows "History · Mathematics" (etc.) in the header when subject filters are active.
        var selectedSubjectNames = Filter.Subjects
            .Where(s => s.IsSelected)
            .Select(s => s.Name)
            .ToList();
        SubjectFilterSummary = selectedSubjectNames.Count > 0
            ? string.Join(" · ", selectedSubjectNames)
            : string.Empty;

        // ── Stats line ─────────────────────────────────────────────────────────
        // Counts distinct section IDs (not commitment blocks) and total meeting slots.
        var sectionBlocks = allBlocks.OfType<SectionMeetingBlock>().ToList();
        int sectionCount  = sectionBlocks.Select(b => b.SectionId).Distinct().Count();
        int meetingCount  = sectionBlocks.Count;
        StatsLine = sectionCount == 0
            ? "No sections shown"
            : $"{sectionCount} {(sectionCount == 1 ? "section" : "sections")} · " +
              $"{meetingCount} {(meetingCount == 1 ? "meeting" : "meetings")} shown";
    }

    /// <summary>
    /// Resolves the semester border brush from AppColors using the semester name.
    /// Uses the same color-key mapping as SemesterBannerViewModel so that semester colors
    /// throughout the app are visually consistent.
    /// Returns null if the resource cannot be found (renderer will use default styling).
    /// Used by ScheduleGridView to color meeting card borders in multi-semester mode.
    /// </summary>
    public static Avalonia.Media.IBrush? ResolveSemesterBorderBrush(string semesterName)
    {
        var firstWord = semesterName.Split(' ')[0];
        string key = firstWord switch
        {
            "Fall"   => "FallBorder",
            "Winter" => "WinterBorder",
            "Early"  => "EarlySummerBorder",
            "Summer" => "SummerBorder",
            "Late"   => "LateSummerBorder",
            _        => "FallBorder"
        };

        object? resource = null;
        Avalonia.Application.Current?.Resources.TryGetResource(key, null, out resource);
        return resource as Avalonia.Media.IBrush;
    }

    /// <summary>
    /// Converts a GridBlock to the TileEntry record consumed by the renderer.
    ///
    /// For SectionMeetingBlocks: the label, initials, section ID, and overlay flag
    /// are all passed through. The renderer uses SectionId for selection highlighting
    /// and IsOverlay for red styling.
    ///
    /// For CommitmentBlocks: the commitment name becomes the label; initials and
    /// SectionId are set to empty string (commitments are not selectable and have no
    /// instructor initials displayed). IsOverlay is hardcoded true (CommitmentBlock
    /// itself hardcodes it, so this just passes it through) and IsCommitment is true,
    /// which tells the renderer to suppress click interactions (no selection, no
    /// right-click context menu, no hand cursor).
    ///
    /// If a new GridBlock subtype is added in future, add a case here. The exhaustive
    /// switch will throw at runtime if a new subtype is forgotten.
    /// </summary>
    private static TileEntry ToEntry(GridBlock block) => block switch
    {
        SectionMeetingBlock s => new TileEntry(s.Label, s.Initials, s.SectionId, s.IsOverlay, false, s.FrequencyAnnotation, s.IsDeemphasized),
        CommitmentBlock c     => new TileEntry(c.Name, string.Empty, string.Empty, true, true),
        _ => throw new InvalidOperationException($"Unknown GridBlock type: {block.GetType().Name}")
    };

    /// <summary>
    /// Builds positioned tiles for one day's blocks.
    /// Blocks with identical start+end are merged into a single tile (stacked entries).
    /// Overlapping blocks (different time spans) are placed side-by-side.
    /// </summary>
    /// <summary>
    /// Computes the tile layout for a single day's blocks (already filtered by day and semester).
    /// All blocks in this call belong to the same semester, so semesterName is provided
    /// and propagated to each resulting GridTile for use by the renderer.
    /// </summary>
    internal static List<GridTile> ComputeTiles(List<GridBlock> blocks, string semesterName = "")
    {
        var tiles = new List<GridTile>();
        if (blocks.Count == 0) return tiles;

        // Step 1: merge blocks with identical start+end into combined tile entries.
        var merged = new List<(int Start, int End, List<TileEntry> Entries)>();
        var mergeIndex = new Dictionary<(int, int), int>();

        foreach (var block in blocks)
        {
            var key = (block.StartMinutes, block.EndMinutes);
            if (mergeIndex.TryGetValue(key, out int idx))
            {
                merged[idx].Entries.Add(ToEntry(block));
            }
            else
            {
                mergeIndex[key] = merged.Count;
                merged.Add((block.StartMinutes, block.EndMinutes, [ToEntry(block)]));
            }
        }

        // Sort merged tiles by start then end
        merged.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        // Step 2: group into overlap clusters
        var clusters     = new List<List<int>>();
        var clusterMaxEnd = new List<int>();

        for (int i = 0; i < merged.Count; i++)
        {
            var m = merged[i];
            int clusterIdx = -1;
            for (int c = 0; c < clusters.Count; c++)
            {
                if (m.Start < clusterMaxEnd[c]) { clusterIdx = c; break; }
            }
            if (clusterIdx == -1)
            {
                clusters.Add([i]);
                clusterMaxEnd.Add(m.End);
            }
            else
            {
                clusters[clusterIdx].Add(i);
                clusterMaxEnd[clusterIdx] = Math.Max(clusterMaxEnd[clusterIdx], m.End);
            }
        }

        // Step 3: assign column slots greedily within each cluster
        foreach (var cluster in clusters)
        {
            var colEnds = new List<int>();

            foreach (var idx in cluster)
            {
                var m = merged[idx];
                int col = -1;
                for (int c = 0; c < colEnds.Count; c++)
                {
                    if (m.Start >= colEnds[c]) { col = c; break; }
                }
                if (col == -1) { col = colEnds.Count; colEnds.Add(0); }
                colEnds[col] = m.End;

                tiles.Add(new GridTile(m.Entries, m.Start, m.End, col, cluster.Count, semesterName));
            }

            // Fix OverlapCount to actual column count used
            int actualCols = colEnds.Count;
            for (int t = tiles.Count - cluster.Count; t < tiles.Count; t++)
            {
                var old = tiles[t];
                tiles[t] = old with { OverlapCount = actualCols, SemesterName = semesterName };
            }
        }

        return tiles;
    }

    /// <summary>
    /// Builds colored semester line segments for display in the header.
    /// In single-semester mode, shows just the semester name. In multi-semester mode,
    /// shows the academic year followed by colored segment buttons for each semester.
    /// </summary>
    private void BuildSemesterLineSegments(IReadOnlyList<SemesterDisplay> semesters)
    {
        var segments = new List<SemesterLineSegment>();

        if (semesters.Count == 0)
        {
            SemesterLineSegments = segments;
            return;
        }

        if (semesters.Count == 1)
        {
            // Single semester: show just the semester name without color background
            // (colors are only needed to distinguish between multiple semesters)
            var sem = semesters[0];
            segments.Add(new SemesterLineSegment(sem.Semester.Name, null, null));
        }
        else
        {
            // Multi-semester: show academic year (plain text), then colored segments for each semester
            var ayName = _semesterContext.SelectedAcademicYear?.Name ?? "";
            if (!string.IsNullOrEmpty(ayName))
            {
                segments.Add(new SemesterLineSegment($"{ayName} —", null, null));
            }

            foreach (var sem in semesters)
            {
                var (bg, bd) = GetSemesterBrushes(sem.Semester.Name);
                segments.Add(new SemesterLineSegment(sem.Semester.Name, bg, bd));
            }
        }

        SemesterLineSegments = segments;
    }

    /// <summary>
    /// Gets the background and border brushes for a semester from AppColors.
    /// </summary>
    private static (IBrush? bg, IBrush? bd) GetSemesterBrushes(string semesterName)
    {
        var firstWord = semesterName.Split(' ')[0];
        var (bgKey, bdKey) = firstWord switch
        {
            "Fall" => ("FallBackground", "FallBorder"),
            "Winter" => ("WinterBackground", "WinterBorder"),
            "Early" => ("EarlySummerBackground", "EarlySummerBorder"),
            "Summer" => ("SummerBackground", "SummerBorder"),
            "Late" => ("LateSummerBackground", "LateSummerBorder"),
            _ => ("FallBackground", "FallBorder")
        };

        var bg = Application.Current?.Resources.TryGetResource(bgKey, null, out var bgObj) ?? false
            ? bgObj as IBrush : null;
        var bd = Application.Current?.Resources.TryGetResource(bdKey, null, out var bdObj) ?? false
            ? bdObj as IBrush : null;

        return (bg, bd);
    }

}
