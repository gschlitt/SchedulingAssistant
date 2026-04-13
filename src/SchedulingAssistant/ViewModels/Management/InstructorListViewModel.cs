using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class InstructorListViewModel : ViewModelBase, IDisposable
{
    private readonly IInstructorRepository _repo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IReleaseRepository _releaseRepo;
    private readonly IInstructorCommitmentRepository _commitmentRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IAcademicYearRepository _academicYearRepo;
    private readonly SemesterContext _semesterContext;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<Instructor> _instructors = new();
    [ObservableProperty] private Instructor? _selectedInstructor;
    [ObservableProperty] private InstructorEditViewModel? _editVm;
    [ObservableProperty] private bool _showOnlyActive = true;
    [ObservableProperty] private InstructorWorkloadViewModel _workloadVm = new();
    [ObservableProperty] private ReleaseManagementViewModel _releaseVm;
    [ObservableProperty] private CommitmentsManagementViewModel _commitmentsVm;
    [ObservableProperty] private WorkloadHistoryViewModel _workloadHistoryVm;

    /// <summary>
    /// All semesters in the currently selected academic year, available in the flyout's
    /// local semester picker. This list is independent of the global semester selection.
    /// </summary>
    [ObservableProperty] private ObservableCollection<SemesterDisplay> _flyoutSemesters = new();

    /// <summary>
    /// The semester currently chosen in the flyout's local picker.
    /// Drives all workload, release, and commitment data shown in the flyout,
    /// independently of the global multi-semester selection.
    /// </summary>
    [ObservableProperty] private SemesterDisplay? _flyoutSelectedSemester;

    private void OnWorkloadVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstructorWorkloadViewModel.TotalWorkload))
            OnPropertyChanged(nameof(WorkloadUnitsDisplay));
    }

    /// <summary>
    /// Called when the user picks a different semester in the flyout's local semester chooser.
    /// Refreshes all workload, release, and commitment data for the newly selected semester,
    /// and updates the semester activity label.
    /// </summary>
    partial void OnFlyoutSelectedSemesterChanged(SemesterDisplay? value)
    {
        OnPropertyChanged(nameof(SemesterActivityLabel));
        RefreshWorkload();
    }

    public InstructorListViewModel(
        IInstructorRepository repo,
        ISchedulingEnvironmentRepository propertyRepo,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IReleaseRepository releaseRepo,
        IInstructorCommitmentRepository commitmentRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        SemesterContext semesterContext,
        SectionChangeNotifier changeNotifier,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _repo = repo;
        _propertyRepo = propertyRepo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _releaseRepo = releaseRepo;
        _commitmentRepo = commitmentRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _semesterContext = semesterContext;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        _releaseVm = new ReleaseManagementViewModel(releaseRepo, lockService);
        _commitmentsVm = new CommitmentsManagementViewModel(commitmentRepo, changeNotifier, lockService);
        _workloadHistoryVm = new WorkloadHistoryViewModel(sectionRepo, courseRepo, semesterRepo, academicYearRepo, releaseRepo);
        ShowOnlyActive = AppSettings.Current.ShowOnlyActiveInstructors;
        RebuildFlyoutSemesters();
        Load();

        _semesterContext.PropertyChanged += OnSemesterContextPropertyChanged;
        _releaseVm.ReleasesChanged += RefreshWorkload;
        _workloadVm.PropertyChanged += OnWorkloadVmPropertyChanged;
    }

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
    }

    private void OnSemesterContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.FilteredSemesters))
        {
            // The academic year changed globally — rebuild the flyout's semester list and
            // reset the local selection so it doesn't point to a semester from the old year.
            RebuildFlyoutSemesters(resetSelection: true);
        }
    }

    public void Dispose()
    {
        _semesterContext.PropertyChanged -= OnSemesterContextPropertyChanged;
        ReleaseVm.ReleasesChanged -= RefreshWorkload;
        _workloadVm.PropertyChanged -= OnWorkloadVmPropertyChanged;
    }

    private void Load()
    {
        var staffTypes = _propertyRepo.GetAll(SchedulingEnvironmentTypes.StaffType)
            .ToDictionary(s => s.Id, s => s.Name);

        var all = _repo.GetAll();
        foreach (var instructor in all)
            instructor.StaffTypeName = instructor.StaffTypeId is not null
                ? staffTypes.GetValueOrDefault(instructor.StaffTypeId)
                : null;

        var filtered = ShowOnlyActive ? all.Where(i => i.IsActive).ToList() : all;

        // StaffType sort must be applied in memory here because StaffTypeName is a
        // display-only property resolved above — it is not stored in the database and
        // therefore cannot be used in an ORDER BY clause.  All other sort modes are
        // handled at the SQL level in InstructorRepository.GetAll().
        if (AppSettings.Current.InstructorSortMode == InstructorSortMode.StaffType)
            filtered = filtered
                .OrderBy(i => i.StaffTypeName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.LastName,  StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.FirstName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        Instructors = new ObservableCollection<Instructor>(filtered);
    }

    /// <summary>
    /// Persists <paramref name="mode"/> to <see cref="AppSettings"/> and reloads the
    /// instructor list so the new order takes effect immediately.  Because
    /// <see cref="InstructorRepository.GetAll"/> reads the setting at query time, all
    /// other instructor loads (section-editor picker, grid-filter list) will also reflect
    /// the new order the next time they reload.
    /// </summary>
    /// <param name="mode">The sort mode chosen by the user.</param>
    public void SetSortMode(InstructorSortMode mode)
    {
        var s = AppSettings.Current;
        s.InstructorSortMode = mode;
        s.Save();
        Load();
    }

    partial void OnShowOnlyActiveChanged(bool value)
    {
        Load();
        var settings = AppSettings.Current;
        settings.ShowOnlyActiveInstructors = value;
        settings.Save();
    }

    // Avalonia evaluates bindings even on hidden elements, so binding directly to
    // SelectedInstructor.FirstName logs a null-traversal error when no instructor is selected.
    // These properties return empty string instead of null, avoiding the spurious binding error.
    public string SelectedFirstName => SelectedInstructor?.FirstName ?? string.Empty;
    public string SelectedLastName => SelectedInstructor?.LastName ?? string.Empty;

    public string WorkloadUnitsDisplay => $"Workload units {WorkloadVm.TotalWorkload:0.##}";

    /// <summary>
    /// Formats the semester activity label to include the currently selected semester name.
    /// Example: "Fall 2025 Semester Activity"
    /// </summary>
    public string SemesterActivityLabel => $"{FlyoutSelectedSemester?.Semester.Name ?? ""} Semester Activity";

    partial void OnSelectedInstructorChanged(Instructor? value)
    {
        OnPropertyChanged(nameof(SelectedFirstName));
        OnPropertyChanged(nameof(SelectedLastName));
        // Reset the flyout's local semester to the current global default when a new
        // instructor is selected, then refresh.  SetFlyoutSemesterToDefault may trigger
        // RefreshWorkload via OnFlyoutSelectedSemesterChanged if the semester changes;
        // we call RefreshWorkload unconditionally so it also fires when the semester is
        // the same but the instructor changed.
        SetFlyoutSemesterToDefault();
        RefreshWorkload();
    }

    /// <summary>
    /// Rebuilds <see cref="FlyoutSemesters"/> from the semesters in the currently selected
    /// academic year.  If the existing <see cref="FlyoutSelectedSemester"/> is no longer
    /// in the list (e.g. after an academic-year change), or <paramref name="resetSelection"/>
    /// is true, the selection is reset to the global default via <see cref="SetFlyoutSemesterToDefault"/>.
    /// </summary>
    /// <param name="resetSelection">
    /// When true, resets the selected semester even if the current selection is still valid.
    /// Pass true when the academic year has changed.
    /// </param>
    private void RebuildFlyoutSemesters(bool resetSelection = false)
    {
        var semesters = _semesterContext.FilteredSemesters.ToList();
        FlyoutSemesters = new ObservableCollection<SemesterDisplay>(semesters);

        bool selectionStillValid = !resetSelection
            && FlyoutSelectedSemester != null
            && semesters.Any(s => s.Semester.Id == FlyoutSelectedSemester.Semester.Id);

        if (!selectionStillValid)
            SetFlyoutSemesterToDefault();
    }

    /// <summary>
    /// Sets <see cref="FlyoutSelectedSemester"/> to the globally-selected primary semester
    /// (which is the first selected semester in both single- and multi-semester modes),
    /// falling back to the first item in <see cref="FlyoutSemesters"/> if no match is found.
    /// </summary>
    private void SetFlyoutSemesterToDefault()
    {
        var defaultId = _semesterContext.SelectedSemesterDisplay?.Semester.Id;
        FlyoutSelectedSemester = FlyoutSemesters.FirstOrDefault(s => s.Semester.Id == defaultId)
            ?? FlyoutSemesters.FirstOrDefault();
    }

    private void RefreshWorkload()
    {
        if (SelectedInstructor is null || FlyoutSelectedSemester is null)
        {
            WorkloadVm.Clear();
            ReleaseVm.SetContext(string.Empty, string.Empty);
            CommitmentsVm.SetContext(string.Empty, string.Empty);
            WorkloadHistoryVm.Clear();
            return;
        }

        var semesterId = FlyoutSelectedSemester.Semester.Id;
        var instructorId = SelectedInstructor.Id;

        var allSections = _sectionRepo.GetAll(semesterId);
        var assignedSections = new List<AssignedSectionWorkload>();

        foreach (var section in allSections)
        {
            var assignment = section.InstructorAssignments.FirstOrDefault(a => a.InstructorId == instructorId);
            if (assignment is not null)
            {
                var course = section.CourseId is not null ? _courseRepo.GetById(section.CourseId) : null;
                var courseCode = course?.CalendarCode ?? "(unknown)";
                assignedSections.Add(new AssignedSectionWorkload
                {
                    CourseCode = courseCode,
                    SectionCode = section.SectionCode,
                    WorkloadValue = assignment.Workload ?? 0m
                });
            }
        }

        var dbReleases = _releaseRepo.GetByInstructor(semesterId, instructorId);
        var releaseWorkloads = dbReleases
            .Select(r => new ReleaseWorkload { Id = r.Id, Title = r.Title, WorkloadValue = r.WorkloadValue })
            .ToList();

        WorkloadVm.LoadWorkload(assignedSections, releaseWorkloads);
        ReleaseVm.SetContext(instructorId, semesterId);
        CommitmentsVm.SetContext(instructorId, semesterId);
        WorkloadHistoryVm.LoadHistory(instructorId);
    }

    private IReadOnlyList<SchedulingEnvironmentValue> GetStaffTypes() =>
        _propertyRepo.GetAll(SchedulingEnvironmentTypes.StaffType);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var instructor = new Instructor();
        EditVm = new InstructorEditViewModel(instructor, isNew: true,
            staffTypes: GetStaffTypes(),
            onSave: i =>
            {
                try { _repo.Insert(i); Load(); SelectedInstructor = Instructors.FirstOrDefault(x => x.Id == i.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "InstructorListViewModel.Add"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            initialsExist: initials => _repo.ExistsByInitials(initials));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedInstructor is null) return;
        var s = SelectedInstructor;
        var copy = new Instructor
        {
            Id = s.Id,
            FirstName = s.FirstName,
            LastName = s.LastName,
            Initials = s.Initials,
            Email = s.Email,
            Notes = s.Notes,
            IsActive = s.IsActive,
            StaffTypeId = s.StaffTypeId,
        };
        EditVm = new InstructorEditViewModel(copy, isNew: false,
            staffTypes: GetStaffTypes(),
            onSave: i =>
            {
                try { _repo.Update(i); Load(); SelectedInstructor = Instructors.FirstOrDefault(x => x.Id == i.Id); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "InstructorListViewModel.Edit"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            initialsExist: initials => _repo.ExistsByInitials(initials, excludeId: copy.Id));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedInstructor is null) return;

        if (_repo.HasSections(SelectedInstructor.Id))
        {
            await _dialog.ShowError("The selected instructor has assigned workload and cannot be deleted. Consider deactivating the instructor instead.");
            return;
        }

        if (!await _dialog.Confirm($"Delete {SelectedInstructor.FirstName} {SelectedInstructor.LastName}?"))
            return;

        try
        {
            _repo.Delete(SelectedInstructor.Id);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "InstructorListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }
}
