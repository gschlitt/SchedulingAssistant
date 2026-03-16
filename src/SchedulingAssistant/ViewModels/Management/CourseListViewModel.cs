using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CourseListViewModel : ViewModelBase
{
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly SectionPropertyRepository _propRepo;
    private readonly IDialogService _dialog;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _subjectEditVm;
    [ObservableProperty] private ObservableCollection<Course> _courses = new();
    [ObservableProperty] private Course? _selectedCourse;
    [ObservableProperty] private CourseEditViewModel? _editVm;
    [ObservableProperty] private CourseHistoryViewModel _courseHistoryVm;

    public CourseListViewModel(
        CourseRepository courseRepo,
        SubjectRepository subjectRepo,
        SectionPropertyRepository propRepo,
        IDialogService dialog,
        SectionRepository sectionRepo,
        SemesterRepository semesterRepo,
        AcademicYearRepository academicYearRepo,
        InstructorRepository instructorRepo)
    {
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;
        _propRepo = propRepo;
        _dialog = dialog;
        CourseHistoryVm = new CourseHistoryViewModel(sectionRepo, semesterRepo, academicYearRepo, instructorRepo);

        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        if (Subjects.Count > 0)
            SelectedSubject = Subjects[0];

        LoadCourses();
    }

    public bool HasSubjects => Subjects.Count > 0;

    /// <summary>
    /// Reloads the course list from the database and populates each course's
    /// transient TagSummary display string from the current tag property values.
    /// </summary>
    private void LoadCourses()
    {
        var tagNamesById = _propRepo.GetAll(SectionPropertyTypes.Tag)
            .ToDictionary(t => t.Id, t => t.Name);

        var courses = _courseRepo.GetAll();
        foreach (var c in courses)
        {
            c.TagSummary = c.TagIds.Count == 0
                ? string.Empty
                : string.Join(", ", c.TagIds
                    .Select(id => tagNamesById.TryGetValue(id, out var n) ? n : null)
                    .Where(n => n is not null)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)!);
        }

        Courses = new ObservableCollection<Course>(courses);
        SelectedCourse = null;
    }

    [RelayCommand]
    private void Add()
    {
        var allTags = _propRepo.GetAll(SectionPropertyTypes.Tag);
        var course = new Course();
        EditVm = new CourseEditViewModel(course, isNew: true,
            onSave: async c =>
            {
                try { _courseRepo.Insert(c); LoadCourses(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Add"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code),
            subjects: Subjects,
            allTags: allTags);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedCourse is null) return;
        var allTags = _propRepo.GetAll(SectionPropertyTypes.Tag);
        var clone = new Course
        {
            Id = SelectedCourse.Id,
            SubjectId = SelectedCourse.SubjectId,
            CalendarCode = SelectedCourse.CalendarCode,
            Title = SelectedCourse.Title,
            TagIds = new List<string>(SelectedCourse.TagIds),
            IsActive = SelectedCourse.IsActive
        };
        EditVm = new CourseEditViewModel(clone, isNew: false,
            onSave: async c =>
            {
                try { _courseRepo.Update(c); LoadCourses(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Edit"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code, excludeId: clone.Id),
            subjects: Subjects,
            allTags: allTags);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedCourse is null) return;

        if (_courseRepo.HasSections(SelectedCourse.Id))
        {
            await _dialog.ShowError($"Cannot delete \"{SelectedCourse.CalendarCode}\" — it has sections scheduled in one or more semesters.");
            return;
        }

        try
        {
            _courseRepo.Delete(SelectedCourse.Id);
            LoadCourses();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "CourseListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }

    // ── Subject management ──────────────────────────────────────────────────

    [RelayCommand]
    private void AddSubject()
    {
        var subject = new Subject();
        SubjectEditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: async s =>
            {
                try { _subjectRepo.Insert(s); LoadSubjects(); SubjectEditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.AddSubject"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
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
            onSave: async s =>
            {
                try { _subjectRepo.Update(s); LoadSubjects(); SubjectEditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.EditSubject"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
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
            await _dialog.ShowError($"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.");
            return;
        }

        try
        {
            _subjectRepo.Delete(SelectedSubject.Id);
            LoadSubjects();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "CourseListViewModel.DeleteSubject");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
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

    /// <summary>
    /// Handles course selection change: loads course history when a course is selected.
    /// </summary>
    partial void OnSelectedCourseChanged(Course? value)
    {
        if (value?.Id is not null)
            CourseHistoryVm.LoadByCourse(value.Id);
        else
            CourseHistoryVm.Items.Clear();
    }
}
