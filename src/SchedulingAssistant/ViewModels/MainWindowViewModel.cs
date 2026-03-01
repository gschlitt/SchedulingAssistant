using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.Views;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Collections.ObjectModel;
using System.IO;

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

    /// <summary>
    /// Observable list of recent database files for the Files menu.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RecentDatabaseItem> _recentDatabases = new();

    /// <summary>
    /// Reference to the main window for file operations.
    /// Set by MainWindow.axaml.cs after instantiation.
    /// </summary>
    public MainWindow? MainWindowReference { get; set; }

    internal void SetDatabaseName(string name) => DatabaseName = name;

    /// <summary>
    /// Populate the recent databases list from AppSettings.
    /// Call this after the VM is created and the main window is available.
    /// </summary>
    public void LoadRecentDatabases()
    {
        RecentDatabases.Clear();
        var settings = AppSettings.Load();
        foreach (var path in settings.RecentDatabases)
        {
            if (File.Exists(path))
            {
                RecentDatabases.Add(new RecentDatabaseItem
                {
                    Path = path,
                    DisplayName = Path.GetFileName(path)
                });
            }
        }
    }

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
    private void NavigateToBlockLengths() => OpenFlyout<LegalStartTimeListViewModel>("Scheduling");

    [RelayCommand]
    private void NavigateToSectionProperties() => OpenFlyout<SectionPropertiesViewModel>("Section Properties");

    [RelayCommand]
    private void NavigateToBlockPatterns() => OpenFlyout<BlockPatternListViewModel>("Block Patterns");

    // ── File menu commands ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDatabase()
    {
        if (MainWindowReference is null) return;

        var dialog = new DatabaseLocationDialog(DatabaseLocationMode.OpenExisting);
        await dialog.ShowDialog(MainWindowReference);

        if (dialog.ChosenPath is not null)
        {
            await MainWindowReference.SwitchDatabaseAsync(dialog.ChosenPath);
        }
    }

    [RelayCommand]
    private async Task NewDatabase()
    {
        if (MainWindowReference is null) return;

        var dialog = new DatabaseLocationDialog(DatabaseLocationMode.FirstRun);
        dialog.Title = "Create New Database";
        await dialog.ShowDialog(MainWindowReference);

        if (dialog.ChosenPath is not null)
        {
            await MainWindowReference.SwitchDatabaseAsync(dialog.ChosenPath);
        }
    }

    [RelayCommand]
    private async Task OpenRecentDatabase(string? databasePath)
    {
        if (databasePath is null || MainWindowReference is null) return;
        await MainWindowReference.SwitchDatabaseAsync(databasePath);
    }
}
