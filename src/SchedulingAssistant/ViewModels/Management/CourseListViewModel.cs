using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CourseListViewModel : ViewModelBase
{
    private readonly ICourseRepository _courseRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _subjectEditVm;
    [ObservableProperty] private ObservableCollection<Course> _courses = new();
    [ObservableProperty] private Course? _selectedCourse;
    [ObservableProperty] private CourseEditViewModel? _editVm;
    [ObservableProperty] private CourseHistoryViewModel _courseHistoryVm;

    /// <summary>
    /// When true, only active courses are shown in the list.
    /// Mirrors <see cref="AppSettings.ShowOnlyActiveCourses"/> and is persisted on change.
    /// </summary>
    [ObservableProperty] private bool _showOnlyActive = true;

    public CourseListViewModel(
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo,
        ISchedulingEnvironmentRepository propertyRepo,
        IDialogService dialog,
        ISectionRepository sectionRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        IInstructorRepository instructorRepo,
        WriteLockService lockService)
    {
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;
        _propertyRepo = propertyRepo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        CourseHistoryVm = new CourseHistoryViewModel(sectionRepo, semesterRepo, academicYearRepo, instructorRepo);

        ShowOnlyActive = AppSettings.Current.ShowOnlyActiveCourses;

        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        if (Subjects.Count > 0)
            SelectedSubject = Subjects[0];

        LoadCourses();
    }

    public bool HasSubjects => Subjects.Count > 0;

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        AddSubjectCommand.NotifyCanExecuteChanged();
        EditSubjectCommand.NotifyCanExecuteChanged();
        DeleteSubjectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads courses from the database, optionally filtering to active-only based on
    /// <see cref="ShowOnlyActive"/>, and populates the <see cref="Course.TagSummary"/>
    /// display property on each by resolving tag IDs to names.
    /// </summary>
    private void LoadCourses()
    {
        var tagById = _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag)
                                   .ToDictionary(t => t.Id, t => t.Name);

        // Respect the "show only active" toggle: inactive courses are hidden from the
        // list when the checkbox is checked, but can still be revealed for editing.
        var courses = ShowOnlyActive ? _courseRepo.GetAllActive() : _courseRepo.GetAll();
        foreach (var course in courses)
            course.TagSummary = string.Join(", ",
                course.TagIds.Select(id => tagById.TryGetValue(id, out var name) ? name : null)
                             .Where(n => n is not null));

        Courses = new ObservableCollection<Course>(courses);
        SelectedCourse = null;
    }

    /// <summary>
    /// Loads the full list of available tags from the property repository.
    /// Called by Add/Edit to pass current tags to the course editor.
    /// </summary>
    private List<SchedulingEnvironmentValue> LoadAllTags() =>
        _propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var course = new Course();
        EditVm = new CourseEditViewModel(course, isNew: true,
            allTags: LoadAllTags(),
            onSave: async c =>
            {
                try { _courseRepo.Insert(c); LoadCourses(); SelectedCourse = Courses.FirstOrDefault(x => x.Id == c.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Add"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code),
            subjects: Subjects);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedCourse is null) return;
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
            allTags: LoadAllTags(),
            onSave: async c =>
            {
                try { _courseRepo.Update(c); LoadCourses(); SelectedCourse = Courses.FirstOrDefault(x => x.Id == c.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Edit"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code, excludeId: clone.Id),
            subjects: Subjects);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void AddSubject()
    {
        var subject = new Subject();
        SubjectEditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: async s =>
            {
                try { _subjectRepo.Insert(s); LoadSubjects(); SelectedSubject = Subjects.FirstOrDefault(x => x.Id == s.Id); SubjectEditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.AddSubject"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
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
                try { _subjectRepo.Update(s); LoadSubjects(); SelectedSubject = Subjects.FirstOrDefault(x => x.Id == s.Id); SubjectEditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.EditSubject"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name, excludeId: clone.Id),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr, excludeId: clone.Id));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
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

    /// <summary>
    /// Called when <see cref="ShowOnlyActive"/> changes.
    /// Persists the new value to <see cref="AppSettings"/> and reloads the course list.
    /// </summary>
    /// <param name="value">The new checkbox state.</param>
    partial void OnShowOnlyActiveChanged(bool value)
    {
        LoadCourses();
        var settings = AppSettings.Current;
        settings.ShowOnlyActiveCourses = value;
        settings.Save();
    }
}
