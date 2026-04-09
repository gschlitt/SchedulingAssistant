using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Inline editor for a <see cref="Meeting"/>. No step-gate is required — the
/// Title field is the only prerequisite, and all other fields are immediately
/// available. Reuses <see cref="SectionMeetingViewModel"/> for day/time/room slots
/// and <see cref="InstructorSelectionViewModel"/> for attendees.
/// </summary>
public partial class MeetingEditViewModel : ViewModelBase
{
    private readonly Meeting _meeting;
    private readonly IMeetingRepository _meetingRepo;
    private readonly MeetingStore _meetingStore;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly IReadOnlyList<SchedulingEnvironmentValue> _meetingTypes;
    private readonly IReadOnlyList<Room> _allRooms;

    // ── Title ──────────────────────────────────────────────────────────────────

    /// <summary>The meeting title. Required: saving is blocked when this is blank.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    /// <summary>Free-text notes about this meeting.</summary>
    [ObservableProperty] private string _notes = string.Empty;

    // ── Schedule slots ─────────────────────────────────────────────────────────

    /// <summary>One entry per scheduled time slot; the user can add or remove entries.</summary>
    [ObservableProperty]
    private ObservableCollection<SectionMeetingViewModel> _slots = new();

    // ── Attendees ──────────────────────────────────────────────────────────────

    /// <summary>All instructors as checkboxes; those with IsSelected=true are saved as attendees.</summary>
    public ObservableCollection<InstructorSelectionViewModel> AttendeeSelections { get; } = new();

    /// <summary>
    /// Bulk-selection presets shown above the instructor list in the attendee popup.
    /// Includes "Everyone", "No one", and one entry per staff type.
    /// Checking a sentinel applies the preset and immediately resets itself.
    /// </summary>
    public ObservableCollection<AttendeeSentinelViewModel> SentinelPresets { get; } = new();

    /// <summary>Maximum number of attendee names shown in the collapsed popup trigger before truncation.</summary>
    private const int AttendeeSummaryMax = 5;

    /// <summary>
    /// Up to <see cref="AttendeeSummaryMax"/> attendee names joined by ", ", followed by
    /// " …+N more" when the list is longer. Shows "(none)" when no attendees are selected.
    /// </summary>
    public string AttendeeSummary
    {
        get
        {
            var selected = AttendeeSelections.Where(a => a.IsSelected).Select(a => a.DisplayName).ToList();
            if (selected.Count == 0) return "(none)";
            if (selected.Count <= AttendeeSummaryMax) return string.Join(", ", selected);
            return string.Join(", ", selected.Take(AttendeeSummaryMax)) + $" …+{selected.Count - AttendeeSummaryMax} more";
        }
    }

    /// <summary>
    /// Full attendee list for the toggle-button tooltip. Null when ≤ <see cref="AttendeeSummaryMax"/>
    /// attendees are selected so no tooltip appears for short lists.
    /// </summary>
    public string? AttendeeSummaryTooltip
    {
        get
        {
            var selected = AttendeeSelections.Where(a => a.IsSelected).Select(a => a.DisplayName).ToList();
            return selected.Count > AttendeeSummaryMax ? string.Join(", ", selected) : null;
        }
    }

    // ── Campus ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Campus> _campuses = new();
    [ObservableProperty] private string? _selectedCampusId;

    // ── Tags ──────────────────────────────────────────────────────────────────

    /// <summary>All available tags as checkboxes; selected ones are saved onto the meeting.</summary>
    public ObservableCollection<TagSelectionViewModel> TagSelections { get; } = new();

    /// <summary>Comma-joined names of selected tags, shown in the collapsed popup trigger.</summary>
    public string TagSummary =>
        TagSelections.Where(t => t.IsSelected).Select(t => t.Value.Name) is var names && names.Any()
            ? string.Join(", ", names)
            : "(none)";

    // ── Resources ─────────────────────────────────────────────────────────────

    /// <summary>All available resources as checkboxes; selected ones are saved onto the meeting.</summary>
    public ObservableCollection<ResourceSelectionViewModel> ResourceSelections { get; } = new();

    /// <summary>Comma-joined names of selected resources, shown in the collapsed popup trigger.</summary>
    public string ResourceSummary =>
        ResourceSelections.Where(r => r.IsSelected).Select(r => r.Value.Name) is var names && names.Any()
            ? string.Join(", ", names)
            : "(none)";

    // ── Meta ──────────────────────────────────────────────────────────────────

    /// <summary>The database ID of the meeting being edited (used to populate the section list header).</summary>
    public string MeetingId => _meeting.Id;

    /// <summary>True for a brand-new meeting that has not yet been saved.</summary>
    public bool IsNew { get; }

