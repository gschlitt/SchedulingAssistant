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
/// when the user has toggled to Meeting View.
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

    /// <summary>Opens a blank editor to add a new meeting to the currently selected semester.</summary>
    [RelayCommand]
    private void Add()
    {
        var semester = _semesterContext.SelectedSemesters.FirstOrDefault()?.Semester;
        if (semester is null) return;

        var meeting = new Meeting { SemesterId = semester.Id };
        OpenEditor(meeting, isNew: true);
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
        var rooms        = _roomRepo.GetAll();
        var instructors  = _instructorRepo.GetAll();
        var campuses     = _campusRepo.GetAll();

        var vm = new MeetingEditViewModel(
            meeting, isNew,
            _meetingRepo, _meetingStore,
            legalStartTimes, instructors, meetingTypes, rooms, campuses, tags);

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

    private void CloseEditor()
    {
        if (EditVm is not null)
        {
            EditVm.EditCompleted -= CloseEditor;
            EditVm = null;
        }
        foreach (var item in Items)
            item.IsExpanded = false;
    }
}
