using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Drives the Event List left panel — the counterpart to <see cref="SectionListViewModel"/>
/// when the user has toggled to Events View.
///
/// <para>
/// In single-semester mode, displays a flat list of <see cref="MeetingListItemViewModel"/>
/// cards. In multi-semester mode, inserts a <see cref="SemesterBannerViewModel"/> header
/// before each semester group and shows a colored left border on every card, matching the
/// pattern used by <see cref="SectionListViewModel"/>.
/// </para>
///
/// <para>
/// Reloads automatically when <see cref="MeetingStore.MeetingsChanged"/> fires (e.g.
/// after a save) or when <see cref="SemesterContext.SelectedSemestersChanged"/> fires.
/// </para>
/// </summary>
public partial class MeetingListViewModel : ViewModelBase
{
    private readonly IMeetingRepository _meetingRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly ICampusRepository _campusRepo;
    private readonly ILegalStartTimeRepository _legalStartTimeRepo;
    private readonly SemesterContext _semesterContext;
    private readonly MeetingStore _meetingStore;
    private readonly WriteLockService _lockService;

    /// <summary>
    /// The transient placeholder card inserted into <see cref="Items"/> during Add
    /// while the event has not yet been saved. Removed by <see cref="CloseEditor"/>.
    /// </summary>
    private MeetingListItemViewModel? _newMeetingPlaceholder;

    /// <summary>
    /// The flat list of items shown in the Event List.
    /// Contains a mix of <see cref="SemesterBannerViewModel"/> (group headers) and
    /// <see cref="MeetingListItemViewModel"/> (event cards). Banners appear only when
    /// more than one semester is loaded; in single-semester mode the list contains only
    /// event cards.
    /// </summary>
    [ObservableProperty] private ObservableCollection<IMeetingListEntry> _items = new();

    /// <summary>
    /// The currently selected list entry. May be an event card or (transiently) a banner.
    /// Use <see cref="SelectedMeetingItem"/> for a cast-safe accessor that returns null for banners.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private IMeetingListEntry? _selectedItem;

    /// <summary>
    /// The currently open inline editor, or null when no card is being edited.
    /// Only one edit form can be open at a time.
    /// </summary>
    [ObservableProperty] private MeetingEditViewModel? _editVm;

    /// <summary>Whether the "Add to which semester?" inline prompt is visible.</summary>
    [ObservableProperty] private bool _isAddSemesterPromptVisible;

    // ── Computed Properties ───────────────────────────────────────────────────

    /// <summary>True when write operations are permitted (the write lock is held).</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>True when more than one semester is currently loaded.</summary>
    public bool IsMultiSemesterMode => _semesterContext.IsMultiSemesterMode;

    /// <summary>
    /// The currently selected event card, or null if nothing is selected or a banner is selected.
    /// </summary>
    public MeetingListItemViewModel? SelectedMeetingItem => SelectedItem as MeetingListItemViewModel;

    /// <summary>
    /// Semester options shown in the Add prompt. Rebuilt each time the prompt opens.
    /// </summary>
    public IReadOnlyList<SemesterPromptItem> AddSemesterOptions { get; private set; } = [];

    // ── Property-Changed Hooks ────────────────────────────────────────────────

    partial void OnSelectedItemChanged(IMeetingListEntry? value)
    {
        // Banners are not selectable entities; ignore clicks on them.
        // CanExecute for Edit/Delete is evaluated against SelectedMeetingItem, so
        // NotifyCanExecuteChangedFor on the attribute handles those automatically.
        if (value is SemesterBannerViewModel)
            return;
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MeetingListViewModel(
        IMeetingRepository meetingRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ISchedulingEnvironmentRepository propertyRepo,
        ICampusRepository campusRepo,
        ILegalStartTimeRepository legalStartTimeRepo,
        SemesterContext semesterContext,
        MeetingStore meetingStore,
        WriteLockService lockService)
    {
        _meetingRepo         = meetingRepo;
        _instructorRepo      = instructorRepo;
        _roomRepo            = roomRepo;
        _propertyRepo        = propertyRepo;
        _campusRepo          = campusRepo;
        _legalStartTimeRepo  = legalStartTimeRepo;
        _semesterContext     = semesterContext;
        _meetingStore        = meetingStore;
        _lockService         = lockService;

        // Reload when the meeting data changes (after any save) or semester selection changes.
        _meetingStore.MeetingsChanged += LoadFromStore;
        _semesterContext.PropertyChanged += OnSemesterContextChanged;

        LoadFromStore();
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SemesterContext.SelectedSemesters)) return;

