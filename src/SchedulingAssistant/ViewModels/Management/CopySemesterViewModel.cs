using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CopySemesterViewModel : ViewModelBase
{
    private readonly AcademicYearRepository _ayRepo;
    private readonly SemesterRepository _semRepo;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _fromAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _fromSemesters = new();
    [ObservableProperty] private Semester? _fromSemester;

    [ObservableProperty] private AcademicYear? _toAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _toSemesters = new();
    [ObservableProperty] private Semester? _toSemester;

    [ObservableProperty] private bool _isCopyEnabled;

    /// <summary>
    /// Set by the view. Called when user wants to navigate back to Academic Years.
    /// </summary>
    public Action? OnNavigateBackToAcademicYears { get; set; }

    public CopySemesterViewModel(
        AcademicYearRepository ayRepo,
        SemesterRepository semRepo)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        FromAcademicYear = AcademicYears.FirstOrDefault();
        ToAcademicYear = AcademicYears.FirstOrDefault();
    }

    partial void OnFromAcademicYearChanged(AcademicYear? value)
    {
        LoadFromSemesters();
        UpdateCopyEnabled();
    }

    partial void OnFromSemesterChanged(Semester? value)
    {
        UpdateCopyEnabled();
    }

    partial void OnToAcademicYearChanged(AcademicYear? value)
    {
        LoadToSemesters();
        UpdateCopyEnabled();
    }

    partial void OnToSemesterChanged(Semester? value)
    {
        UpdateCopyEnabled();
    }

    private void LoadFromSemesters()
    {
        if (FromAcademicYear is null)
        {
            FromSemesters = new ObservableCollection<Semester>();
            FromSemester = null;
            return;
        }
        FromSemesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(FromAcademicYear.Id));
        FromSemester = FromSemesters.FirstOrDefault();
    }

    private void LoadToSemesters()
    {
        if (ToAcademicYear is null)
        {
            ToSemesters = new ObservableCollection<Semester>();
            ToSemester = null;
            return;
        }
        ToSemesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(ToAcademicYear.Id));
        ToSemester = ToSemesters.FirstOrDefault();
    }

    private void UpdateCopyEnabled()
    {
        IsCopyEnabled = FromAcademicYear is not null && FromSemester is not null &&
                        ToAcademicYear is not null && ToSemester is not null &&
                        (FromAcademicYear.Id != ToAcademicYear.Id || FromSemester.Id != ToSemester.Id);
    }

    [RelayCommand]
    private void Copy()
    {
        // Placeholder for copy logic
        // TODO: Implement the section copy logic
        OnNavigateBackToAcademicYears?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnNavigateBackToAcademicYears?.Invoke();
    }
}
