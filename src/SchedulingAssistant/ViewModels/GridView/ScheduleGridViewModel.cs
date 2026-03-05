using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.GridView;

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
    private readonly SectionChangeNotifier _changeNotifier;
    private readonly InstructorCommitmentRepository _commitmentRepo;

    [ObservableProperty] private GridData _gridData = GridData.Empty;
    [ObservableProperty] private string? _selectedSectionId;
    [ObservableProperty] private string? _lastErrorMessage;

    /// <summary>Display name of the selected semester, e.g. "2025-2026 — Fall"</summary>
    [ObservableProperty] private string _semesterLine = string.Empty;

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

    public ScheduleGridViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        SubjectRepository subjectRepo,
        SectionPropertyRepository propertyRepo,
        SemesterContext semesterContext,
        AcademicUnitService academicUnitService,
        SectionChangeNotifier changeNotifier,
        InstructorCommitmentRepository commitmentRepo)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _subjectRepo = subjectRepo;
        _propertyRepo = propertyRepo;
        _semesterContext = semesterContext;
        _academicUnitService = academicUnitService;
        _changeNotifier = changeNotifier;
        _commitmentRepo = commitmentRepo;

        ContextMenu = new SectionContextMenuViewModel(sectionRepo, NotifySectionChanged);

        LoadAcademicUnitName();

        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        Filter.FilterChanged += Reload;

        // Subscribe to the shared change notifier so the grid reloads whenever any
        // code fires NotifySectionChanged() — including the context menu (via
        // NotifySectionChanged below), external section edits, and commitment CRUD
        // (via CommitmentsManagementViewModel). All callers go through the same
        // notifier, so there is one single place that drives grid refresh.
        _changeNotifier.SectionChanged += Reload;

        Reload();
    }

    private void LoadAcademicUnitName()
    {
        var unit = _academicUnitService.GetUnit();
        AcademicUnitName = unit?.Name ?? string.Empty;
    }

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
        {
            // Clear filter selections so each semester starts fresh.
            // Unsubscribe temporarily so clearing doesn't trigger multiple Reloads.
            Filter.FilterChanged -= Reload;
            Filter.ClearAll();
            Filter.FilterChanged += Reload;
            Reload();
        }
    }

    /// <summary>Called by the view when a tile is clicked; sets SelectedSectionId.</summary>
    [RelayCommand]
    public void SelectSection(string sectionId) => SelectedSectionId = sectionId;

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

    /// <summary>
    /// Callback passed to SectionContextMenuViewModel; called after the context menu
    /// saves a change to a section. Fires the shared SectionChangeNotifier, which:
    ///   1. Triggers Reload() on THIS view model (via the subscription in the constructor)
    ///   2. Triggers Reload() on SectionListViewModel (which also subscribes)
    /// Do NOT call Reload() directly here — that would cause a double reload because
    /// the subscription above already handles it.
    /// </summary>
    private void NotifySectionChanged()
    {
        _changeNotifier.NotifySectionChanged();
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

    private void ReloadCore()
    {
        var semesterDisplay = _semesterContext.SelectedSemesterDisplay;
        var semester = semesterDisplay?.Semester;
        if (semester is null)
        {
            GridData = GridData.Empty;
            SemesterLine = string.Empty;
            SubjectFilterSummary = string.Empty;
            StatsLine = string.Empty;
            return;
        }

        // ── Build lookup tables ────────────────────────────────────────────────
        var sections         = _sectionRepo.GetAll(semester.Id);
        var courseLookup     = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var roomLookup       = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var subjectLookup    = _subjectRepo.GetAll().ToDictionary(s => s.Id);

        var campusLookup      = _propertyRepo.GetAll(SectionPropertyTypes.Campus).ToDictionary(v => v.Id);
        var sectionTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.SectionType).ToDictionary(v => v.Id);
        var tagLookup         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var meetingTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType).ToDictionary(v => v.Id);

        var levelLookup = new Dictionary<string, string>
        {
            { "0XX", "0XX" },
            { "1XX", "1XX" },
            { "2XX", "2XX" },
            { "3XX", "3XX" },
            { "4XX", "4XX" },
            { "5+XX", "5+XX" }
        };

        // ── Rebuild filter option lists (preserves selections) ─────────────────
        Filter.PopulateOptions(
            sections,
            instructorLookup,
            roomLookup,
            subjectLookup,
            courseLookup,
            campusLookup,
            sectionTypeLookup,
            tagLookup,
            meetingTypeLookup,
            levelLookup);

        // ── Snapshot active filter sets (HashSet lookups are O(1)) ─────────────
        var selInstructors  = Filter.SelectedInstructorIds;
        var selRooms        = Filter.SelectedRoomIds;
        var selSubjects     = Filter.SelectedSubjectIds;
        var selCampuses     = Filter.SelectedCampusIds;
        var selSectionTypes = Filter.SelectedSectionTypeIds;
        var selTags         = Filter.SelectedTagIds;
        var selMeetingTypes = Filter.SelectedMeetingTypeIds;
        var selLevels       = Filter.SelectedLevelIds;

        // The Instructor and Room filter lists each contain a sentinel item at index 0
        // ("Not staffed" / "Unroomed") that represents sections/meetings with no value
        // assigned for that dimension. Strip these from the ID sets now so the remaining
        // sets contain only real entity IDs. The boolean flags carry the sentinel intent
        // into the filter predicates below, where they are ORed with the named-item check.
        bool notStaffedSelected = selInstructors.Remove(GridFilterViewModel.NotStaffedId);
        bool unroomedSelected   = selRooms.Remove(GridFilterViewModel.UnroomedId);

        bool filterInstructor  = selInstructors.Count > 0 || notStaffedSelected;
        bool filterRoom        = selRooms.Count       > 0 || unroomedSelected;
        bool filterSubject     = selSubjects.Count     > 0;
        bool filterCampus      = selCampuses.Count     > 0;
        bool filterSectionType = selSectionTypes.Count > 0;
        bool filterTag         = selTags.Count         > 0;
        bool filterMeetingType = selMeetingTypes.Count > 0;
        bool filterLevel       = selLevels.Count       > 0;

        var includeSaturday = AppSettings.Load().IncludeSaturday;

        // ── Build day columns ─────────────────────────────────────────────────
        var dayNumbers = new List<int> { 1, 2, 3, 4, 5 };
        if (includeSaturday) dayNumbers.Add(6);
        var dayNames = new Dictionary<int, string>
        {
            [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday",
            [4] = "Thursday", [5] = "Friday", [6] = "Saturday"
        };

        // ── Identify overlay-matched sections BEFORE filtering ────────────────
        // For Instructors and Tags (section-level attributes), precompute which sections match.
        // For Rooms (meeting-level attribute), we'll compute per-meeting in the loop below.
        var overlayMatchedSectionIds = new HashSet<string>();
        if (Filter.HasOverlay && Filter.OverlayType != "Room")
        {
            var overlayId = Filter.SelectedOverlayId ?? string.Empty;
            foreach (var section in sections)
            {
                bool matchesOverlay = false;
                if (Filter.OverlayType == "Instructor")
                    matchesOverlay = section.InstructorIds.Contains(overlayId);
                else if (Filter.OverlayType == "Tag")
                    matchesOverlay = section.TagIds.Contains(overlayId);

                if (matchesOverlay)
                    overlayMatchedSectionIds.Add(section.Id);
            }
        }

        // ── Collect all time blocks for the grid ─────────────────────────────
        // allBlocks accumulates everything that will be drawn:
        //   Pass 1 (below): section meetings that pass the active filters
        //   Pass 2 (below): overlay-matched sections not already in Pass 1
        //   Pass 3 (below): instructor commitments when an instructor overlay is active
        // After collection, a dedup step removes any block that appears more than once,
        // then blocks are split by day and handed to ComputeTiles() for layout.
        var allBlocks = new List<GridBlock>();

        foreach (var section in sections)
        {
            // ── Section-level filter ───────────────────────────────────────────
            if (filterInstructor)
            {
                // OR within the instructor dimension:
                //   "Not staffed" sentinel → section must have no instructor assignments
                //   Named instructors      → section must be assigned to at least one
                // The two are mutually exclusive in the UI, but the OR handles both
                // in case state is restored from a saved filter.
                bool passes = (notStaffedSelected && !section.InstructorIds.Any())
                           || (selInstructors.Count > 0 && section.InstructorIds.Any(selInstructors.Contains));
                if (!passes) continue;
            }

            if (filterSubject)
            {
                if (string.IsNullOrEmpty(section.CourseId) || !courseLookup.TryGetValue(section.CourseId, out var c))
                    continue;
                if (!selSubjects.Contains(c.SubjectId))
                    continue;
            }

            if (filterLevel)
            {
                if (string.IsNullOrEmpty(section.CourseId) || !courseLookup.TryGetValue(section.CourseId, out var c))
                    continue;
                if (!selLevels.Contains(c.Level))
                    continue;
            }

            if (filterCampus && !selCampuses.Contains(section.CampusId ?? string.Empty))
                continue;

            if (filterSectionType && !selSectionTypes.Contains(section.SectionTypeId ?? string.Empty))
                continue;

            // Tags: AND — section must have ALL selected tags
            if (filterTag && !selTags.IsSubsetOf(section.TagIds))
                continue;

            // ── Build display label ────────────────────────────────────────────
            var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                ? course.CalendarCode : null;
            var initials = string.Join(" ", section.InstructorIds
                .Where(id => instructorLookup.TryGetValue(id, out _))
                .Select(id => instructorLookup[id].Initials));
            var label = calCode is not null
                ? $"{calCode} {section.SectionCode}"
                : section.SectionCode;

            // Check if this section is part of a section-level overlay (instructor or tag)
            bool sectionIsOverlay = overlayMatchedSectionIds.Contains(section.Id);

            // ── Meeting-level filter ──────────────────────────────────────────
            foreach (var slot in section.Schedule)
            {
                if (filterRoom)
                {
                    // OR within the room dimension: "Unroomed" sentinel → meeting has
                    // no room assigned; named rooms → meeting is in one of those rooms.
                    bool passes = (unroomedSelected && string.IsNullOrEmpty(slot.RoomId))
                               || (selRooms.Count > 0 && selRooms.Contains(slot.RoomId ?? string.Empty));
                    if (!passes) continue;
                }
                if (filterMeetingType && !selMeetingTypes.Contains(slot.MeetingTypeId ?? string.Empty))
                    continue;

                // Determine if this meeting is part of the overlay
                bool isOverlay = sectionIsOverlay;
                if (!isOverlay && Filter.HasOverlay && Filter.OverlayType == "Room")
                {
                    var overlayId = Filter.SelectedOverlayId ?? string.Empty;
                    isOverlay = slot.RoomId == overlayId;
                }

                allBlocks.Add(new SectionMeetingBlock(slot.Day, slot.StartMinutes, slot.EndMinutes, isOverlay, label, initials, section.Id));
            }
        }

        // ── Add overlay-only sections (those not already shown by filters) ─────
        // For section-level overlays (Instructor, Tag): add all meetings from overlay-matched
        // sections that weren't already added by regular filters.
        // For room overlays: add only the meetings in that room that weren't already added.
        if (Filter.HasOverlay && Filter.OverlayType != "Room")
        {
            foreach (var overlayId in overlayMatchedSectionIds)
            {
                // Skip if this section already got added by the filter loop
                if (allBlocks.OfType<SectionMeetingBlock>().Any(b => b.SectionId == overlayId))
                    continue;

                var section = sections.FirstOrDefault(s => s.Id == overlayId);
                if (section is null) continue;

                // Build display label for this overlay-only section
                var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                    ? course.CalendarCode : null;
                var initials = string.Join(" ", section.InstructorIds
                    .Where(id => instructorLookup.TryGetValue(id, out _))
                    .Select(id => instructorLookup[id].Initials));
                var label = calCode is not null
                    ? $"{calCode} {section.SectionCode}"
                    : section.SectionCode;

                // Add ALL meetings for this overlay-only section
                foreach (var slot in section.Schedule)
                {
                    allBlocks.Add(new SectionMeetingBlock(slot.Day, slot.StartMinutes, slot.EndMinutes, true, label, initials, section.Id));
                }
            }
        }
        else if (Filter.HasOverlay && Filter.OverlayType == "Room")
        {
            // For room overlays: find all meetings in the overlay room that weren't already added
            var overlayRoomId = Filter.SelectedOverlayId ?? string.Empty;

            foreach (var section in sections)
            {
                var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                    ? course.CalendarCode : null;
                var initials = string.Join(" ", section.InstructorIds
                    .Where(id => instructorLookup.TryGetValue(id, out _))
                    .Select(id => instructorLookup[id].Initials));
                var label = calCode is not null
                    ? $"{calCode} {section.SectionCode}"
                    : section.SectionCode;

                foreach (var slot in section.Schedule)
                {
                    // Only add meetings that have the overlay room assigned
                    if (slot.RoomId != overlayRoomId)
                        continue;

                    // Skip if this meeting was already added by the filter loop
                    if (allBlocks.OfType<SectionMeetingBlock>().Any(b =>
                            b.SectionId == section.Id && b.Day == slot.Day &&
                            b.StartMinutes == slot.StartMinutes && b.EndMinutes == slot.EndMinutes))
                        continue;

                    allBlocks.Add(new SectionMeetingBlock(slot.Day, slot.StartMinutes, slot.EndMinutes, true, label, initials, section.Id));
                }
            }
        }

        // ── Instructor-overlay commitments ────────────────────────────────────
        // When an instructor overlay is active, load that instructor's time commitments
        // for the selected semester and inject them into the block list. Commitments are
        // entirely independent of sections — they can appear on any day at any time
        // regardless of what sections the instructor teaches. They enter the layout engine
        // as CommitmentBlocks, which means they participate in the same overlap/column-
        // split logic as section meetings. If a commitment happens to share the exact same
        // time span as a section meeting, they are merged into one tile (stacked rows).
        //
        // Commitments are only shown under instructor overlays, not room or tag overlays,
        // because commitments belong to an instructor, not a room or a tag.
        //
        // CommitmentBlock.IsOverlay is hardcoded true, so they always render red.
        // No dedup guard is needed here — commitments are fetched once, directly from the
        // DB for this instructor+semester, so duplicates cannot arise.
        if (Filter.HasOverlay
            && Filter.OverlayType == "Instructor"
            && !string.IsNullOrEmpty(Filter.SelectedOverlayId))
        {
            var commitments = _commitmentRepo.GetByInstructor(semester.Id, Filter.SelectedOverlayId);
            foreach (var c in commitments)
                allBlocks.Add(new CommitmentBlock(c.Day, c.StartMinutes, c.EndMinutes, c.Name, c.Id));
        }

        // ── Defensive deduplication ────────────────────────────────────────────
        // A section meeting can appear in allBlocks at most twice: once from the filtered
        // pass (Pass 1) and once from the overlay-only pass (Pass 2). Dedup keeps the
        // first occurrence, which preserves the correct IsOverlay flag (Pass 1 sets it
        // based on the section's overlay status; Pass 2 always sets it true).
        // Commitment blocks are fetched once from the DB so they cannot be duplicated,
        // but they pass through the same dedup path for safety.
        // Key = (entity-id, Day, StartMinutes, EndMinutes). Using the entity ID means a
        // section that teaches the same time slot on two different days does NOT dedup
        // across days (Day is part of the key).
        static string BlockId(GridBlock b) => b switch
        {
            SectionMeetingBlock s => s.SectionId,
            CommitmentBlock c     => c.CommitmentId,
            _ => throw new InvalidOperationException($"Unknown GridBlock type: {b.GetType().Name}")
        };

        var seenBlocks    = new HashSet<(string, int, int, int)>();
        var dedupedBlocks = new List<GridBlock>();

        foreach (var block in allBlocks)
        {
            var key = (BlockId(block), block.Day, block.StartMinutes, block.EndMinutes);
            if (seenBlocks.Add(key))
                dedupedBlocks.Add(block);
        }

        allBlocks = dedupedBlocks;

        // ── Time range: always 08:30–22:00 ────────────────────────────────────
        const int firstRow = 8 * 60 + 30;
        const int lastRow  = 22 * 60;

        // ── Build per-day tile lists ───────────────────────────────────────────
        var dayColumns = new List<GridDayColumn>();
        foreach (var dayNum in dayNumbers)
        {
            var dayBlocks = allBlocks
                .Where(b => b.Day == dayNum)
                .OrderBy(b => b.StartMinutes).ThenBy(b => b.EndMinutes)
                .ToList();

            var tiles = ComputeTiles(dayBlocks);
            dayColumns.Add(new GridDayColumn(dayNames[dayNum], tiles));
        }

        GridData = new GridData(firstRow, lastRow, dayColumns);

        // ── Update title bar summary properties ───────────────────────────────
        SemesterLine = _semesterContext.SelectedSemesterDisplay?.DisplayName ?? string.Empty;

        var selectedSubjectNames = Filter.Subjects
            .Where(s => s.IsSelected)
            .Select(s => s.Name)
            .ToList();
        SubjectFilterSummary = selectedSubjectNames.Count > 0
            ? string.Join(" · ", selectedSubjectNames)
            : string.Empty;

        // Stats line counts sections and meetings, not commitments. A "section" is a
        // distinct SectionId; a "meeting" is one SectionMeetingBlock (one time slot for
        // one section on one day). Commitment blocks are excluded from both counts because
        // they are not sections — showing "3 sections · 8 meetings" is meaningful; mixing
        // in commitment tiles would make the number misleading.
        var sectionBlocks = allBlocks.OfType<SectionMeetingBlock>().ToList();
        int sectionCount  = sectionBlocks.Select(b => b.SectionId).Distinct().Count();
        int meetingCount  = sectionBlocks.Count;
        StatsLine = sectionCount == 0
            ? "No sections shown"
            : $"{sectionCount} {(sectionCount == 1 ? "section" : "sections")} · {meetingCount} {(meetingCount == 1 ? "meeting" : "meetings")} shown";
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
        SectionMeetingBlock s => new TileEntry(s.Label, s.Initials, s.SectionId, s.IsOverlay, false),
        CommitmentBlock c     => new TileEntry(c.Name, string.Empty, string.Empty, true, true),
        _ => throw new InvalidOperationException($"Unknown GridBlock type: {block.GetType().Name}")
    };

    /// <summary>
    /// Builds positioned tiles for one day's blocks.
    /// Blocks with identical start+end are merged into a single tile (stacked entries).
    /// Overlapping blocks (different time spans) are placed side-by-side.
    /// </summary>
    private static List<GridTile> ComputeTiles(List<GridBlock> blocks)
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

                tiles.Add(new GridTile(m.Entries, m.Start, m.End, col, cluster.Count));
            }

            // Fix OverlapCount to actual column count used
            int actualCols = colEnds.Count;
            for (int t = tiles.Count - cluster.Count; t < tiles.Count; t++)
            {
                var old = tiles[t];
                tiles[t] = old with { OverlapCount = actualCols };
            }
        }

        return tiles;
    }

}
