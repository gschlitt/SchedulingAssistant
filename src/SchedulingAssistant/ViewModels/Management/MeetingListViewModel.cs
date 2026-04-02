using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Drives the Meeting List left panel — the counterpart to <see cref="SectionListViewModel"/>
/// when the user has toggled to Events View.
///
/// <para>
/// Displays a list of <see cref="MeetingListItemViewModel"/> cards for the currently
/// selected semester(s). One card may be expanded inline for editing via
/// <see cref="MeetingEditViewModel"/>. Adding a new meeting inserts a transient card
/// at the top of the list.
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
    /// while the meeting has not yet been saved. Removed by <see cref="CloseEditor"/>.
    /// </summary>
    private MeetingListItemViewModel? _newMeetingPlaceholder;

    /// <summary>The displayed list of meeting cards.</summary>
    [ObservableProperty] private ObservableCollection<MeetingListItemViewModel> _items = new();

    /// <summary>The currently selected card, or null when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private MeetingListItemViewModel? _selectedItem;

    /// <summary>
    /// The currently open inline editor, or null when no card is being edited.
    /// Only one edit form can be open at a time.
    /// </summary>
    [ObservableProperty] private MeetingEditViewModel? _editVm;

    /// <summary>True when write operations are permitted (the write lock is held).</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

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

        // When semesters change, ScheduleGridViewModel already reloads the MeetingStore,
        // which fires MeetingsChanged → LoadFromStore. But if the grid is not active we
        // reload here directly so the list is never stale.
        var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
        _meetingStore.Reload(_meetingRepo, semIds);
    }

    /// <summary>Rebuilds <see cref="Items"/> from the current <see cref="MeetingStore"/> cache.</summary>
    private void LoadFromStore()
    {
        // If a new meeting is being added when a reload arrives (e.g. from a semester
        // change), discard the unsaved placeholder and close the editor — the unsaved
        // meeting cannot survive a context switch, matching section-list behaviour.
        if (_newMeetingPlaceholder is not null)
            CloseEditor();

        var instructors = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var rooms       = _roomRepo.GetAll().ToDictionary(r => r.Id);

        // Only show meetings for currently selected semesters.
        var selectedIds = _semesterContext.SelectedSemesters
            .Select(s => s.Semester.Id)
            .ToHashSet();

        var previousEditId = EditVm?.MeetingId;
        var previousSelId  = SelectedItem?.Meeting.Id;

        Items.Clear();
        foreach (var meeting in _meetingStore.Meetings.Where(m => selectedIds.Contains(m.SemesterId)))
            Items.Add(new MeetingListItemViewModel(meeting, instructors, rooms));

        // Restore selection and edit state after reload.
        if (previousSelId is not null)
            SelectedItem = Items.FirstOrDefault(i => i.Meeting.Id == previousSelId);

        if (previousEditId is not null)
        {
            var restored = Items.FirstOrDefault(i => i.Meeting.Id == previousEditId);
            if (restored is not null)
                OpenEditor(restored.Meeting, isNew: false);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a blank inline editor at the top of the list for a new meeting.
    /// Inserts a transient placeholder card (parallel to the section list Add flow)
    /// rather than routing through <see cref="OpenEditor"/>, so that the placeholder
    /// is in <see cref="Items"/> before the card lookup runs.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        var semester = _semesterContext.SelectedSemesters.FirstOrDefault()?.Semester;
        if (semester is null) return;

        // Close any currently open editor (collapses its card and removes any
        // previous placeholder) before inserting the new one.
        CloseEditor();

        var meeting = new Meeting { SemesterId = semester.Id };

        // Share the lookup data between the placeholder card and the editor VM
        // to avoid a duplicate round-trip to the repository.
        var allInstructors  = _instructorRepo.GetAll();
        var allRooms        = _roomRepo.GetAll();
        var instructorLookup = allInstructors.ToDictionary(i => i.Id);
        var roomLookup       = allRooms.ToDictionary(r => r.Id);

        _newMeetingPlaceholder = new MeetingListItemViewModel(meeting, instructorLookup, roomLookup)
        {
            IsBeingCreated = true
        };
        Items.Insert(0, _newMeetingPlaceholder);

        // Build the editor VM directly (not via OpenEditor) so we can wire the
        // placeholder-removal into the EditCompleted callback.
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
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource));

        vm.EditCompleted += CloseEditor;
        EditVm = vm;

        SelectedItem = _newMeetingPlaceholder;
        _newMeetingPlaceholder.IsExpanded = true;
    }

    /// <summary>Opens the inline editor for the currently selected meeting.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (SelectedItem is null) return;
        OpenEditor(SelectedItem.Meeting, isNew: false);
    }

    private bool CanEdit() => SelectedItem is not null;

    /// <summary>Deletes the currently selected meeting after confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedItem is null) return;
        _meetingRepo.Delete(SelectedItem.Meeting.Id);

        var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
        _meetingStore.Reload(_meetingRepo, semIds);
    }

    private bool CanDelete() => SelectedItem is not null;

    // ── Editor lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MeetingEditViewModel"/> for the given meeting and sets
    /// <see cref="EditVm"/>. Wires the <c>EditCompleted</c> callback to collapse the editor.
    /// </summary>
    private void OpenEditor(Meeting meeting, bool isNew)
    {
        // Collapse any currently open editor first.
        CloseEditor();

        var academicYearId = _semesterContext.SelectedAcademicYear?.Id ?? string.Empty;
        var legalStartTimes = string.IsNullOrEmpty(academicYearId)
            ? new List<LegalStartTime>()
            : _legalStartTimeRepo.GetAll(academicYearId);

        var meetingTypes = _propertyRepo.GetAll(SchedulingEnvironmentTypes.MeetingType);
        var tags         = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag);
        var resources    = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource);
        var rooms        = _roomRepo.GetAll();
        var instructors  = _instructorRepo.GetAll();
        var campuses     = _campusRepo.GetAll();

        var vm = new MeetingEditViewModel(
            meeting, isNew,
            _meetingRepo, _meetingStore,
            legalStartTimes, instructors, meetingTypes, rooms, campuses, tags, resources);

        vm.EditCompleted += CloseEditor;
        EditVm = vm;

        // Expand and select the corresponding card.
        var card = Items.FirstOrDefault(i => i.Meeting.Id == meeting.Id);
        if (card is not null)
        {
            SelectedItem      = card;
            card.IsExpanded   = true;
        }
    }

    /// <summary>
    /// Opens the inline editor for the meeting with the given ID.
    /// Called by the schedule grid when the user double-clicks a meeting tile.
    /// If no matching meeting is found in the current list (e.g. the meeting's semester
    /// is not selected) the call is silently ignored.
    /// </summary>
    /// <param name="meetingId">The ID of the meeting to edit.</param>
    public void EditMeetingById(string meetingId)
    {
        var item = Items.FirstOrDefault(i => i.Meeting.Id == meetingId);
        if (item is null) return;
        OpenEditor(item.Meeting, isNew: false);
    }

    private void CloseEditor()
    {
        // Remove the unsaved placeholder card if one exists (Add flow, cancelled or saved).
        // After a successful save the store reload will add the persisted meeting back.
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

        foreach (var item in Items)
            item.IsExpanded = false;
    }
}