        OnPropertyChanged(nameof(IsMultiSemesterMode));

        // When semesters change, ScheduleGridViewModel already reloads the MeetingStore,
        // which fires MeetingsChanged → LoadFromStore. But if the grid is not active we
        // reload here directly so the list is never stale.
        var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
        _meetingStore.Reload(_meetingRepo, semIds);
    }

    /// <summary>
    /// Rebuilds <see cref="Items"/> from the current <see cref="MeetingStore"/> cache.
    /// In multi-semester mode, inserts a <see cref="SemesterBannerViewModel"/> before
    /// each semester group and passes semester color to each card for the left border.
    /// </summary>
    private void LoadFromStore()
    {
        // If a new event is being added when a reload arrives (e.g. from a semester
        // change), discard the unsaved placeholder and close the editor.
        if (_newMeetingPlaceholder is not null)
            CloseEditor();

        var instructors    = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var rooms          = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var tagLookup      = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag).ToDictionary(v => v.Id);
        var resourceLookup = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource).ToDictionary(v => v.Id);

        var selectedSemesters = _semesterContext.SelectedSemesters.ToList();
        var selectedIds       = selectedSemesters.Select(s => s.Semester.Id).ToHashSet();

        var previousEditId = EditVm?.MeetingId;
        var previousSelId  = SelectedMeetingItem?.Meeting.Id;

        bool showBanners = selectedSemesters.Count > 1;
        var newItems = new List<IMeetingListEntry>();

        for (int i = 0; i < selectedSemesters.Count; i++)
        {
            var semDisplay = selectedSemesters[i];

            if (showBanners)
                newItems.Add(new SemesterBannerViewModel(semDisplay, i));

            var semName  = semDisplay.Semester.Name;
            var semColor = semDisplay.Semester.Color ?? string.Empty;

            var meetings = _meetingStore.Meetings
                .Where(m => m.SemesterId == semDisplay.Semester.Id)
                .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase);

            foreach (var meeting in meetings)
                newItems.Add(new MeetingListItemViewModel(meeting, instructors, rooms, tagLookup, resourceLookup, semName, semColor));
        }

        Items = new ObservableCollection<IMeetingListEntry>(newItems);

        // Restore selection and edit state after reload.
        if (previousSelId is not null)
            SelectedItem = Items.OfType<MeetingListItemViewModel>().FirstOrDefault(i => i.Meeting.Id == previousSelId);

        if (previousEditId is not null)
        {
            var restored = Items.OfType<MeetingListItemViewModel>().FirstOrDefault(i => i.Meeting.Id == previousEditId);
            if (restored is not null)
                OpenEditor(restored.Meeting, isNew: false);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a blank inline editor for a new event.
    /// In multi-semester mode: if an event is selected, defaults to its semester without
    /// prompting; otherwise shows the "Add to which semester?" inline prompt.
    /// Matches the pattern used by <see cref="SectionListViewModel.Add"/>.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        if (_semesterContext.IsMultiSemesterMode)
        {
            if (SelectedMeetingItem is not null)
                AddToSemester(SelectedMeetingItem.Meeting.SemesterId);
            else
                ShowAddSemesterPrompt();
            return;
        }

        var semester = _semesterContext.SelectedSemesters.FirstOrDefault()?.Semester;
        if (semester is null) return;

        // In single-semester mode insert at the top of the list.
        CreateNewMeetingAt(semester.Id, insertAt: 0);
    }

    /// <summary>
    /// Builds the <see cref="AddSemesterOptions"/> list from the currently selected semesters
    /// and makes the prompt panel visible.
    /// </summary>
    private void ShowAddSemesterPrompt()
    {
        var semesters = _semesterContext.SelectedSemesters.ToList();
        AddSemesterOptions = semesters
            .Select((s, i) => new SemesterPromptItem(s, i))
            .ToList();
        OnPropertyChanged(nameof(AddSemesterOptions));
        IsAddSemesterPromptVisible = true;
    }

    /// <summary>
    /// Called when the user picks a semester from the Add prompt.
    /// Hides the prompt, finds the right insertion point in that semester's group,
    /// and opens the inline editor.
    /// </summary>
    /// <param name="semesterId">The ID of the semester to add to.</param>
    [RelayCommand]
    private void AddToSemester(string semesterId)
    {
        IsAddSemesterPromptVisible = false;
        int insertAt = FindInsertionIndex(semesterId);
        CreateNewMeetingAt(semesterId, insertAt);
    }

    /// <summary>Hides the Add prompt without adding an event.</summary>
    [RelayCommand]
    private void CancelAddPrompt() => IsAddSemesterPromptVisible = false;

    /// <summary>
    /// Creates a placeholder card and inline editor for a new event in the given semester,
    /// inserting the card at <paramref name="insertAt"/>.
    /// </summary>
    /// <param name="semesterId">The target semester for the new event.</param>
    /// <param name="insertAt">Position in <see cref="Items"/> to insert the placeholder card.</param>
    private void CreateNewMeetingAt(string semesterId, int insertAt)
    {
        CloseEditor();

        var semDisplay = _semesterContext.SelectedSemesters.FirstOrDefault(s => s.Semester.Id == semesterId);
        var semName    = semDisplay?.Semester.Name  ?? string.Empty;
        var semColor   = semDisplay?.Semester.Color ?? string.Empty;

        var meeting = new Meeting { SemesterId = semesterId };

        // Share the lookup data between the placeholder card and the editor VM
        // to avoid a duplicate round-trip to the repository.
        var allInstructors   = _instructorRepo.GetAll();
        var allRooms         = _roomRepo.GetAll();
        var instructorLookup = allInstructors.ToDictionary(i => i.Id);
        var roomLookup       = allRooms.ToDictionary(r => r.Id);
        var tagLookup        = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag).ToDictionary(v => v.Id);
        var resourceLookup   = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource).ToDictionary(v => v.Id);

        _newMeetingPlaceholder = new MeetingListItemViewModel(meeting, instructorLookup, roomLookup, tagLookup, resourceLookup, semName, semColor)
        {
            IsBeingCreated = true
        };

        // Clamp insertAt to a valid range in case the list changed since the index was computed.
        insertAt = Math.Clamp(insertAt, 0, Items.Count);
        Items.Insert(insertAt, _newMeetingPlaceholder);

        var academicYearId  = _semesterContext.SelectedAcademicYear?.Id ?? string.Empty;
        var legalStartTimes = string.IsNullOrEmpty(academicYearId)
            ? new List<LegalStartTime>()
            : _legalStartTimeRepo.GetAll(academicYearId);

        var vm = new MeetingEditViewModel(
            meeting, isNew: true,
            _meetingRepo, _meetingStore,
            legalStartTimes,
            allInstructors,
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.MeetingType),
            allRooms,
            _campusRepo.GetAll(),
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag),
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource),
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.StaffType));

        vm.EditCompleted += CloseEditor;
        EditVm = vm;

        SelectedItem = _newMeetingPlaceholder;
        _newMeetingPlaceholder.IsExpanded = true;
    }

    /// <summary>
    /// Finds the best index at which to insert a new event for the given semester.
    /// Returns the index immediately after the last existing event in that semester's
    /// group, or immediately after the group's banner if the group is empty.
    /// Falls back to end-of-list if neither is found.
    /// </summary>
    /// <param name="semesterId">Target semester ID.</param>
    private int FindInsertionIndex(string semesterId)
    {
        // Walk backward to find the last event belonging to this semester.
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is MeetingListItemViewModel vm && vm.Meeting.SemesterId == semesterId)
                return i + 1;
        }

        // No events yet — insert right after the semester's banner.
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is SemesterBannerViewModel banner && banner.SemesterId == semesterId)
                return i + 1;
        }

        return Items.Count;
    }

    /// <summary>Opens the inline editor for the currently selected event.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (SelectedMeetingItem is null) return;
        OpenEditor(SelectedMeetingItem.Meeting, isNew: false);
    }

    private bool CanEdit() => SelectedMeetingItem is not null;

    /// <summary>Deletes the currently selected event after confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedMeetingItem is null) return;
        _meetingRepo.Delete(SelectedMeetingItem.Meeting.Id);

        var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
        _meetingStore.Reload(_meetingRepo, semIds);
    }

    private bool CanDelete() => SelectedMeetingItem is not null;

    // ── Editor lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MeetingEditViewModel"/> for the given meeting and sets
    /// <see cref="EditVm"/>. Wires the <c>EditCompleted</c> callback to collapse the editor.
    /// </summary>
    private void OpenEditor(Meeting meeting, bool isNew)
    {
        CloseEditor();

        var academicYearId = _semesterContext.SelectedAcademicYear?.Id ?? string.Empty;
        var legalStartTimes = string.IsNullOrEmpty(academicYearId)
            ? new List<LegalStartTime>()
            : _legalStartTimeRepo.GetAll(academicYearId);

        var meetingTypes = _propertyRepo.GetAll(SchedulingEnvironmentTypes.MeetingType);
        var tags         = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag);
        var resources    = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource);
        var staffTypes   = _propertyRepo.GetAll(SchedulingEnvironmentTypes.StaffType);
        var rooms        = _roomRepo.GetAll();
        var instructors  = _instructorRepo.GetAll();
        var campuses     = _campusRepo.GetAll();

        var vm = new MeetingEditViewModel(
            meeting, isNew,
            _meetingRepo, _meetingStore,
            legalStartTimes, instructors, meetingTypes, rooms, campuses, tags, resources, staffTypes);

        vm.EditCompleted += CloseEditor;
        EditVm = vm;

        // Expand and select the corresponding card.
        var card = Items.OfType<MeetingListItemViewModel>().FirstOrDefault(i => i.Meeting.Id == meeting.Id);
        if (card is not null)
        {
            SelectedItem    = card;
            card.IsExpanded = true;
        }
    }

    /// <summary>
    /// Opens the inline editor for the event with the given ID.
    /// Called by the schedule grid when the user double-clicks an event tile.
    /// If no matching event is found in the current list (e.g. the event's semester
    /// is not selected) the call is silently ignored.
    /// </summary>
    /// <param name="meetingId">The ID of the event to edit.</param>
    public void EditMeetingById(string meetingId)
    {
        var item = Items.OfType<MeetingListItemViewModel>().FirstOrDefault(i => i.Meeting.Id == meetingId);
        if (item is null) return;
        OpenEditor(item.Meeting, isNew: false);
    }

    private void CloseEditor()
    {
        // Remove the unsaved placeholder card if one exists (Add flow, cancelled or saved).
        // After a successful save the store reload will add the persisted event back.
        if (_newMeetingPlaceholder is not null)
        {
            Items.Remove(_newMeetingPlaceholder);
            _newMeetingPlaceholder = null;
        }

        if (EditVm is not null)
        {
            EditVm.EditCompleted -= CloseEditor;
            EditVm = null;
        }

        foreach (var item in Items.OfType<MeetingListItemViewModel>())
            item.IsExpanded = false;
    }
}
