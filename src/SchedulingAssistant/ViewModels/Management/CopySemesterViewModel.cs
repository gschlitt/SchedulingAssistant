using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public enum CopyState { Ready, Complete }

public partial class CopySemesterViewModel : ViewModelBase, IDisposable
{
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly IDatabaseContext _db;
    private readonly WriteLockService _lockService;

    /// <summary>True when the current user holds the write lock; gates all Copy Semester controls.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    private List<Section>? _sourceSections;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _fromAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _fromSemesters = new();
    [ObservableProperty] private Semester? _fromSemester;

    [ObservableProperty] private AcademicYear? _toAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _toSemesters = new();
    [ObservableProperty] private Semester? _toSemester;

    [ObservableProperty] private bool _isCopyEnabled;

    // Copy options — top two are mutually exclusive
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreSubOptionsEnabled))]
    [NotifyPropertyChangedFor(nameof(AreMeetingSubOptionsEnabled))]
    private bool _copyDesignationsOnly = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreSubOptionsEnabled))]
    [NotifyPropertyChangedFor(nameof(AreMeetingSubOptionsEnabled))]
    private bool _copyAndMore = false;

    public bool AreSubOptionsEnabled => CopyAndMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreMeetingSubOptionsEnabled))]
    private bool _includeAllMeetingTimes;

    public bool AreMeetingSubOptionsEnabled => CopyAndMore && IncludeAllMeetingTimes;

    [ObservableProperty] private bool _includeSectionType;
    [ObservableProperty] private bool _includeAllTags;
    [ObservableProperty] private bool _includeAllStaff;
    [ObservableProperty] private bool _includeRoomAssignments;
    [ObservableProperty] private bool _includeMeetingTypeAssignments;
    [ObservableProperty] private bool _includeAllReserves;

    partial void OnCopyDesignationsOnlyChanged(bool value) { if (value) CopyAndMore = false; }
    partial void OnCopyAndMoreChanged(bool value) { if (value) CopyDesignationsOnly = false; }

    // UI state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    private CopyState _state = CopyState.Ready;

    [ObservableProperty] private string? _statusMessage;

    public bool IsReady => State == CopyState.Ready;
    public bool IsComplete => State == CopyState.Complete;

    public CopySemesterViewModel(
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        ISectionRepository sectionRepo,
        IDatabaseContext db,
        WriteLockService lockService)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _db = db;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        FromAcademicYear = AcademicYears.FirstOrDefault();
        ToAcademicYear = AcademicYears.FirstOrDefault();
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        CopyCommand.NotifyCanExecuteChanged();
    }

    partial void OnFromAcademicYearChanged(AcademicYear? value)
    {
        LoadFromSemesters();
        UpdateCopyEnabled();
    }

    partial void OnFromSemesterChanged(Semester? value)
    {
        UpdateCopyEnabled();
    }

    partial void OnToAcademicYearChanged(AcademicYear? value)
    {
        LoadToSemesters();
        UpdateCopyEnabled();
    }

    partial void OnToSemesterChanged(Semester? value)
    {
        UpdateCopyEnabled();
    }

    private void LoadFromSemesters()
    {
        if (FromAcademicYear is null)
        {
            FromSemesters = new ObservableCollection<Semester>();
            FromSemester = null;
            return;
        }
        FromSemesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(FromAcademicYear.Id));
        FromSemester = FromSemesters.FirstOrDefault();
    }

    private void LoadToSemesters()
    {
        if (ToAcademicYear is null)
        {
            ToSemesters = new ObservableCollection<Semester>();
            ToSemester = null;
            return;
        }
        ToSemesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(ToAcademicYear.Id));
        ToSemester = ToSemesters.FirstOrDefault();
    }

    private void UpdateCopyEnabled()
    {
        IsCopyEnabled = FromAcademicYear is not null && FromSemester is not null &&
                        ToAcademicYear is not null && ToSemester is not null &&
                        (FromAcademicYear.Id != ToAcademicYear.Id || FromSemester.Id != ToSemester.Id);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Copy()
    {
        StatusMessage = null;

        // Guard: target must be empty
        if (_sectionRepo.GetAll(ToSemester!.Id).Count > 0)
        {
            StatusMessage = "The destination semester already contains sections. Please choose an empty semester.";
            return;
        }

        // Load source
        _sourceSections = _sectionRepo.GetAll(FromSemester!.Id);
        if (_sourceSections.Count == 0)
        {
            StatusMessage = "The source semester has no sections to copy.";
            return;
        }

        ExecuteCopy();
    }

    private void ExecuteCopy()
    {
        var count = 0;

        // Use the nullable-tx pattern so WASM demo contexts (SupportsTransactions = false)
        // can complete without throwing NotSupportedException on Connection access. (F14.)
        using var tx = _db.SupportsTransactions ? _db.Connection.BeginTransaction() : null;
        try
        {
            foreach (var source in _sourceSections!)
            {
                var newSection = BuildNewSection(source);
                _sectionRepo.Insert(newSection, tx);
                count++;
            }
            tx?.Commit();
        }
        catch
        {
            tx?.Rollback();
            throw;
        }

        StatusMessage = $"Copied {count} section(s) to {ToSemester!.Name}.";
        State = CopyState.Complete;
    }

    private Section BuildNewSection(Section source)
    {
        var s = new Section
        {
            SemesterId = ToSemester!.Id,
            CourseId = source.CourseId,
            SectionCode = source.SectionCode,
            CampusId = source.CampusId,
        };

        if (!CopyAndMore)
            return s;

        if (IncludeSectionType)
            s.SectionTypeId = source.SectionTypeId;

        if (IncludeAllTags)
            s.TagIds = new List<string>(source.TagIds);

        if (IncludeAllStaff)
            s.InstructorAssignments = source.InstructorAssignments
                .Select(a => new InstructorAssignment
                {
                    InstructorId = a.InstructorId,
                    Workload = null
                }).ToList();

        if (IncludeAllMeetingTimes)
            s.Schedule = source.Schedule
                .Select(m => new SectionDaySchedule
                {
                    Day = m.Day,
                    StartMinutes = m.StartMinutes,
                    DurationMinutes = m.DurationMinutes,
                    Frequency = m.Frequency,
                    RoomId = IncludeRoomAssignments ? m.RoomId : null,
                    MeetingTypeId = IncludeMeetingTypeAssignments ? m.MeetingTypeId : null,
                }).ToList();

        if (IncludeAllReserves)
            s.Reserves = source.Reserves
                .Select(r => new SectionReserve
                {
                    ReserveId = r.ReserveId,
                    Code = r.Code
                }).ToList();

        return s;
    }

    [RelayCommand]
    private void Done() => NavigateBackToAcademicYears();

    [RelayCommand]
    private void Cancel() => NavigateBackToAcademicYears();

    /// <summary>
    /// Callback invoked when the user completes or cancels the copy flow.
    /// Set by the caller (e.g. <see cref="MainWindowViewModel"/>) after construction
    /// to avoid a direct dependency on the service container.
    /// </summary>
    public Action? NavigateToAcademicYears { get; set; }

    private void NavigateBackToAcademicYears() => NavigateToAcademicYears?.Invoke();

    /// <inheritdoc/>
    public void Dispose()
    {
        _lockService.LockStateChanged -= OnLockStateChanged;
    }
}
