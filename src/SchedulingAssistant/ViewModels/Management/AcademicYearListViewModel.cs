using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
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
    private readonly LegalStartTimeRepository _legalStartTimeRepo;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _selectedAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private AcademicYearEditViewModel? _editVm;

    /// <summary>
    /// Set by the view. Called before deletion with (ayName, sectionCount).
    /// Should return true if the user confirms, false to cancel.
    /// </summary>
    public Func<string, int, Task<bool>>? ConfirmDelete { get; set; }

    /// <summary>
    /// Set by the view. Called when adding a new academic year to ask if the user
    /// wants to copy the start-time/block-length setup from the previous year.
    /// Receives the previous year's name and ID; should return the ID to copy from, or null to skip.
    /// </summary>
    public Func<string, string, Task<string?>>? ConfirmCopyStartTimes { get; set; }

    /// <summary>
    /// Set by the view. Called when creating the first academic year if persisted data exists.
    /// Receives the persisted data summary; should return true to import, false to skip.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmImportPersistedData { get; set; }

    public AcademicYearListViewModel(
        AcademicYearRepository ayRepo,
        SemesterRepository semRepo,
        SectionRepository sectionRepo,
        SemesterContext semesterContext,
        LegalStartTimeRepository legalStartTimeRepo)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _semesterContext = semesterContext;
        _legalStartTimeRepo = legalStartTimeRepo;
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
            onSave: null,
            onCancel: () => EditVm = null,
            nameExists: name => _ayRepo.ExistsByName(name),
            onSaveAsync: async saved =>
            {
                _ayRepo.Insert(saved);
                CreateDefaultSemesters(saved.Id);

                var allAYs = _ayRepo.GetAll().OrderBy(a => a.Name).ToList();
                var savedIndex = allAYs.FindIndex(a => a.Id == saved.Id);

                if (savedIndex == 0)
                {
                    // This is the first academic year — check if persisted data exists
                    var persistedSummary = LegalStartTimesDataStore.GetPersistedDataSummary();
                    if (!string.IsNullOrEmpty(persistedSummary) && ConfirmImportPersistedData is not null)
                    {
                        var shouldImport = await ConfirmImportPersistedData(persistedSummary);
                        if (shouldImport)
                        {
                            var dbContext = App.Services.GetRequiredService<DatabaseContext>();
                            SeedData.ImportPersistedStartTimes(dbContext.Connection, saved.Id);
                        }
                    }
                }
                else if (savedIndex > 0)
                {
                    // Not the first year — ask to copy from previous
                    var prevAY = allAYs[savedIndex - 1];
                    string? fromAyId = null;
                    if (ConfirmCopyStartTimes is not null)
                    {
                        fromAyId = await ConfirmCopyStartTimes(prevAY.Name, prevAY.Id);
                    }

                    if (fromAyId is not null)
                    {
                        _legalStartTimeRepo.CopyFromPreviousYear(saved.Id, fromAyId);
                    }
                }

                _semesterContext.Reload(_ayRepo, _semRepo);
                Load();
                EditVm = null;
            });
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
