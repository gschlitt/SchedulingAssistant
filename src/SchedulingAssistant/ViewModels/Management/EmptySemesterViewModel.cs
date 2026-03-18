using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class EmptySemesterViewModel : ViewModelBase
{
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly SemesterContext _semesterContext;
    private readonly WriteLockService _lockService;

    /// <summary>True when the current user holds the write lock; gates the Empty Semester button.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _selectedAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private Semester? _selectedSemester;

    [ObservableProperty] private int _sectionCount;
    [ObservableProperty] private bool _isCurrentlyLoaded;
    [ObservableProperty] private string? _statusMessage;

    public bool CanEmpty => _lockService.IsWriter && SelectedSemester is not null && !IsCurrentlyLoaded;

    /// <summary>Set by the view. Called before deletion with (semesterName, sectionCount).
    /// Should return true if the user confirms, false to cancel.</summary>
    public Func<string, int, Task<bool>>? ConfirmEmpty { get; set; }

    /// <summary>Set by the view. Called when an error occurs.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public EmptySemesterViewModel(
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        ISectionRepository sectionRepo,
        SemesterContext semesterContext,
        WriteLockService lockService)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _semesterContext = semesterContext;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        SelectedAcademicYear = AcademicYears.FirstOrDefault();
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        EmptyCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAcademicYearChanged(AcademicYear? value)
    {
        LoadSemesters();
    }

    partial void OnSelectedSemesterChanged(Semester? value)
    {
        UpdateSectionCount();
        UpdateCurrentlyLoadedStatus();
    }

    private void LoadSemesters()
    {
        if (SelectedAcademicYear is null)
        {
            Semesters = new ObservableCollection<Semester>();
            SelectedSemester = null;
            return;
        }
        Semesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(SelectedAcademicYear.Id));
        SelectedSemester = Semesters.FirstOrDefault();
    }

    private void UpdateSectionCount()
    {
        if (SelectedSemester is null)
        {
            SectionCount = 0;
            return;
        }
        SectionCount = _sectionRepo.CountBySemesterId(SelectedSemester.Id);
    }

    private void UpdateCurrentlyLoadedStatus()
    {
        if (SelectedSemester is null)
        {
            IsCurrentlyLoaded = false;
            StatusMessage = null;
            return;
        }

        IsCurrentlyLoaded = SelectedSemester.Id == _semesterContext.SelectedSemesterDisplay?.Semester.Id;

        if (IsCurrentlyLoaded)
        {
            StatusMessage = "This semester is currently loaded. To empty it, first switch to a different semester in the main view.";
        }
        else
        {
            StatusMessage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEmpty))]
    private async Task Empty()
    {
        if (SelectedSemester is null) return;

        // Double-check guard
        if (IsCurrentlyLoaded)
        {
            if (ShowError is not null)
                await ShowError("Cannot empty the currently loaded semester. Switch to a different semester first.");
            return;
        }

        // Confirm deletion
        if (ConfirmEmpty is null || !await ConfirmEmpty(SelectedSemester.Name, SectionCount))
            return;

        try
        {
            _sectionRepo.DeleteBySemesterId(SelectedSemester.Id);
            SectionCount = 0;
            StatusMessage = $"Done. All sections have been removed from {SelectedSemester.Name}.";
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "EmptySemesterViewModel.Empty");
            if (ShowError is not null)
                await ShowError("The operation could not be completed. Please try again.");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.CloseFlyoutCommand.Execute(null);
    }
}
