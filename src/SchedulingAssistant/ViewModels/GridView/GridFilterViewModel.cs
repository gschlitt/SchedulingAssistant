using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
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
    // ── Option lists (one per filter dimension) ──────────────────────────────

    public ObservableCollection<FilterItemViewModel> Instructors  { get; } = new();
    public ObservableCollection<FilterItemViewModel> Rooms        { get; } = new();
    public ObservableCollection<FilterItemViewModel> Subjects     { get; } = new();
    public ObservableCollection<FilterItemViewModel> Campuses     { get; } = new();
    public ObservableCollection<FilterItemViewModel> SectionTypes { get; } = new();
    public ObservableCollection<FilterItemViewModel> Tags         { get; } = new();
    public ObservableCollection<FilterItemViewModel> MeetingTypes { get; } = new();
    public ObservableCollection<FilterItemViewModel> Levels       { get; } = new();

    // ── Derived selected-ID sets (computed on demand) ─────────────────────────

    public HashSet<string> SelectedInstructorIds  => SelectedIds(Instructors);
    public HashSet<string> SelectedRoomIds        => SelectedIds(Rooms);
    public HashSet<string> SelectedSubjectIds     => SelectedIds(Subjects);
    public HashSet<string> SelectedCampusIds      => SelectedIds(Campuses);
    public HashSet<string> SelectedSectionTypeIds => SelectedIds(SectionTypes);
    public HashSet<string> SelectedTagIds         => SelectedIds(Tags);
    public HashSet<string> SelectedMeetingTypeIds => SelectedIds(MeetingTypes);
    public HashSet<string> SelectedLevelIds       => SelectedIds(Levels);

    private static HashSet<string> SelectedIds(IEnumerable<FilterItemViewModel> items)
        => items.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet();

    // ── Active state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _activeSummary = string.Empty;

    // ── Overlay state ─────────────────────────────────────────────────────────

    [ObservableProperty] private string? _overlayType = null;      // "Instructor", "Room", "Tag", or null
    [ObservableProperty] private string? _selectedOverlayId = null;
    [ObservableProperty] private string _overlaySummary = string.Empty;

    public bool HasOverlay => OverlayType is not null && !string.IsNullOrEmpty(SelectedOverlayId);

    /// <summary>Fired whenever any filter selection changes. Wired to ScheduleGridViewModel.Reload.</summary>
    public event Action? FilterChanged;

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
    }

    [RelayCommand]
    public void ClearOverlay()
    {
        ClearOverlayCore();
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();
        RefreshDerived();
        FilterChanged?.Invoke();
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
    /// Rebuilds option lists from the sections visible in the current semester.
    /// Preserves IsSelected state for items that still exist (matched by Id).
    /// Should be called at the start of ScheduleGridViewModel.Reload(), BEFORE
    /// the filter change event is subscribed for this cycle.
    /// </summary>
    public void PopulateOptions(
        IEnumerable<Section>              sections,
        IReadOnlyDictionary<string, Instructor> instructorLookup,
        IReadOnlyDictionary<string, Room>       roomLookup,
        IReadOnlyDictionary<string, Subject>    subjectLookup,
        IReadOnlyDictionary<string, Course>     courseLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> campusLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> sectionTypeLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> tagLookup,
        IReadOnlyDictionary<string, SectionPropertyValue> meetingTypeLookup,
        IReadOnlyDictionary<string, string> levelLookup)
    {
        var sectionList = sections.ToList();

        // Build sets of IDs actually used in this semester
        var usedInstructorIds  = sectionList.SelectMany(s => s.InstructorIds).ToHashSet();
        var usedRoomIds        = sectionList.SelectMany(s => s.Schedule.Select(m => m.RoomId))
                                            .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToHashSet();
        var usedSubjectIds     = sectionList
                                    .Where(s => !string.IsNullOrEmpty(s.CourseId) && courseLookup.ContainsKey(s.CourseId))
                                    .Select(s => courseLookup[s.CourseId!].SubjectId)
                                    .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToHashSet();
        var usedCampusIds      = sectionList.Select(s => s.CampusId)
                                            .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToHashSet();
        var usedSectionTypeIds = sectionList.Select(s => s.SectionTypeId)
                                            .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToHashSet();
        var usedTagIds         = sectionList.SelectMany(s => s.TagIds).ToHashSet();
        var usedMeetingTypeIds = sectionList.SelectMany(s => s.Schedule.Select(m => m.MeetingTypeId))
                                            .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToHashSet();
        var usedLevelIds       = new HashSet<string>();
        foreach (var section in sectionList)
        {
            if (section.CourseId != null && courseLookup.TryGetValue(section.CourseId, out var course))
            {
                var level = course.Level;
                if (!string.IsNullOrEmpty(level))
                    usedLevelIds.Add(level);
            }
        }

        RebuildList(Instructors,  usedInstructorIds,
            id => instructorLookup.TryGetValue(id, out var v) ? $"{v.LastName}, {v.FirstName}" : null);
        RebuildList(Rooms,        usedRoomIds,
            id => roomLookup.TryGetValue(id, out var v)
                  ? (string.IsNullOrWhiteSpace(v.Building) ? v.RoomNumber : $"{v.Building} {v.RoomNumber}")
                  : null);
        RebuildList(Subjects,     usedSubjectIds,
            id => subjectLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(Campuses,     usedCampusIds,
            id => campusLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(SectionTypes, usedSectionTypeIds,
            id => sectionTypeLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(Tags,         usedTagIds,
            id => tagLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(MeetingTypes, usedMeetingTypeIds,
            id => meetingTypeLookup.TryGetValue(id, out var v) ? v.Name : null);
        RebuildList(Levels,       usedLevelIds,
            id => levelLookup.TryGetValue(id, out var v) ? v : id);

        RefreshDerived();
        RefreshOverlaySummary();
        RefreshOverlayActiveStates();  // Restore IsOverlayActive flags after collection rebuild
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
    }

    // ── Change tracking ───────────────────────────────────────────────────────

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilterItemViewModel.IsSelected)) return;
        RefreshDerived();
        FilterChanged?.Invoke();
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

        IsActive = parts.Count > 0 || HasOverlay;
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
    }
}
