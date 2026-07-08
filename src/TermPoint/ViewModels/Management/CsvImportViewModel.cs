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

    /// <summary>Shared log entries visible at the bottom of the flyout, across all imports.</summary>
    public ObservableCollection<string> Log { get; } = new();

    public CsvImportViewModel(
        MainWindowViewModel mainVm,
        IInstructorRepository instructorRepo,
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo,
        IDatabaseContext db,
        CsvImportParser parser,
        CsvImportMatcher matcher)
    {
        InstructorVm = new InstructorImportViewModel(
            mainVm, instructorRepo, db, parser, matcher, AddLog);

        CourseVm = new CourseImportViewModel(
            mainVm, courseRepo, subjectRepo, db, parser, matcher, AddLog);
    }

    private void AddLog(string message)
    {
        var entry = $"{DateTime.Now:HH:mm:ss}  {message}";
        Log.Add(entry);
    }
}
