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
    private readonly WriteLockService _lockService;

    /// <summary>
    /// The permanent left-panel section list. Held for app lifetime.
    /// </summary>
    public SectionListViewModel SectionListVm { get; }

    /// <summary>
    /// The permanent right-panel schedule grid. Held for app lifetime.
    /// </summary>
    public ScheduleGridViewModel ScheduleGridVm => _scheduleGridVm;

    /// <summary>
    /// The permanent bottom-left workload panel. Held for app lifetime.
    /// </summary>
    public WorkloadPanelViewModel WorkloadPanelVm { get; }

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

    // ── Write-lock properties ─────────────────────────────────────────────────

    /// <summary>
    /// Timestamp of the last manual data refresh performed while in read-only mode.
    /// Shown in the banner tooltip rather than inline, to avoid being confused with
    /// the lock heartbeat.  Null until the user clicks Refresh for the first time.
    /// </summary>
    private DateTime? _lastRefreshedAt;

    /// <summary>
    /// True when this instance holds the write lock and the user may make edits.
    /// False in read-only mode. Bound by container <c>IsEnabled</c> in the view
    /// to gate all write-capable UI in a single binding per container.
    /// </summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>
    /// True when the lock banner should be displayed (i.e., this instance is in
    /// read-only mode).
    /// </summary>
    public bool ShowLockBanner => !_lockService.IsWriter;

    /// <summary>
    /// Human-readable description of the current lock state for the banner.
    /// Shows who holds the lock and since when.
    /// Returns null when this instance is the writer (banner is hidden).
    /// </summary>
    public string? LockStatusMessage
    {
        get
        {
            if (_lockService.IsWriter) return null;
            var h = _lockService.CurrentHolder;
            return h is null
                ? "Read-only — database is locked by another instance."
                : $"Read-only — held by {h.Username} on {h.Machine} since {h.Acquired.ToLocalTime():h:mm tt}";
        }
    }

    /// <summary>
    /// Tooltip text for the Refresh button in the read-only banner.
    /// Shows when data was last refreshed, or a prompt to refresh if it hasn't been yet.
    /// </summary>
    public string RefreshButtonTooltip => _lastRefreshedAt is { } t
        ? $"Last refreshed at {t:h:mm tt}. Click to reload again."
        : "Reload sections from the database to see changes made by the writer.";

    /// <summary>
    /// True when a read-only instance has detected that the write lock is no longer
    /// held and the user should be prompted to switch to edit mode.
    /// </summary>
    public bool ShowWriteLockAvailablePrompt => _lockService.WriteLockBecameAvailable;

    internal void SetDatabaseName(string name) => DatabaseName = name;

    /// <summary>
    /// Populate the recent databases list from AppSettings.
    /// Call this after the VM is created and the main window is available.
    /// </summary>
    public void LoadRecentDatabases()
    {
        RecentDatabases.Clear();
        var settings = AppSettings.Current;
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
        ScheduleGridViewModel scheduleGridVm,
        WorkloadPanelViewModel workloadPanelVm,
        WriteLockService lockService)
    {
        _services = services;
        SemesterContext = semesterContext;
        SectionListVm = sectionListVm;
        _scheduleGridVm = scheduleGridVm;
        WorkloadPanelVm = workloadPanelVm;
        _lockService = lockService;

        // React to lock state changes (e.g., writer released the lock while we are
        // in read-only mode) so the banner updates automatically.
        _lockService.LockStateChanged += OnLockStateChanged;
    }

    /// <summary>
    /// Called on the UI thread when the lock state changes. Re-raises all lock-derived
    /// properties so the banner and control containers update via data binding.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        OnPropertyChanged(nameof(ShowLockBanner));
        OnPropertyChanged(nameof(LockStatusMessage));
        OnPropertyChanged(nameof(ShowWriteLockAvailablePrompt));
    }

    /// <summary>
    /// Attempts to re-acquire the write lock. Called when the user clicks the
    /// "Switch to edit mode" button in the read-only banner after the polling
    /// timer has signalled that the lock is no longer held.
    /// </summary>
    [RelayCommand]
    private void AcquireWriteLock()
    {
        var path = AppSettings.Current.DatabasePath;
        if (path is null) return;
        _lockService.TryAcquire(path);
        OnLockStateChanged();
    }

    /// <summary>
    /// Reloads all section data from the database while in read-only mode.
    /// Triggers a full <see cref="SectionStore"/> reload, which cascades to the
    /// schedule grid, section list, and workload panel via their <c>SectionsChanged</c>
    /// subscriptions. Records the refresh time so the banner can show "Refreshed at X:XX".
    /// </summary>
    [RelayCommand]
    private void RefreshData()
    {
        // ReloadFromDatabase re-queries the DB and updates the SectionStore cache.
        // The SectionsChanged event then cascades automatically to the schedule grid
        // and workload panel — no additional calls required here.
        SectionListVm.ReloadFromDatabase();
        _lastRefreshedAt = DateTime.Now;
        OnPropertyChanged(nameof(RefreshButtonTooltip));
    }

    partial void OnFlyoutPageChanged(object? oldValue, object? newValue)
    {
        (oldValue as IDisposable)?.Dispose();
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
    private void NavigateToCourses() => OpenFlyout<CourseListViewModel>("Courses");

    [RelayCommand]
    private void NavigateToInstructors() => OpenFlyout<InstructorListViewModel>("Instructors");

    [RelayCommand]
    private void NavigateToSemesters() => OpenFlyout<SemesterListViewModel>("Semesters");

    [RelayCommand]
    private void NavigateToAcademicYears() => OpenFlyout<AcademicYearListViewModel>("Academic Years");

    [RelayCommand]
    private void NavigateToCopySemester()
    {
        var vm = _services.GetRequiredService<CopySemesterViewModel>();
        vm.NavigateToAcademicYears = NavigateToAcademicYears;
        FlyoutPage = vm;
        FlyoutTitle = "Copy Semester";
    }

    [RelayCommand]
    private void NavigateToEmptySemester() => OpenFlyout<EmptySemesterViewModel>("Empty Semester");

    [RelayCommand]
    private void NavigateToBlockLengths() => OpenFlyout<LegalStartTimeListViewModel>("Scheduling");

    [RelayCommand]
    private void NavigateToSectionProperties() => OpenFlyout<SectionPropertiesViewModel>("Section Properties");

    [RelayCommand]
    private void NavigateToBlockPatterns() => OpenFlyout<BlockPatternListViewModel>("Block Patterns");

    [RelayCommand]
    private void NavigateToAcademicUnits() => OpenFlyout<AcademicUnitListViewModel>("Academic Units");

    [RelayCommand]
    private void NavigateToSectionPrefixes() => OpenFlyout<SectionPrefixListViewModel>("Section Prefixes");

    [RelayCommand]
    private void NavigateToExport() => OpenFlyout<ExportViewModel>("Export");

    [RelayCommand]
    private void NavigateToWorkloadReport() => OpenFlyout<WorkloadReportViewModel>("Workload Report");

    [RelayCommand]
    private void NavigateToWorkloadMailer() => OpenFlyout<WorkloadMailerViewModel>("Workload Mailer");

#if DEBUG
    [RelayCommand]
    private void OpenDebug() => OpenFlyout<DebugTestDataViewModel>("Debug: Generate Test Data");

    // ONE-TIME MIGRATION UTILITY — remove after migration is complete
    // Not available in the browser (WASM) demo build.
#if !BROWSER
    [RelayCommand]
    private void OpenMigration() => OpenFlyout<MigrationViewModel>("Migration: CSV → JSON (one-time utility)");
#endif
#endif

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

    /// <summary>
    /// Closes the main window via the Files → Exit menu item.
    /// Triggers the same <see cref="MainWindow.OnClosing"/> path as clicking the title-bar X,
    /// so all shutdown logic lives in one place.
    /// </summary>
    [RelayCommand]
    private void Exit() => MainWindowReference?.Close();
}
