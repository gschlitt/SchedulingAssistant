using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CourseListViewModel : ViewModelBase
{
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _subjectEditVm;
    [ObservableProperty] private ObservableCollection<Course> _courses = new();
    [ObservableProperty] private Course? _selectedCourse;
    [ObservableProperty] private CourseEditViewModel? _editVm;

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public CourseListViewModel(CourseRepository courseRepo, SubjectRepository subjectRepo)
    {
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;

        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        if (Subjects.Count > 0)
            SelectedSubject = Subjects[0];

        LoadCourses();
    }

    public bool HasSubjects => Subjects.Count > 0;

    private void LoadCourses()
    {
        // Load all courses (not filtered by subject anymore)
        Courses = new ObservableCollection<Course>(_courseRepo.GetAll());
        SelectedCourse = null;
    }

    [RelayCommand]
    private void Add()
    {
        var course = new Course();
        EditVm = new CourseEditViewModel(course, isNew: true,
            onSave: c => { _courseRepo.Insert(c); LoadCourses(); EditVm = null; },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code),
            subjects: Subjects);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedCourse is null) return;
        var clone = new Course
        {
            Id = SelectedCourse.Id,
            SubjectId = SelectedCourse.SubjectId,
            CalendarCode = SelectedCourse.CalendarCode,
            Title = SelectedCourse.Title,
            Tags = new List<string>(SelectedCourse.Tags),
            IsActive = SelectedCourse.IsActive
        };
        EditVm = new CourseEditViewModel(clone, isNew: false,
            onSave: c => { _courseRepo.Update(c); LoadCourses(); EditVm = null; },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code, excludeId: clone.Id),
            subjects: Subjects);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedCourse is null) return;

        if (_courseRepo.HasSections(SelectedCourse.Id))
        {
            if (ShowError is not null)
                await ShowError($"Cannot delete \"{SelectedCourse.CalendarCode}\" — it has sections scheduled in one or more semesters.");
            return;
        }

        _courseRepo.Delete(SelectedCourse.Id);
        LoadCourses();
    }

    // ── Subject management ──────────────────────────────────────────────────

    [RelayCommand]
    private void AddSubject()
    {
        var subject = new Subject();
        SubjectEditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: s => { _subjectRepo.Insert(s); LoadSubjects(); SubjectEditVm = null; },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr));
    }

    [RelayCommand]
    private void EditSubject()
    {
        if (SelectedSubject is null) return;
        var clone = new Subject
        {
            Id = SelectedSubject.Id,
            Name = SelectedSubject.Name,
            CalendarAbbreviation = SelectedSubject.CalendarAbbreviation
        };
        SubjectEditVm = new SubjectEditViewModel(clone, isNew: false,
            onSave: s => { _subjectRepo.Update(s); LoadSubjects(); SubjectEditVm = null; },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name, excludeId: clone.Id),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr, excludeId: clone.Id));
    }

    [RelayCommand]
    private async Task DeleteSubject()
    {
        if (SelectedSubject is null) return;

        if (_subjectRepo.HasCourses(SelectedSubject.Id))
        {
            if (ShowError is not null)
                await ShowError($"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.");
            return;
        }

        _subjectRepo.Delete(SelectedSubject.Id);
        LoadSubjects();
    }

    private void LoadSubjects()
    {
        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        if (Subjects.Count > 0)
            SelectedSubject = Subjects[0];
        else
            SelectedSubject = null;
        LoadCourses();
    }
}
