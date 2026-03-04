using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SchedulingAssistant.ViewModels.Management;

public enum CopyState { Ready, FlaggedWarning, Complete }

public partial class CopySemesterViewModel : ViewModelBase
{
    private readonly AcademicYearRepository _ayRepo;
    private readonly SemesterRepository _semRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly DatabaseContext _db;
    private readonly ScheduleValidationService _scheduleValidation;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;

    // Cached between Copy() and ContinueCopy()
    private List<Section>? _sourceSections;
    private List<(Section section, List<SectionDaySchedule> badMeetings)>? _flaggedSections;

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
    [NotifyPropertyChangedFor(nameof(IsFlaggedWarning))]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    private CopyState _state = CopyState.Ready;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _writeCsvReport = true;
    [ObservableProperty] private ObservableCollection<string> _flaggedDescriptions = new();

    public bool IsReady => State == CopyState.Ready;
    public bool IsFlaggedWarning => State == CopyState.FlaggedWarning;
    public bool IsComplete => State == CopyState.Complete;

    public CopySemesterViewModel(
        AcademicYearRepository ayRepo,
        SemesterRepository semRepo,
        SectionRepository sectionRepo,
        DatabaseContext db,
        ScheduleValidationService scheduleValidation,
        CourseRepository courseRepo,
        SubjectRepository subjectRepo)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _db = db;
        _scheduleValidation = scheduleValidation;
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        FromAcademicYear = AcademicYears.FirstOrDefault();
        ToAcademicYear = AcademicYears.FirstOrDefault();
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

    [RelayCommand]
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

        // Schedule compatibility check (only when copying meeting times)
        if (CopyAndMore && IncludeAllMeetingTimes)
        {
            _flaggedSections = _scheduleValidation.FindIncompatibleSections(
                _sourceSections, ToAcademicYear!.Id);

            if (_flaggedSections.Count > 0)
            {
                BuildFlaggedDescriptions();
                State = CopyState.FlaggedWarning;
                return;
            }
        }

        // No flags — proceed
        ExecuteCopy(new HashSet<string>());
    }

    [RelayCommand]
    private void AbortCopy()
    {
        State = CopyState.Ready;
        StatusMessage = null;
        _flaggedSections = null;
        _sourceSections = null;
        FlaggedDescriptions = new ObservableCollection<string>();
    }

    [RelayCommand]
    private void ContinueCopy()
    {
        if (WriteCsvReport)
            WriteFlaggedCsv();

        var flaggedIds = new HashSet<string>(_flaggedSections!.Select(f => f.section.Id));
        ExecuteCopy(flaggedIds);
    }

    private void ExecuteCopy(HashSet<string> flaggedIds)
    {
        var count = 0;

        using var tx = _db.Connection.BeginTransaction();
        try
        {
            foreach (var source in _sourceSections!)
            {
                var isFlagged = flaggedIds.Contains(source.Id);
                var newSection = BuildNewSection(source, isFlagged);
                _sectionRepo.Insert(newSection, tx);
                count++;
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        var msg = $"Copied {count} section(s) to {ToSemester!.Name}.";
        if (flaggedIds.Count > 0)
            msg += $" {flaggedIds.Count} section(s) copied without schedules.";

        StatusMessage = msg;
        State = CopyState.Complete;
    }

    private Section BuildNewSection(Section source, bool stripSchedule)
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

        if (IncludeAllMeetingTimes && !stripSchedule)
            s.Schedule = source.Schedule
                .Select(m => new SectionDaySchedule
                {
                    Day = m.Day,
                    StartMinutes = m.StartMinutes,
                    DurationMinutes = m.DurationMinutes,
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

    private void BuildFlaggedDescriptions()
    {
        var courses = new Dictionary<string, Course>();
        var subjects = new Dictionary<string, Subject>();
        var descriptions = new List<string>();

        foreach (var (section, badMeetings) in _flaggedSections!)
        {
            var courseLabel = "?";
            if (section.CourseId is not null)
            {
                if (!courses.TryGetValue(section.CourseId, out var course))
                {
                    course = _courseRepo.GetById(section.CourseId);
                    if (course is not null) courses[section.CourseId] = course;
                }
                if (course is not null)
                {
                    if (!subjects.TryGetValue(course.SubjectId, out var subject))
                    {
                        subject = _subjectRepo.GetById(course.SubjectId);
                        if (subject is not null) subjects[course.SubjectId] = subject;
                    }
                    var subjectAbbrev = subject?.CalendarAbbreviation ?? "?";
                    courseLabel = $"{subjectAbbrev} {course.CalendarCode}";
                }
            }

            descriptions.Add(
                $"{courseLabel} {section.SectionCode} — {badMeetings.Count} meeting(s) incompatible");
        }

        FlaggedDescriptions = new ObservableCollection<string>(descriptions);
    }

    private void WriteFlaggedCsv()
    {
        var dbPath = AppSettings.Load().DatabasePath;
        if (string.IsNullOrEmpty(dbPath)) return;

        var dir = Path.GetDirectoryName(dbPath);
        if (string.IsNullOrEmpty(dir)) return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fromName = SanitizeFileName(FromSemester?.Name ?? "source");
        var toName = SanitizeFileName(ToSemester?.Name ?? "target");
        var fileName = $"CopySemester_Flagged_{fromName}_to_{toName}_{timestamp}.csv";
        var filePath = Path.Combine(dir, fileName);

        var courses = new Dictionary<string, Course>();
        var subjects = new Dictionary<string, Subject>();
        var dayNames = new[] { "", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        var sb = new StringBuilder();
        sb.AppendLine("Subject,Course,Section Code,Day,Start Time,Duration (min),Issue");

        foreach (var (section, badMeetings) in _flaggedSections!)
        {
            var subjectAbbrev = "?";
            var calendarCode = "?";

            if (section.CourseId is not null)
            {
                if (!courses.TryGetValue(section.CourseId, out var course))
                {
                    course = _courseRepo.GetById(section.CourseId);
                    if (course is not null) courses[section.CourseId] = course;
                }
                if (course is not null)
                {
                    calendarCode = course.CalendarCode;
                    if (!subjects.TryGetValue(course.SubjectId, out var subject))
                    {
                        subject = _subjectRepo.GetById(course.SubjectId);
                        if (subject is not null) subjects[course.SubjectId] = subject;
                    }
                    subjectAbbrev = subject?.CalendarAbbreviation ?? "?";
                }
            }

            foreach (var meeting in badMeetings)
            {
                var day = meeting.Day >= 1 && meeting.Day < dayNames.Length
                    ? dayNames[meeting.Day] : meeting.Day.ToString();
                var startTime = FormatMinutes(meeting.StartMinutes);
                var issue = "Start time not valid for block length";

                sb.AppendLine($"{CsvEscape(subjectAbbrev)},{CsvEscape(calendarCode)},{CsvEscape(section.SectionCode)},{day},{startTime},{meeting.DurationMinutes},{CsvEscape(issue)}");
            }
        }

        File.WriteAllText(filePath, sb.ToString());
        StatusMessage = $"Flagged sections report written to {fileName}";
    }

    private static string FormatMinutes(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        var ampm = h >= 12 ? "PM" : "AM";
        var h12 = h > 12 ? h - 12 : (h == 0 ? 12 : h);
        return $"{h12}:{m:D2} {ampm}";
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
    }

    [RelayCommand]
    private void Done() => NavigateBackToAcademicYears();

    [RelayCommand]
    private void Cancel() => NavigateBackToAcademicYears();

    private void NavigateBackToAcademicYears()
    {
        var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.NavigateToAcademicYearsCommand.Execute(null);
    }
}
