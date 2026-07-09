using System.Collections.ObjectModel;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.ViewModels;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Top-level flyout ViewModel for the CSV Import facility.
/// Hosts three sub-ViewModels (Instructors, Courses, Sections) and a shared log.
/// </summary>
public partial class CsvImportViewModel : ViewModelBase
{
    /// <summary>Instructor import sub-VM: file selection, preview, matching, import.</summary>
    public InstructorImportViewModel InstructorVm { get; }

    /// <summary>Course import sub-VM: file selection, subject mapping, preview, import.</summary>
    public CourseImportViewModel CourseVm { get; }

    /// <summary>Section import sub-VM: file selection, environment mapping, preview, import.</summary>
    public SectionImportViewModel SectionVm { get; }

    /// <summary>Shared log entries visible at the bottom of the flyout, across all imports.</summary>
    public ObservableCollection<string> Log { get; } = new();

    public CsvImportViewModel(
        MainWindowViewModel mainVm,
        IInstructorRepository instructorRepo,
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo,
        ISectionRepository sectionRepo,
        IRoomRepository roomRepo,
        ICampusRepository campusRepo,
        ISchedulingEnvironmentRepository schedEnvRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        IDatabaseContext db,
        CsvImportParser parser,
        CsvImportMatcher matcher)
    {
        InstructorVm = new InstructorImportViewModel(
            mainVm, instructorRepo, db, parser, matcher, AddLog);

        CourseVm = new CourseImportViewModel(
            mainVm, courseRepo, subjectRepo, db, parser, matcher, AddLog);

        SectionVm = new SectionImportViewModel(
            mainVm, sectionRepo, courseRepo, instructorRepo, roomRepo,
            campusRepo, schedEnvRepo, semesterRepo, academicYearRepo,
            db, parser, matcher, AddLog);
    }

    private void AddLog(string message)
    {
        var entry = $"{DateTime.Now:HH:mm:ss}  {message}";
        Log.Add(entry);
    }
}
