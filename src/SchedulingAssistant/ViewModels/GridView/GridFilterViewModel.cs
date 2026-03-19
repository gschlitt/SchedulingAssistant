using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// Holds all filter state for the Schedule Grid.
/// Option lists are rebuilt by PopulateOptions() on each Reload().
/// FilterChanged fires whenever any item's IsSelected toggles.
/// </summary>
public partial class GridFilterViewModel : ViewModelBase
{
    // ── Sentinel IDs for "no value assigned" filter options ───────────────────
    //
    // The instructor filter has two sentinel items at the top of its listbox:
    //   • "Show Unstaffed"      — shows ONLY sections with no instructor assignment.
    //   • "Emphasize Unstaffed" — shows ALL sections, but staffed ones receive a
    //                             strikethrough in the Schedule Grid to de-emphasise them.
    //
    // The room filter has one sentinel:
    //   • "Unroomed" — shows only meetings with no room assigned.
    //
    // All sentinels are implemented as regular FilterItemViewModels with sentinel ID
    // strings so they flow through the standard IsSelected/FilterChanged machinery.
    // ScheduleGridViewModel strips these IDs from the selected-ID sets before applying
    // predicates, converting them to separate boolean flags instead.
    //
    // Mutual exclusion rules for instructors:
    //   - "Show Unstaffed" selected   → "Emphasize Unstaffed" and all named items disabled.
    //   - "Emphasize Unstaffed" selected → "Show Unstaffed" and all named items disabled.
    //   - Any named instructor selected  → both sentinels disabled.
    //   - Nothing selected               → everything enabled.
    // Enforcement is in RefreshInstructorMutualExclusion / RefreshRoomMutualExclusion.

    public const string NotStaffedId         = "__not_staffed__";
    public const string EmphasizeUnstaffedId = "__emphasize_unstaffed__";
    public const string UnroomedId           = "__unroomed__";

    // Live references to the current sentinel items, held so their IsSelected state
    // can be preserved across PopulateOptions() rebuilds and so mutual-exclusion
    // logic can address them directly without searching the collection each time.
    private FilterItemViewModel? _notStaffedItem;
    private FilterItemViewModel? _emphasizeUnstaffedItem;
    private FilterItemViewModel? _unroomedItem;

    // ── Option lists (one per filter dimension) ──────────────────────────────

    // Instructors and Rooms always contain their sentinel at index 0, followed by
    // named items. NamedInstructors / NamedRooms are mirror collections of the
    // same objects but without the sentinel; they are kept in sync by PopulateOptions()
    // and are used by the overlay listboxes, where the sentinel has no meaning.
    public ObservableCollection<FilterItemViewModel> Instructors      { get; } = new();
    public ObservableCollection<FilterItemViewModel> Rooms            { get; } = new();
    public ObservableCollection<FilterItemViewModel> NamedInstructors { get; } = new();
    public ObservableCollection<FilterItemViewModel> NamedRooms       { get; } = new();
    public ObservableCollection<FilterItemViewModel> Subjects     { get; } = new();
    public ObservableCollection<FilterItemViewModel> Campuses     { get; } = new();
    public ObservableCollection<FilterItemViewModel> SectionTypes { get; } = new();
    public ObservableCollection<FilterItemViewModel> Tags         { get; } = new();
    public ObservableCollection<FilterItemViewModel> MeetingTypes { get; } = new();
    public ObservableCollection<FilterItemViewModel> Levels       { get; } = new();
    public ObservableCollection<FilterItemViewModel> Courses      { get; } = new();

    // ── Derived selected-ID sets (computed on demand) ─────────────────────────

    public HashSet<string> SelectedInstructorIds  => SelectedIds(Instructors);
    public HashSet<string> SelectedRoomIds        => SelectedIds(Rooms);
    public HashSet<string> SelectedSubjectIds     => SelectedIds(Subjects);
    public HashSet<string> SelectedCampusIds      => SelectedIds(Campuses);
    public HashSet<string> SelectedSectionTypeIds => SelectedIds(SectionTypes);
    public HashSet<string> SelectedTagIds         => SelectedIds(Tags);
    public HashSet<string> SelectedMeetingTypeIds => SelectedIds(MeetingTypes);
    public HashSet<string> SelectedLevelIds       => SelectedIds(Levels);
    public HashSet<string> SelectedCourseIds      => SelectedIds(Courses);

