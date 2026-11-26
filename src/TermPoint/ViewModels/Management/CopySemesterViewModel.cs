using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.Management;

public enum CopyState { Ready, Complete }

public partial class CopySemesterViewModel : ViewModelBase, IDisposable
{
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly SectionHistory? _history;
    private readonly IDatabaseContext _db;
    private readonly WriteLockService _lockService;

    /// <summary>True when the current user holds the write lock; gates all Copy Semester controls.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    private readonly AccessSetupService? _setup;

    private List<Section>? _sourceSections;

    /// <summary>The courses with sections in the source semester that have two or more components (rule 36).</summary>
    private HashSet<string> _multiComponentCourseIds = new();

    /// <summary>The course codes of <see cref="_multiComponentCourseIds"/>, sorted, for the line before Copy.</summary>
    private List<string> _multiComponentCodes = new();

    /// <summary>The ids of the tags that are access watches, kept when the tags option is off (Q40).</summary>
    private HashSet<string> _watchedTagIds = new();

    /// <summary>True when some source section carries a watched tag.</summary>
    private bool _sourceHasWatchedTags;

    /// <summary>
    /// Rule 36's line, shown while the section types option would not copy them: section types are still copied for
    /// courses with more than one component. Null when the option is on, or no course in the source has two or more
    /// components. Recomputed when the From semester or an option changes.
    /// </summary>
    public string? ComponentTypesLine =>
        !(CopyAndMore && IncludeSectionType) && _multiComponentCodes.Count > 0
            ? AccessText.CopyComponentTypesLine(_multiComponentCodes)
            : null;

    /// <summary>
    /// Q40's line, shown while the tags option would not copy them: watched tags are still copied. Null when the
    /// option is on, or no source section carries a watched tag. Recomputed when the From semester or an option changes.
    /// </summary>
    public string? WatchedTagsLine =>
        !(CopyAndMore && IncludeAllTags) && _sourceHasWatchedTags ? AccessText.CopyWatchedTagsLine : null;

    /// <summary>Tells the view to re-read the two lines.</summary>
    private void RaiseAccessLinesChanged()
    {
        OnPropertyChanged(nameof(ComponentTypesLine));
        OnPropertyChanged(nameof(WatchedTagsLine));
    }

    /// <summary>
    /// Works out, over the source semester's sections, which courses have two or more components (the engine's rules,
    /// so before the access setup the suggestion counts) and whether any section carries a watched tag. Without the
    /// access service both stay empty and nothing extra is copied.
    /// </summary>
    /// <param name="source">The source semester's sections.</param>
    private void ComputeSourceFacts(IReadOnlyList<Section> source)
    {
        _multiComponentCourseIds = new();
        _multiComponentCodes = new();
        _watchedTagIds = new();
        _sourceHasWatchedTags = false;
        if (_setup is null || source.Count == 0) return;

        var inputs = _setup.InputsFor(source);
        var courseIds = source.Where(s => s.CourseId is not null).Select(s => s.CourseId!).Distinct()
            .Where(id => inputs.ComponentsOf(id).Count >= 2)
            .OrderBy(id => inputs.CourseCode(id), StringComparer.OrdinalIgnoreCase)
            .ToList();
        _multiComponentCourseIds = courseIds.ToHashSet();
        _multiComponentCodes = courseIds.Select(inputs.CourseCode).ToList();
        _watchedTagIds = _setup.WatchedTagIds().ToHashSet();
        _sourceHasWatchedTags = source.Any(s => s.TagIds.Any(_watchedTagIds.Contains));
    }

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

    /// <summary>
    /// Whether each copied section keeps its delivery type (<see cref="Section.DeliveryTypeId"/>).
    /// Only applies under "Copy and more" (<see cref="CopyAndMore"/>), like the other sub-options.
    /// Ticked by default, unlike the other sub-options, because a section's delivery type rarely changes
    /// from one year to the next. Not persisted between sessions.
    /// </summary>
    [ObservableProperty] private bool _includeDeliveryType = true;
    [ObservableProperty] private bool _includeAllTags;
    [ObservableProperty] private bool _includeAllStaff;
    [ObservableProperty] private bool _includeRoomAssignments;
    [ObservableProperty] private bool _includeMeetingTypeAssignments;
    [ObservableProperty] private bool _includeAllReserves;

    partial void OnCopyDesignationsOnlyChanged(bool value) { if (value) CopyAndMore = false; }
    partial void OnCopyAndMoreChanged(bool value) { if (value) CopyDesignationsOnly = false; RaiseAccessLinesChanged(); }
    partial void OnIncludeSectionTypeChanged(bool value) => RaiseAccessLinesChanged();
    partial void OnIncludeAllTagsChanged(bool value) => RaiseAccessLinesChanged();

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
        WriteLockService lockService,
        AccessSetupService? setupService = null,
        SectionHistory? history = null)
    {
        _history = history;
        _setup = setupService;
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
        ComputeSourceFacts(value is null || _setup is null ? [] : _sectionRepo.GetAll(value.Id));
        RaiseAccessLinesChanged();
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

        ComputeSourceFacts(_sourceSections);
        ExecuteCopy();
    }

    private void ExecuteCopy()
    {
        var count = 0;

        // Use the nullable-tx pattern so WASM demo contexts (SupportsTransactions = false)
        // can complete without throwing NotSupportedException on Connection access.
        // The copied sections are not an undo step and are never undone, but they are unsaved. The pause is declared
        // before the transaction, so it ends after the transaction is disposed, as SectionHistory requires.
        using var pause = _history.PauseOrNothing();
        using var tx = _db.BeginWriteTransaction();
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

        // Access watches still need these two, whatever the options say (rule 36, Q40): the section type of a course
        // with two or more components, and the tags that are access watches.
        bool typesOption = CopyAndMore && IncludeSectionType;
        if (!typesOption && source.CourseId is not null && _multiComponentCourseIds.Contains(source.CourseId))
            s.SectionTypeId = source.SectionTypeId;
        if (!(CopyAndMore && IncludeAllTags))
            s.TagIds = source.TagIds.Where(_watchedTagIds.Contains).ToList();

        if (!CopyAndMore)
            return s;

        if (IncludeSectionType)
            s.SectionTypeId = source.SectionTypeId;

        if (IncludeDeliveryType)
            s.DeliveryTypeId = source.DeliveryTypeId;

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
