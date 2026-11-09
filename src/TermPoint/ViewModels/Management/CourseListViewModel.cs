using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.Management;

public partial class CourseListViewModel : ViewModelBase, IDismissableEditor, IUnappliedChangesHost, IDisposable
{
    private readonly ICourseRepository _courseRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;
    private readonly SectionStore _sectionStore;
    private readonly SubjectColorStore _colorStore;

    /// <summary>
    /// The subject colour presenter the Section list and Schedule view read. Told to drop its caches after a subject's
    /// abbreviation is renamed, which changes no section and so reloads nothing on its own.
    /// </summary>
    private readonly SubjectColorPresenter _subjectColors;

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

    /// <inheritdoc/>
    public IUnappliedChanges? FindUnappliedChanges() => UnappliedChangesFinder.First(EditVm, SubjectEditVm);

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (SubjectEditVm is not null) { SubjectEditVm.CancelCommand.Execute(null); return true; }
        if (EditVm is not null) { EditVm.CancelCommand.Execute(null); return true; }
        return false;
    }

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
        WriteLockService lockService,
        SectionStore sectionStore,
        SubjectColorStore colorStore,
        SubjectColorPresenter subjectColors)
    {
        _courseRepo = courseRepo;
        _sectionRepo = sectionRepo;
        _subjectRepo = subjectRepo;
        _propertyRepo = propertyRepo;
        _dialog = dialog;
        _lockService = lockService;
        _sectionStore = sectionStore;
        _colorStore = colorStore;
        _subjectColors = subjectColors;
        _colorStore.Changed += OnColorStoreChanged;
        _lockService.LockStateChanged += OnLockStateChanged;
        CourseHistoryVm = new CourseHistoryViewModel(sectionRepo, semesterRepo, academicYearRepo, instructorRepo);

        ShowOnlyActive = AppSettings.Current.ShowOnlyActiveCourses;

        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        RebuildSubjectRows();
        if (Subjects.Count > 0)
            SelectedSubject = Subjects[0];

        LoadCourses();
    }

    // ── Subject colours (personal settings, never the database: none of this checks the write lock) ──

    /// <summary>
    /// One row per entry of <see cref="Subjects"/>, in the same order, for the Subjects grid (name, abbreviation
    /// and colour swatch). <see cref="Subjects"/> stays the source other code reads (tour, section editor).
    /// </summary>
    [ObservableProperty] private ObservableCollection<SubjectRowViewModel> _subjectRows = new();

    /// <summary>The grid's selected row; kept in step with <see cref="SelectedSubject"/> in both directions.</summary>
    [ObservableProperty] private SubjectRowViewModel? _selectedSubjectRow;

    /// <summary>The three ways colours can be shown, with the words the user sees.</summary>
    public IReadOnlyList<SubjectColorDisplayOption> DisplayOptions { get; } = new SubjectColorDisplayOption[]
    {
        new(SubjectColorDisplay.Always, "Always"),
        new(SubjectColorDisplay.OnlyWhenSeveral, "Only when more than one subject is shown"),
        new(SubjectColorDisplay.Never, "Never"),
    };

    /// <summary>How colours are shown; reads and writes the store (which saves and raises its change events).</summary>
    public SubjectColorDisplay SelectedDisplay
    {
        get => _colorStore.Display;
        set => _colorStore.Display = value;
    }

    /// <summary>The <see cref="DisplayOptions"/> entry for <see cref="SelectedDisplay"/>, for the ComboBox.</summary>
    public SubjectColorDisplayOption SelectedDisplayOption
    {
        get => DisplayOptions.First(o => o.Value == SelectedDisplay);
        set { if (value is not null) SelectedDisplay = value.Value; }
    }

    /// <summary>True when at least one subject of this database has a colour (shows the display choice; enables Clear).</summary>
    public bool HasAnyColour => _colorStore.HasAnyColor(SubjectAbbreviations());

    /// <summary>True when some subject with an abbreviation has no colour yet (enables Suggest).</summary>
    public bool CanSuggest => Subjects.Any(s => !string.IsNullOrWhiteSpace(s.CalendarAbbreviation)
                                                && _colorStore.GetColor(s.CalendarAbbreviation) is null);

    /// <summary>True when two subjects of this database have the same colour (normalised); shows a note.</summary>
    public bool HasSharedColours => Subjects
        .Select(s => _colorStore.GetColor(s.CalendarAbbreviation))
        .Where(c => c is not null)
        .GroupBy(c => c!, StringComparer.OrdinalIgnoreCase)
        .Any(g => g.Count() > 1);

    /// <summary>The abbreviations of this database's subjects (blank ones included; the store skips them).</summary>
    private List<string> SubjectAbbreviations() => Subjects.Select(s => s.CalendarAbbreviation).ToList();

    /// <summary>
    /// Rebuilds <see cref="SubjectRows"/> from <see cref="Subjects"/>. The selected row is re-pointed without touching
    /// <see cref="SelectedSubject"/>; the caller then usually sets <see cref="SelectedSubject"/>, which selects the
    /// matching new row.
    /// </summary>
    private void RebuildSubjectRows()
    {
        _syncingSubjectSelection = true;
        try
        {
            SubjectRows = new ObservableCollection<SubjectRowViewModel>(
                Subjects.Select(s => new SubjectRowViewModel(s, _colorStore)));
            SelectedSubjectRow = SubjectRows.FirstOrDefault(r => r.Subject == SelectedSubject);
        }
        finally { _syncingSubjectSelection = false; }
        RaiseColourStateChanged();
    }

    /// <summary>True while one of the two selection properties is being set from the other (breaks the loop).</summary>
    private bool _syncingSubjectSelection;

    /// <summary>Selecting a subject elsewhere (code, tour) selects its grid row.</summary>
    /// <param name="value">The newly selected subject, or null.</param>
    partial void OnSelectedSubjectChanged(Subject? value)
    {
        if (_syncingSubjectSelection) return;
        _syncingSubjectSelection = true;
        try { SelectedSubjectRow = SubjectRows.FirstOrDefault(r => r.Subject == value); }
        finally { _syncingSubjectSelection = false; }
    }

    /// <summary>Selecting a grid row selects its subject.</summary>
    /// <param name="value">The newly selected row, or null.</param>
    partial void OnSelectedSubjectRowChanged(SubjectRowViewModel? value)
    {
        if (_syncingSubjectSelection) return;
        _syncingSubjectSelection = true;
        try { SelectedSubject = value?.Subject; }
        finally { _syncingSubjectSelection = false; }
    }

    /// <summary>The store changed (here, or on another screen): re-read every row and the colour state.</summary>
    private void OnColorStoreChanged()
    {
        foreach (var row in SubjectRows) row.RefreshFromStore();
        RaiseColourStateChanged();
    }

    /// <summary>Tells the view to re-read the colour properties and re-evaluates the two colour commands.</summary>
    private void RaiseColourStateChanged()
    {
        OnPropertyChanged(nameof(HasAnyColour));
        OnPropertyChanged(nameof(CanSuggest));
        OnPropertyChanged(nameof(HasSharedColours));
        OnPropertyChanged(nameof(SelectedDisplay));
        OnPropertyChanged(nameof(SelectedDisplayOption));
        SuggestColoursCommand.NotifyCanExecuteChanged();
        ClearColoursCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Gives every subject of this database that has no colour a palette colour; existing colours stay.</summary>
    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private void SuggestColours() => _colorStore.ApplySuggestions(SubjectAbbreviations());

    /// <summary>
    /// Asks, then removes the colours of this database's subjects (other databases' abbreviations keep theirs).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasAnyColour))]
    private async Task ClearColoursAsync()
    {
        var n = Subjects.Count(s => _colorStore.GetColor(s.CalendarAbbreviation) is not null);
        if (n == 0) return;
        var message = n == 1
            ? "Remove the colour of 1 subject in this database? You can't undo this."
            : $"Remove the colours of all {n} subjects in this database? You can't undo this.";
        if (await _dialog.Confirm(message, "Remove colours"))
            _colorStore.ClearColors(SubjectAbbreviations());
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

    /// <summary>
    /// Overwrites the tag list on every section of the given course (all semesters)
    /// with the specified tag IDs. Refreshes the shared section cache so the section
    /// list and grid reflect the changes immediately. Returns the number of sections updated.
    /// </summary>
    private int ApplyTagsToAllSections(string courseId, List<string> tagIds)
    {
        var sections = _sectionRepo.GetByCourseId(courseId);
        foreach (var section in sections)
        {
            section.TagIds = new List<string>(tagIds);
            _sectionRepo.Update(section);
        }

        if (sections.Count > 0)
            _sectionStore.Reload(_sectionRepo, _sectionStore.SectionsBySemester.Keys);

        return sections.Count;
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        LastErrorMessage = null;
        var course = new Course();
        EditVm = new CourseEditViewModel(course, isNew: true,
            allTags: LoadAllTags(),
            onSave: async c =>
            {
                try { _courseRepo.Insert(c); LoadCourses(); SelectedCourse = Courses.FirstOrDefault(x => x.Id == c.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Add"); LastErrorMessage = WriteFailureText.For(ex, "The save could not be completed. Please try again."); }
                await Task.CompletedTask;
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code),
            subjects: Subjects);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        LastErrorMessage = null;
        if (SelectedCourse is null) return;
        // Work on a copy so Cancel leaves the list's own course untouched. Save writes the WHOLE
        // copy back, so every persisted field must be carried over here: Level and Capacity seed
        // the editor's Level and Capacity controls (a clone that omitted them reset both to
        // blank/auto on every Apply), and ExtensionData keeps unknown JSON fields written by a
        // newer app version (spec item 3).
        var clone = new Course
        {
            Id = SelectedCourse.Id,
            SubjectId = SelectedCourse.SubjectId,
            CalendarCode = SelectedCourse.CalendarCode,
            Title = SelectedCourse.Title,
            TagIds = new List<string>(SelectedCourse.TagIds),
            IsActive = SelectedCourse.IsActive,
            Level = SelectedCourse.Level,
            Capacity = SelectedCourse.Capacity,
        }.WithExtensionDataFrom(SelectedCourse);
        EditVm = new CourseEditViewModel(clone, isNew: false,
            allTags: LoadAllTags(),
            onSave: async c =>
            {
                try { _courseRepo.Update(c); LoadCourses(); SelectedCourse = Courses.FirstOrDefault(x => x.Id == c.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.Edit"); LastErrorMessage = WriteFailureText.For(ex, "The save could not be completed. Please try again."); }
                await Task.CompletedTask;
            },
            onCancel: () => EditVm = null,
            codeExists: code => _courseRepo.ExistsByCalendarCode(code, excludeId: clone.Id),
            subjects: Subjects,
            applyTagsToAll: tagIds => Task.FromResult(ApplyTagsToAllSections(clone.Id, tagIds)),
            // Lock subject + course number once sections exist — same rule that forbids deletion.
            identityLocked: _courseRepo.HasSections(SelectedCourse.Id));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Delete()
    {
        if (SelectedCourse is null) return;

        LastErrorMessage = null;

        if (_courseRepo.HasSections(SelectedCourse.Id))
        {
            LastErrorMessage = $"Cannot delete \"{SelectedCourse.CalendarCode}\" — it has sections scheduled in one or more semesters.";
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
            LastErrorMessage = WriteFailureText.For(ex, "The delete could not be completed. Please try again.");
        }
    }

    // ── Subject management ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void AddSubject()
    {
        LastErrorMessage = null;
        var subject = new Subject();
        SubjectEditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: async s =>
            {
                try { _subjectRepo.Insert(s); LoadSubjects(); SelectedSubject = Subjects.FirstOrDefault(x => x.Id == s.Id); SubjectEditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.AddSubject"); LastErrorMessage = WriteFailureText.For(ex, "The save could not be completed. Please try again."); }
                await Task.CompletedTask;
            },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void EditSubject()
    {
        LastErrorMessage = null;
        if (SelectedSubject is null) return;
        var originalAbbreviation = SelectedSubject.CalendarAbbreviation;
        // Save writes the whole copy back, so unknown JSON fields (spec item 3) are carried over too.
        var clone = new Subject
        {
            Id = SelectedSubject.Id,
            Name = SelectedSubject.Name,
            CalendarAbbreviation = SelectedSubject.CalendarAbbreviation
        }.WithExtensionDataFrom(SelectedSubject);
        SubjectEditVm = new SubjectEditViewModel(clone, isNew: false,
            onSave: async s =>
            {
                try
                {
                    _subjectRepo.Update(s);
                    var abbreviationChanged = !string.Equals(originalAbbreviation?.Trim(), s.CalendarAbbreviation?.Trim(),
                        StringComparison.OrdinalIgnoreCase);
                    // A renamed abbreviation keeps its colour: copy it before the rows are rebuilt.
                    if (abbreviationChanged
                        && !string.IsNullOrWhiteSpace(originalAbbreviation) && !string.IsNullOrWhiteSpace(s.CalendarAbbreviation))
                        _colorStore.CopyColor(originalAbbreviation, s.CalendarAbbreviation);
                    // The presenter maps courses to abbreviations, and a rename reloads no sections, so its map is
                    // stale (whether or not a colour was copied): drop it so markers in both views follow the rename now.
                    if (abbreviationChanged) _subjectColors.Invalidate();
                    LoadSubjects();
                    SelectedSubject = Subjects.FirstOrDefault(x => x.Id == s.Id);
                    SubjectEditVm = null;
                }
                catch (Exception ex) { App.Logger.LogError(ex, "CourseListViewModel.EditSubject"); LastErrorMessage = WriteFailureText.For(ex, "The save could not be completed. Please try again."); }
                await Task.CompletedTask;
            },
            onCancel: () => SubjectEditVm = null,
            nameExists: name => _subjectRepo.ExistsByName(name, excludeId: clone.Id),
            abbreviationExists: abbr => _subjectRepo.ExistsByAbbreviation(abbr, excludeId: clone.Id));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void DeleteSubject()
    {
        if (SelectedSubject is null) return;

        LastErrorMessage = null;

        if (_subjectRepo.HasCourses(SelectedSubject.Id))
        {
            LastErrorMessage = $"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.";
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
            LastErrorMessage = WriteFailureText.For(ex, "The delete could not be completed. Please try again.");
        }
    }

    private void LoadSubjects()
    {
        Subjects = new ObservableCollection<Subject>(_subjectRepo.GetAll());
        RebuildSubjectRows();
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

    /// <inheritdoc/>
    public void Dispose()
    {
        _lockService.LockStateChanged -= OnLockStateChanged;
        _colorStore.Changed -= OnColorStoreChanged;
    }
}

/// <summary>One choice in the "Show subject colours" list: the setting value and the words the user sees.</summary>
/// <param name="Value">The setting this choice stands for.</param>
/// <param name="Label">The text shown in the list.</param>
public sealed record SubjectColorDisplayOption(SubjectColorDisplay Value, string Label)
{
    /// <summary>The label, so a ComboBox with no item template shows the words.</summary>
    public override string ToString() => Label;
}
