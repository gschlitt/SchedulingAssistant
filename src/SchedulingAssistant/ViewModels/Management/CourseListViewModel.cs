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
    }

    partial void OnSelectedSubjectChanged(Subject? value) => LoadCourses();

    private void LoadCourses()
    {
        if (SelectedSubject is null)
        {
            Courses = new ObservableCollection<Course>();
            return;
        }
        Courses = new ObservableCollection<Course>(_courseRepo.GetBySubject(SelectedSubject.Id));
        SelectedCourse = null;
    }

    [RelayCommand]
    private void Add()
    {
        if (SelectedSubject is null) return;
        var course = new Course { SubjectId = SelectedSubject.Id };
        EditVm = new CourseEditViewModel(course, isNew: true,
            onSave: c => { _courseRepo.Insert(c); LoadCourses(); EditVm = null; },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code));
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
            codeExists: code => _courseRepo.ExistsByCalendarCode(code, excludeId: clone.Id));
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
}