    private static HashSet<string> SelectedIds(IEnumerable<FilterItemViewModel> items)
        => items.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet();

    // ── Active state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _activeSummary = string.Empty;

    /// <summary>
    /// True when one or more non-overlay filter dimensions are active (i.e. at least one
    /// checkbox is checked in Instructors, Rooms, Subjects, etc.).  Distinct from
    /// <see cref="IsActive"/>, which is also true when only an overlay is active.
    /// Used by <see cref="ScheduleGridViewModel"/> to decide whether to push a
    /// filtered-section-ID set to <see cref="SectionStore"/> for section-list highlighting.
    /// </summary>
    [ObservableProperty] private bool _hasRegularFilter;

    // ── Overlay state ─────────────────────────────────────────────────────────

    [ObservableProperty] private string? _overlayType = null;      // "Instructor", "Room", "Tag", or null
    [ObservableProperty] private string? _selectedOverlayId = null;
    [ObservableProperty] private string _overlaySummary = string.Empty;

    public bool HasOverlay => OverlayType is not null && !string.IsNullOrEmpty(SelectedOverlayId);

    /// <summary>Fired whenever any filter selection changes. Wired to ScheduleGridViewModel.Reload.</summary>
    public event Action? FilterChanged;

    /// <summary>
    /// Fired whenever the filter header display state changes — i.e. when any item's
    /// IsSelected toggles, when overlay state changes, or when PopulateOptions rebuilds
    /// the option lists. The view subscribes to this to update header labels and colours
    /// without needing to watch all individual collection items itself.
    /// </summary>
    public event Action? HeadersChanged;

    // ── Overlay commands ──────────────────────────────────────────────────────

