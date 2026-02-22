using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class AcademicYearListViewModel : ViewModelBase
{
    private readonly AcademicYearRepository _ayRepo;
    private readonly SemesterRepository _semRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _selectedAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private AcademicYearEditViewModel? _editVm;

    /// <summary>
    /// Set by the view. Called before deletion with (ayName, sectionCount).
    /// Should return true if the user confirms, false to cancel.
    /// </summary>
    public Func<string, int, Task<bool>>? ConfirmDelete { get; set; }

    public AcademicYearListViewModel(
        AcademicYearRepository ayRepo,
        SemesterRepository semRepo,
        SectionRepository sectionRepo,
        SemesterContext semesterContext)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _semesterContext = semesterContext;
        Load();
    }

    partial void OnSelectedAcademicYearChanged(AcademicYear? value) => LoadSemesters();

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        SelectedAcademicYear = AcademicYears.FirstOrDefault();
    }

    private void LoadSemesters()
    {
        if (SelectedAcademicYear is null)
        {
            Semesters = new ObservableCollection<Semester>();
            return;
        }
        Semesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(SelectedAcademicYear.Id));
    }

    [RelayCommand]
    private void Add()
    {
        var ay = new AcademicYear();
        EditVm = new AcademicYearEditViewModel(ay,
            onSave: saved =>
            {
                _ayRepo.Insert(saved);
                CreateDefaultSemesters(saved.Id);
                _semesterContext.Reload(_ayRepo, _semRepo);
                Load();
                EditVm = null;
            },
            onCancel: () => EditVm = null,
            nameExists: name => _ayRepo.ExistsByName(name));
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedAcademicYear is null) return;

        var sectionCount = _sectionRepo.CountByAcademicYear(SelectedAcademicYear.Id);

        if (ConfirmDelete is not null)
        {
            var confirmed = await ConfirmDelete(SelectedAcademicYear.Name, sectionCount);
            if (!confirmed) return;
        }

        _semRepo.DeleteByAcademicYear(SelectedAcademicYear.Id);
        _ayRepo.Delete(SelectedAcademicYear.Id);
        _semesterContext.Reload(_ayRepo, _semRepo);
        Load();
    }

    private void CreateDefaultSemesters(string academicYearId)
    {
        for (int i = 0; i < Semester.DefaultNames.Length; i++)
        {
            _semRepo.Insert(new Semester
            {
                AcademicYearId = academicYearId,
                Name = Semester.DefaultNames[i],
                SortOrder = i
            });
        }
    }
}