    /// <summary>Fired when the edit is saved or cancelled so the list can collapse the form.</summary>
    public event Action? EditCompleted;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the editor for the given meeting.
    /// </summary>
    /// <param name="meeting">The meeting to edit. For a new meeting, pass a freshly constructed instance.</param>
    /// <param name="isNew">True when <paramref name="meeting"/> has not yet been persisted.</param>
    /// <param name="meetingRepo">Repository used by <see cref="SaveCommand"/>.</param>
    /// <param name="meetingStore">Store that is reloaded after a successful save.</param>
    /// <param name="legalStartTimes">Academic-year legal start times for the time-slot pickers.</param>
    /// <param name="allInstructors">Full instructor list — those assigned to the meeting are pre-checked.</param>
    /// <param name="meetingTypes">Available meeting-type property values for the slot dropdowns.</param>
    /// <param name="allRooms">Available rooms for the slot dropdowns.</param>
    /// <param name="campuses">Available campuses for the campus picker.</param>
    /// <param name="allTags">Available tags — those already on the meeting are pre-checked.</param>
    /// <param name="allResources">Available resources — those already on the meeting are pre-checked.</param>
    /// <param name="allStaffTypes">Staff types used to build per-type preset sentinels in the attendee popup.</param>
    public MeetingEditViewModel(
        Meeting meeting,
        bool isNew,
        IMeetingRepository meetingRepo,
        MeetingStore meetingStore,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        IReadOnlyList<Instructor> allInstructors,
        IReadOnlyList<SchedulingEnvironmentValue> meetingTypes,
        IReadOnlyList<Room> allRooms,
        IReadOnlyList<Campus> campuses,
        IReadOnlyList<SchedulingEnvironmentValue> allTags,
        IReadOnlyList<SchedulingEnvironmentValue> allResources,
        IReadOnlyList<SchedulingEnvironmentValue> allStaffTypes)
    {
        _meeting         = meeting;
        _meetingRepo     = meetingRepo;
        _meetingStore    = meetingStore;
        _legalStartTimes = legalStartTimes;
        _meetingTypes    = meetingTypes;
        _allRooms        = allRooms;
        IsNew            = isNew;

        // Populate title and notes from the existing meeting.
        _title = meeting.Title;
        _notes = meeting.Notes;

        // Populate schedule slots from existing schedule.
        foreach (var slot in meeting.Schedule)
            _slots.Add(CreateSlotVm(slot));

        // Populate attendees (all instructors listed; assigned ones pre-checked).
        // No workload concept for meetings — IsSelected is all that matters.
        var assignedIds = meeting.InstructorAssignments.ToDictionary(a => a.InstructorId, a => a.Workload);
        foreach (var instr in allInstructors.OrderBy(i => i.LastName).ThenBy(i => i.FirstName))
        {
            bool selected = assignedIds.TryGetValue(instr.Id, out var w);
            AttendeeSelections.Add(new InstructorSelectionViewModel(instr, selected, w));
        }
        WireSelectionSummary(AttendeeSelections, nameof(AttendeeSummary));
        WireSelectionSummary(AttendeeSelections, nameof(AttendeeSummaryTooltip));

        // Build attendee preset sentinels: universal (Everyone / No one) plus one per staff type.
        var sentinels = new List<AttendeeSentinelViewModel>
        {
            new("Everyone", AttendeeSentinelKind.Everyone),
            new("No one",   AttendeeSentinelKind.NoOne),
        };
        foreach (var st in allStaffTypes.OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            sentinels.Add(new AttendeeSentinelViewModel(st.Name, AttendeeSentinelKind.StaffType, st.Id));

        foreach (var sentinel in sentinels)
        {
            sentinel.PropertyChanged += OnSentinelPropertyChanged;
            SentinelPresets.Add(sentinel);
        }

        // Populate campuses with a leading "(none)" sentinel.
        _campuses.Add(new Campus { Id = "", Name = "(none)" });
        foreach (var c in campuses.OrderBy(c => c.Name))
            _campuses.Add(c);
        _selectedCampusId = meeting.CampusId ?? "";

        // Populate tags (all available tags; meeting's tags pre-checked).
        var meetingTagSet = meeting.TagIds.ToHashSet();
        foreach (var tag in allTags.OrderBy(t => t.Name))
            TagSelections.Add(new TagSelectionViewModel(tag, meetingTagSet.Contains(tag.Id)));
        WireSelectionSummary(TagSelections, nameof(TagSummary));

        // Populate resources (all available resources; meeting's resources pre-checked).
        var meetingResourceSet = meeting.ResourceIds.ToHashSet();
        foreach (var resource in allResources.OrderBy(r => r.Name))
            ResourceSelections.Add(new ResourceSelectionViewModel(resource, meetingResourceSet.Contains(resource.Id)));
        WireSelectionSummary(ResourceSelections, nameof(ResourceSummary));
    }

    /// <summary>
    /// Subscribes to <see cref="INotifyPropertyChanged.PropertyChanged"/> on each item in
    /// <paramref name="collection"/> so that the named summary property fires
    /// <see cref="ObservableObject.OnPropertyChanged(string)"/> whenever <c>IsSelected</c> changes.
    /// </summary>
    /// <typeparam name="T">Item type; must implement <see cref="INotifyPropertyChanged"/>.</typeparam>
    /// <param name="collection">The observable collection to watch.</param>
    /// <param name="summaryPropertyName">The name of the summary property to notify.</param>
    private void WireSelectionSummary<T>(ObservableCollection<T> collection, string summaryPropertyName)
        where T : INotifyPropertyChanged
    {
        foreach (var item in collection)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "IsSelected")
                    OnPropertyChanged(summaryPropertyName);
            };
    }

    // ── Attendee sentinel preset ──────────────────────────────────────────────

    /// <summary>Guards against re-entrant property-change handling while applying a preset.</summary>
    private bool _applyingPreset;

    /// <summary>
    /// Fired when any sentinel's <c>IsSelected</c> changes. When a sentinel is checked,
    /// applies the bulk-selection preset to <see cref="AttendeeSelections"/> and then
    /// immediately resets the sentinel to unchecked (it is a one-shot action, not a state).
    /// </summary>
    private void OnSentinelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_applyingPreset || e.PropertyName != nameof(AttendeeSentinelViewModel.IsSelected)) return;
        var sentinel = (AttendeeSentinelViewModel)sender!;
        if (!sentinel.IsSelected) return;

        _applyingPreset = true;
        try
        {
            ApplySentinelPreset(sentinel);
        }
        finally
        {
            sentinel.IsSelected = false;
            _applyingPreset = false;
        }
    }

    /// <summary>
    /// Sets <c>IsSelected</c> on each instructor item according to the sentinel's kind.
    /// </summary>
    /// <param name="sentinel">The triggered sentinel.</param>
    private void ApplySentinelPreset(AttendeeSentinelViewModel sentinel)
    {
        foreach (var item in AttendeeSelections)
        {
            item.IsSelected = sentinel.Kind switch
            {
                AttendeeSentinelKind.Everyone  => true,
                AttendeeSentinelKind.NoOne     => false,
                AttendeeSentinelKind.StaffType => item.Value.StaffTypeId == sentinel.StaffTypeId,
                _                              => item.IsSelected,
            };
        }
    }

    // ── Slot management ───────────────────────────────────────────────────────

    /// <summary>Adds a new empty time slot to the meeting.</summary>
    [RelayCommand]
    private void AddSlot() => Slots.Add(CreateSlotVm(null));

    /// <summary>Removes the given time slot from the meeting.</summary>
    /// <param name="slot">The slot to remove.</param>
    [RelayCommand]
    private void RemoveSlot(SectionMeetingViewModel slot) => Slots.Remove(slot);

    private SectionMeetingViewModel CreateSlotVm(SectionDaySchedule? existing) =>
        new(_legalStartTimes, includeSaturday: true, includeSunday: false,
            _meetingTypes, _allRooms, existing,
            unit: AppSettings.Current.BlockLengthUnit);

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    /// <summary>Saves the meeting to the repository and reloads the store.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _meeting.Title  = Title.Trim();
        _meeting.Notes  = Notes.Trim();

        // Collect schedule slots (only those with both start and length committed)
        _meeting.Schedule = Slots
            .Select(s => s.ToSchedule())
            .Where(s => s is not null)
            .Cast<SectionDaySchedule>()
            .ToList();

        // Collect selected attendees (meetings have no workload concept).
        _meeting.InstructorAssignments = AttendeeSelections
            .Where(a => a.IsSelected)
            .Select(a => new InstructorAssignment { InstructorId = a.Value.Id })
            .ToList();

        _meeting.CampusId = string.IsNullOrEmpty(SelectedCampusId) ? null : SelectedCampusId;

        _meeting.TagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();

        _meeting.ResourceIds = ResourceSelections
            .Where(r => r.IsSelected)
            .Select(r => r.Value.Id)
            .ToList();

        if (IsNew)
            _meetingRepo.Insert(_meeting);
        else
            _meetingRepo.Update(_meeting);

        // Fire EditCompleted BEFORE reloading the store.
        // MeetingStore.Reload fires MeetingsChanged synchronously, which triggers
        // MeetingListViewModel.LoadFromStore().  LoadFromStore snapshots EditVm at its
        // start to decide whether to re-open the editor after the reload.  If we fire
        // EditCompleted first, CloseEditor() sets EditVm = null so LoadFromStore sees
        // no previous editor and the form stays closed after Apply.
        EditCompleted?.Invoke();

        // Reload after the editor is closed so the list and grid reflect the saved data.
        _meetingStore.Reload(_meetingRepo,
            _meetingStore.MeetingsBySemester.Keys
                .Append(_meeting.SemesterId)
                .Distinct());
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    /// <summary>Discards changes and collapses the editor without saving.</summary>
    [RelayCommand]
    private void Cancel() => EditCompleted?.Invoke();
}

