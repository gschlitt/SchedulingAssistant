using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using System;

namespace SchedulingAssistant.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ScheduleGridViewModel _scheduleGridVm;

    /// <summary>
    /// The permanent left-panel section list. Held for app lifetime.
    /// </summary>
    public SectionListViewModel SectionListVm { get; }

    /// <summary>
    /// The permanent right-panel schedule grid. Held for app lifetime.
    /// </summary>
    public ScheduleGridViewModel ScheduleGridVm => _scheduleGridVm;

    /// <summary>
    /// The management ViewModel currently shown in the flyout overlay.
    /// Null when the flyout is hidden.
    /// </summary>
    [ObservableProperty]
    private object? _flyoutPage;

    /// <summary>
    /// Title shown in the flyout header bar.
    /// </summary>
    [ObservableProperty]
    private string _flyoutTitle = string.Empty;

    /// <summary>
    /// Exposed so MainWindow.axaml can bind the semester ComboBox directly
    /// to the singleton context without going through intermediary properties.
    /// </summary>
    public SemesterContext SemesterContext { get; }

    /// <summary>
    /// The display name of the open database (filename without extension).
    /// </summary>
    [ObservableProperty]
    private string _databaseName = string.Empty;

    internal void SetDatabaseName(string name) => DatabaseName = name;

    public MainWindowViewModel(
        IServiceProvider services,
        SemesterContext semesterContext,
        SectionListViewModel sectionListVm,
        ScheduleGridViewModel scheduleGridVm)
    {
        _services = services;
        SemesterContext = semesterContext;
        SectionListVm = sectionListVm;
        _scheduleGridVm = scheduleGridVm;
    }

    [RelayCommand]
    private void CloseFlyout() => FlyoutPage = null;

    private void OpenFlyout<TViewModel>(string title) where TViewModel : ViewModelBase
    {
        try
        {
            FlyoutPage = _services.GetRequiredService<TViewModel>();
            FlyoutTitle = title;
        }
        catch (Exception ex)
        {
            FlyoutPage = new ErrorViewModel($"Failed to open {title}: {ex.Message}\n\n{ex}");
            FlyoutTitle = title;
        }
    }

    [RelayCommand]
    private void NavigateToSubjects() => OpenFlyout<SubjectListViewModel>("Subjects");

    [RelayCommand]
    private void NavigateToCourses() => OpenFlyout<CourseListViewModel>("Courses");

    [RelayCommand]
    private void NavigateToInstructors() => OpenFlyout<InstructorListViewModel>("Instructors");

    [RelayCommand]
    private void NavigateToRooms() => OpenFlyout<RoomListViewModel>("Rooms");

    [RelayCommand]
    private void NavigateToSemesters() => OpenFlyout<SemesterListViewModel>("Semesters");

    [RelayCommand]
    private void NavigateToAcademicYears() => OpenFlyout<AcademicYearListViewModel>("Academic Years");

    [RelayCommand]
    private void NavigateToBlockLengths() => OpenFlyout<LegalStartTimeListViewModel>("Block Lengths");
}