    [RelayCommand]
    public void SetInstructorOverlay(string? instructorId)
    {
        // Toggle off if the same item is already the active overlay
        if (!string.IsNullOrEmpty(instructorId) && OverlayType == "Instructor" && SelectedOverlayId == instructorId)
            ClearOverlayCore();
        else
        {
            ClearOverlayCore();
            if (!string.IsNullOrEmpty(instructorId))
            {
                OverlayType = "Instructor";
                SelectedOverlayId = instructorId;
            }
        }
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        RefreshDerived();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    [RelayCommand]
    public void SetRoomOverlay(string? roomId)
    {
        if (!string.IsNullOrEmpty(roomId) && OverlayType == "Room" && SelectedOverlayId == roomId)
            ClearOverlayCore();
        else
        {
            ClearOverlayCore();
            if (!string.IsNullOrEmpty(roomId))
            {
                OverlayType = "Room";
                SelectedOverlayId = roomId;
            }
        }
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        RefreshDerived();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    [RelayCommand]
    public void SetTagOverlay(string? tagId)
    {
        if (!string.IsNullOrEmpty(tagId) && OverlayType == "Tag" && SelectedOverlayId == tagId)
            ClearOverlayCore();
        else
        {
            ClearOverlayCore();
            if (!string.IsNullOrEmpty(tagId))
            {
                OverlayType = "Tag";
                SelectedOverlayId = tagId;
            }
        }
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        RefreshDerived();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    [RelayCommand]
    public void ClearOverlay()
    {
        ClearOverlayCore();
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        RefreshDerived();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    private void ClearOverlayCore()
    {
        OverlayType = null;
        SelectedOverlayId = null;
    }

    private void RefreshOverlaySummary()
    {
        if (!HasOverlay)
        {
            OverlaySummary = string.Empty;
            return;
        }

        // Resolve ID to name using the relevant collection
        string? name = null;
        if (OverlayType == "Instructor")
            name = Instructors.FirstOrDefault(i => i.Id == SelectedOverlayId)?.Name;
        else if (OverlayType == "Room")
            name = Rooms.FirstOrDefault(r => r.Id == SelectedOverlayId)?.Name;
        else if (OverlayType == "Tag")
            name = Tags.FirstOrDefault(t => t.Id == SelectedOverlayId)?.Name;

        if (name is null)
            OverlaySummary = string.Empty;
        else
            OverlaySummary = $"Overlay: {OverlayType} {name}";
    }

    /// <summary>
    /// Updates the IsOverlayActive flag on each item in Instructors, Rooms, and Tags
    /// to reflect the current overlay state. Called after any overlay change or rebuild.
    /// </summary>
    private void RefreshOverlayActiveStates()
    {
        foreach (var item in Instructors)
            item.IsOverlayActive = OverlayType == "Instructor" && item.Id == SelectedOverlayId;
        foreach (var item in Rooms)
            item.IsOverlayActive = OverlayType == "Room" && item.Id == SelectedOverlayId;
        foreach (var item in Tags)
            item.IsOverlayActive = OverlayType == "Tag" && item.Id == SelectedOverlayId;
    }

    // ── Populate ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds option lists from the full set of available entities, regardless of
    /// which are currently used by sections in the loaded semester(s).
    /// Preserves IsSelected state for items that still exist (matched by Id).
    /// Should be called at the start of ScheduleGridViewModel.Reload(), BEFORE
    /// the filter change event is subscribed for this cycle.
    /// </summary>
    public void PopulateOptions(
        IReadOnlyDictionary<string, Instructor> instructorLookup,
        IReadOnlyDictionary<string, Room>       roomLookup,
        IReadOnlyDictionary<string, Subject>    subjectLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> campusLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> sectionTypeLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> tagLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> meetingTypeLookup,
        IReadOnlyDictionary<string, string> levelLookup,
        IReadOnlyDictionary<string, Course> courseLookup)
    {
        // Instructors: use the FULL instructor lookup (all active instructors), not just
        // those assigned to sections in this semester. This ensures the overlay listbox
        // always shows every instructor so their commitments can be overlaid even when
        // they have no sections scheduled yet. The instructorLookup is already filtered
        // by ShowOnlyActiveInstructors (set in AppSettings), so inactive instructors
        // are excluded regardless.
        RebuildList(Instructors,  instructorLookup.Keys,
            id => instructorLookup.TryGetValue(id, out var v) ? $"{v.FirstName} {v.LastName}" : null);
        // Insert in reverse order: Emphasize Unstaffed goes in first (lands at [0]),
        // then Unstaffed is inserted at [0], pushing Emphasize to [1].
        // Final layout: [0]=Unstaffed, [1]=Unstaffed (Emphasize), [2..]=named instructors.
        InsertSentinelItem(Instructors, ref _emphasizeUnstaffedItem, EmphasizeUnstaffedId, "Unstaffed (Emphasize)");
        InsertSentinelItem(Instructors, ref _notStaffedItem,         NotStaffedId,         "Unstaffed");

        // All rooms, subjects, campuses, section types, tags, and meeting types are shown
        // in full — not filtered to only those present in the current semester's sections.
        // This lets users filter by values that exist in the system even if no section
        // currently uses them, avoiding the "where did my tag go?" confusion.
        RebuildList(Rooms,        roomLookup.Keys,
            id => roomLookup.TryGetValue(id, out var v)
                  ? (string.IsNullOrWhiteSpace(v.Building) ? v.RoomNumber : $"{v.Building} {v.RoomNumber}")
                  : null);
        InsertSentinelItem(Rooms, ref _unroomedItem, UnroomedId, "Unroomed");

        // Keep named-only mirrors in sync (same objects, no sentinels).
        // Use IsSentinel rather than Skip() so the count of sentinels can change without
        // breaking this line.
        SyncCollection(NamedInstructors, Instructors.Where(i => !i.IsSentinel));
        SyncCollection(NamedRooms,       Rooms.Skip(1));

        RebuildList(Subjects,     subjectLookup.Keys,
            id => subjectLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(Campuses,     campusLookup.Keys,
            id => campusLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(SectionTypes, sectionTypeLookup.Keys,
            id => sectionTypeLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(Tags,         tagLookup.Keys,
            id => tagLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(MeetingTypes, meetingTypeLookup.Keys,
            id => meetingTypeLookup.TryGetValue(id, out var v) ? v.Name : null);
        // Levels always show the full fixed set of level bands (0, 100, … 900).
        RebuildList(Levels,       levelLookup.Keys,
            id => levelLookup.TryGetValue(id, out var v) ? v : id);
        // Courses: all courses in the system, sorted by calendar code (e.g. "HIST101").
        RebuildList(Courses, courseLookup.Keys,
            id => courseLookup.TryGetValue(id, out var v) ? v.CalendarCode : null);

        RefreshInstructorMutualExclusion();
        RefreshRoomMutualExclusion();
        RefreshDerived();
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();  // Restore IsOverlayActive flags after collection rebuild
        HeadersChanged?.Invoke();      // Notify view to redraw all filter and overlay headers
    }

    /// <summary>
    /// Rebuilds one option list, preserving selected state for items that still exist.
    /// </summary>
    private void RebuildList(
        ObservableCollection<FilterItemViewModel> list,
        IEnumerable<string> usedIds,
        Func<string, string?> nameResolver)
    {
        // Snapshot currently selected IDs so we can restore after rebuild
        var previouslySelected = list.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet();

        // Unsubscribe all existing items
        foreach (var item in list)
            item.PropertyChanged -= OnItemPropertyChanged;
        list.Clear();

        foreach (var id in usedIds.OrderBy(id => nameResolver(id) ?? id))
        {
            var name = nameResolver(id);
            if (name is null) continue;
            var item = new FilterItemViewModel(id, name)
            {
                IsSelected = previouslySelected.Contains(id)
            };
            item.PropertyChanged += OnItemPropertyChanged;
            list.Add(item);
        }
    }

    /// <summary>
    /// Inserts a sentinel item (e.g. "Not staffed") at index 0 of <paramref name="list"/>.
    /// Called immediately after RebuildList() for the Instructor and Room dimensions.
    ///
    /// Why called after, not before: RebuildList() clears the list and rebuilds it from
    /// scratch. If the sentinel were already in the list, RebuildList() would unsubscribe
    /// and remove it along with the named items. Inserting afterwards also guarantees
    /// index 0 is always the sentinel regardless of alphabetical ordering of named items.
    ///
    /// Selection state is preserved by reading it from the previous sentinel object
    /// (stored in <paramref name="sentinelField"/>) before RebuildList() discarded it.
    /// </summary>
    private void InsertSentinelItem(
        ObservableCollection<FilterItemViewModel> list,
        ref FilterItemViewModel? sentinelField,
        string sentinelId,
        string sentinelName)
    {
        bool wasSelected = sentinelField?.IsSelected ?? false;
        var sentinel = new FilterItemViewModel(sentinelId, sentinelName) { IsSelected = wasSelected, IsSentinel = true };
        sentinel.PropertyChanged += OnItemPropertyChanged;
        list.Insert(0, sentinel);
        sentinelField = sentinel;
    }

    /// <summary>
    /// Replaces <paramref name="target"/> with the items in <paramref name="source"/>.
    /// Used to keep NamedInstructors / NamedRooms in sync with Instructors.Skip(1) /
    /// Rooms.Skip(1) after each PopulateOptions() rebuild. The items are the same
    /// object references, so IsOverlayActive and IsEnabled changes propagate automatically.
    /// </summary>
    private static void SyncCollection(
        ObservableCollection<FilterItemViewModel> target,
        IEnumerable<FilterItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    // ── Clear ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public void ClearAll()
    {
        // Suppress FilterChanged while unchecking — each IsSelected change would
        // otherwise trigger Reload() → PopulateOptions() → RebuildList(), which
        // clears and replaces the collections while we're still iterating them.
        var handler = FilterChanged;
        FilterChanged = null;
        try
        {
            foreach (var col in AllCollections())
                foreach (var item in col.ToList())   // ToList() snapshots so iteration is safe
                    item.IsSelected = false;
            ClearOverlayCore();
        }
        finally
        {
            FilterChanged = handler;
        }
        RefreshDerived();
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    // ── Change tracking ───────────────────────────────────────────────────────

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilterItemViewModel.IsSelected)) return;
        // Re-evaluate mutual exclusion for both sentinel dimensions on every selection
        // change. The cost is a short loop over instructor/room items; acceptable given
        // typical list sizes and the correctness guarantee it provides.
        RefreshInstructorMutualExclusion();
        RefreshRoomMutualExclusion();
        RefreshDerived();
        FilterChanged?.Invoke();
        HeadersChanged?.Invoke();
    }

    /// <summary>
    /// Enforces mutual exclusion among the three instructor-filter groups:
    /// "Show Unstaffed", "Emphasize Unstaffed", and named instructor items.
    ///
    /// Rules:
    ///   - Either sentinel selected  → the other sentinel and all named items are disabled.
    ///   - Any named item selected   → both sentinels are disabled.
    ///   - Nothing selected          → everything is enabled.
    ///
    /// Only IsEnabled is mutated — IsSelected is never forced. The UI CheckBox binds
    /// to both IsSelected (two-way) and IsEnabled, so disabling prevents interaction
    /// without silently unchecking.
    /// </summary>
    private void RefreshInstructorMutualExclusion()
    {
        if (_notStaffedItem == null || _emphasizeUnstaffedItem == null) return;

        var namedItems         = Instructors.Where(i => !i.IsSentinel).ToList();
        bool showSelected      = _notStaffedItem.IsSelected;
        bool emphasizeSelected = _emphasizeUnstaffedItem.IsSelected;
        bool anyNamedSelected  = namedItems.Any(i => i.IsSelected);

        if (showSelected || emphasizeSelected)
        {
            // One sentinel is active: disable the other sentinel and all named items.
            _notStaffedItem.IsEnabled         = !emphasizeSelected;
            _emphasizeUnstaffedItem.IsEnabled  = !showSelected;
            foreach (var item in namedItems) item.IsEnabled = false;
        }
        else
        {
            // Neither sentinel active: disable both sentinels if a named item is selected.
            _notStaffedItem.IsEnabled         = !anyNamedSelected;
            _emphasizeUnstaffedItem.IsEnabled  = !anyNamedSelected;
            foreach (var item in namedItems) item.IsEnabled = true;
        }
    }

    /// <summary>
    /// Enforces mutual exclusion between "Unroomed" and named room items.
    /// Same logic as RefreshInstructorMutualExclusion — see that method for details.
    /// </summary>
    private void RefreshRoomMutualExclusion()
    {
        if (_unroomedItem == null) return;
        if (_unroomedItem.IsSelected)
        {
            foreach (var item in Rooms.Skip(1))
                item.IsEnabled = false;
            return;
        }
        bool anyNamedSelected = Rooms.Skip(1).Any(i => i.IsSelected);
        _unroomedItem.IsEnabled = !anyNamedSelected;
        foreach (var item in Rooms.Skip(1))
            item.IsEnabled = true;
    }

    private void RefreshDerived()
    {
        var parts = new List<string>();
        AppendSummaryPart(parts, "Instructor",    Instructors);
        AppendSummaryPart(parts, "Room",          Rooms);
        AppendSummaryPart(parts, "Subject",       Subjects);
        AppendSummaryPart(parts, "Campus",        Campuses);
        AppendSummaryPart(parts, "Type",          SectionTypes);
        AppendSummaryPart(parts, "Tags",          Tags);
        AppendSummaryPart(parts, "Meeting Type",  MeetingTypes);
        AppendSummaryPart(parts, "Level",         Levels);
        AppendSummaryPart(parts, "Course",        Courses);

        HasRegularFilter = parts.Count > 0;
        IsActive = HasRegularFilter || HasOverlay;
        ActiveSummary = parts.Count > 0
            ? string.Join("  ·  ", parts)
            : "No filters active";
    }

    private static void AppendSummaryPart(
        List<string> parts,
        string label,
        IEnumerable<FilterItemViewModel> items)
    {
        var selected = items.Where(i => i.IsSelected).Select(i => i.Name).ToList();
        if (selected.Count > 0)
            parts.Add($"{label}: {string.Join(", ", selected)}");
    }

    private IEnumerable<ObservableCollection<FilterItemViewModel>> AllCollections()
    {
        yield return Instructors;
        yield return Rooms;
        yield return Subjects;
        yield return Campuses;
        yield return SectionTypes;
        yield return Tags;
        yield return MeetingTypes;
        yield return Levels;
        yield return Courses;
    }
}
