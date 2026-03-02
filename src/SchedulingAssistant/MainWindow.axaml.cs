using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SchedulingAssistant.Controls;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.GridView;
using SchedulingAssistant.Views.Management;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace SchedulingAssistant;

public partial class MainWindow : Window
{
    private DetachedPanelWindow? _sectionEditorWindow;
    private DetachedPanelWindow? _workloadWindow;
    private DetachedPanelWindow? _scheduleGridWindow;

    public MainWindow()
    {
        InitializeComponent();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm && vm.FlyoutPage is not null)
            {
                vm.FlyoutPage = null;
                e.Handled = true;
            }

#if DEBUG
            // DEV-ONLY hotkeys for testing error banners. Remove before shipping.
            // Ctrl+Shift+E → simulate schedule grid error
            // Ctrl+Shift+W → simulate section list error
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
                && DataContext is MainWindowViewModel debugVm)
            {
                if (e.Key == Key.E)
                {
                    debugVm.ScheduleGridVm.SimulateReloadError();
                    e.Handled = true;
                }
                else if (e.Key == Key.W)
                {
                    debugVm.SectionListVm.SimulateLoadError();
                    e.Handled = true;
                }
            }
#endif
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            var splash = new SplashScreen();
            splash.Show();

            await Task.Delay(2000);

            splash.Close();

            await RunStartupAsync();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Unhandled exception during startup");
            await ShowStartupErrorAsync(ex);
            (Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
        }
    }

    private async Task RunStartupAsync()
    {
        var settings = AppSettings.Load();
        string? dbPath = null;

        if (!string.IsNullOrWhiteSpace(settings.DatabasePath) && File.Exists(settings.DatabasePath))
        {
            // Happy path — saved path exists and file is there.
            dbPath = settings.DatabasePath;
        }
        else
        {
            var mode = string.IsNullOrWhiteSpace(settings.DatabasePath)
                ? DatabaseLocationMode.FirstRun
                : DatabaseLocationMode.NotFound;

            dbPath = await ShowLocationDialogAsync(mode);

            if (dbPath is null)
            {
                // User cancelled — inform and exit.
                await ShowCancelMessageAsync();
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }

            settings.DatabasePath = dbPath;
            settings.Save();
        }

        // Record this database in recent list
        settings.AddRecentDatabase(dbPath);

        // Initialize DI and DB, wire up the view model, then reveal the window.
        var vm = App.InitializeServices(dbPath);
        DataContext = vm;

        // Set the main window reference and load recent databases
        vm.MainWindowReference = this;
        vm.LoadRecentDatabases();

        IsVisible = true;
        Activate();
    }

    /// <summary>
    /// Switch to a different database without restarting the app.
    /// Call this from the Files menu to open a different database.
    /// The database file will be created if it doesn't exist.
    /// </summary>
    public async Task SwitchDatabaseAsync(string newDatabasePath)
    {
        try
        {
            // Update settings and record in recent list
            var settings = AppSettings.Load();
            settings.DatabasePath = newDatabasePath;
            settings.Save();
            settings.AddRecentDatabase(newDatabasePath);

            // Reinitialize DI and set new data context.
            // DatabaseContext will create the file if it doesn't exist.
            var vm = App.InitializeServices(newDatabasePath);
            DataContext = vm;

            // Reload recent databases in the menu
            vm.MainWindowReference = this;
            vm.LoadRecentDatabases();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Failed to switch database");
            await ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");
        }
    }

    private async Task<string?> ShowLocationDialogAsync(DatabaseLocationMode mode)
    {
        var dialog = new DatabaseLocationDialog(mode);
        await dialog.ShowDialog(this);
        return dialog.ChosenPath;
    }

    private async Task ShowStartupErrorAsync(Exception ex)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SchedulingAssistant", "Logs");

        var msg = new Window
        {
            Title = "Scheduling Assistant — Startup Error",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };

        var ok = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Scheduling Assistant encountered an error during startup and cannot continue.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = ex.Message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brushes.DarkRed
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"A full error log has been written to:\n{logDir}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray
        });
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => msg.Close();
        await msg.ShowDialog(this);
    }

    private async Task ShowCancelMessageAsync()
    {
        var msg = new Window
        {
            Title = "Scheduling Assistant",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16
        };
        panel.Children.Add(new TextBlock
        {
            Text = "A database location must be chosen before Scheduling Assistant can open. The application will now close.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => msg.Close();
        await msg.ShowDialog(this);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var msg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => msg.Close();
        await msg.ShowDialog(this);
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    // ── Left-column auto-sizing ──────────────────────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SectionListVm.PropertyChanged += OnSectionListVmPropertyChanged;
            vm.ScheduleGridVm.PropertyChanged += OnScheduleGridVmPropertyChanged;
            vm.PropertyChanged += OnMainWindowVmPropertyChanged;
            vm.WorkloadPanelVm.ItemClicked += OnWorkloadItemClicked;
            UpdateLeftColumnWidth(vm.SectionListVm.IsEditing);

#if DEBUG
            // Show debug menu in DEBUG mode
            var debugMenu = this.FindControl<Menu>("DebugMenu");
            if (debugMenu is not null)
                debugMenu.IsVisible = true;
#endif
        }
    }

    private void OnSectionListVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SectionListViewModel.IsEditing))
            UpdateLeftColumnWidth(Vm.SectionListVm.IsEditing);
        else if (e.PropertyName == nameof(SectionListViewModel.SelectedItem))
            Vm.WorkloadPanelVm.SelectedSectionId = Vm.SectionListVm.SelectedItem?.Section.Id;
    }

    private void OnScheduleGridVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When a tile is clicked in the Schedule Grid, sync the selection back to the Section List and Workload View
        if (e.PropertyName == nameof(ScheduleGridViewModel.SelectedSectionId))
        {
            var sectionId = Vm.ScheduleGridVm.SelectedSectionId;

            // Update Workload View selection
            Vm.WorkloadPanelVm.SelectedSectionId = sectionId;

            if (string.IsNullOrEmpty(sectionId))
            {
                // Clear selection in Section List if grid cleared
                if (Vm.SectionListVm.SelectedItem is not null)
                    Vm.SectionListVm.SelectedItem = null;
                return;
            }

            // Only update if the currently selected item doesn't match
            if (Vm.SectionListVm.SelectedItem?.Section.Id != sectionId)
            {
                var sectionItem = Vm.SectionListVm.SectionItems.FirstOrDefault(s => s.Section.Id == sectionId);
                if (sectionItem is not null)
                    Vm.SectionListVm.SelectedItem = sectionItem;
            }
        }
    }

    private void OnMainWindowVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When flyout closes (FlyoutPage becomes null), refresh workload to catch any release changes
        if (e.PropertyName == nameof(MainWindowViewModel.FlyoutPage) && Vm.FlyoutPage is null)
            Vm.WorkloadPanelVm.Reload();
    }

    private void OnWorkloadItemClicked(WorkloadItemViewModel item)
    {
        // Only handle section items; releases don't have a direct display in Section View
        if (item.Kind != WorkloadItemKind.Section)
            return;

        // Find the corresponding section in the section list and select it
        var sectionId = item.Id;
        var sectionItem = Vm.SectionListVm.SectionItems.FirstOrDefault(s => s.Section.Id == sectionId);

        if (sectionItem is not null)
        {
            Vm.SectionListVm.SelectedItem = sectionItem;
            // SelectedItem change will automatically sync to ScheduleGridViewModel.SelectedSectionId
        }
    }

    private void UpdateLeftColumnWidth(bool isEditing)
    {
        var grid = this.FindControl<Grid>("ThreePanelGrid");
        if (grid is null) return;
        // Collapsed: 370 px (summary cards); Expanded: 650 px (editor form)
        grid.ColumnDefinitions[0].Width = isEditing
            ? new GridLength(650, GridUnitType.Pixel)
            : new GridLength(370, GridUnitType.Pixel);
    }

    // ── Flyout backdrop ─────────────────────────────────────────────────────

    private void OnFlyoutBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Vm.FlyoutPage = null;
    }

    // ── Detach handlers ─────────────────────────────────────────────────────

    private void OnSectionEditorDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        DetachSlot(slot, ref _sectionEditorWindow,
            () => new SectionListView { DataContext = Vm.SectionListVm },
            () => { slot.IsVisible = true; _sectionEditorWindow = null; });
    }

    private void OnWorkloadDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        DetachSlot(slot, ref _workloadWindow,
            () => new WorkloadPanelView { DataContext = Vm.WorkloadPanelVm },
            () => { slot.IsVisible = true; _workloadWindow = null; });
    }

    private void OnScheduleGridDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        var headerContext = CreateScheduleGridHeaderContext();
        DetachSlot(slot, ref _scheduleGridWindow,
            () => new ScheduleGridView { DataContext = Vm.ScheduleGridVm },
            () => { slot.IsVisible = true; _scheduleGridWindow = null; },
            headerContext);
    }

    private StackPanel CreateScheduleGridHeaderContext()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Academic Unit name
        var auTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        auTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.AcademicUnitName") { Source = Vm });
        auTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auTextBlock);

        // Separator (only shown when Academic Unit is available)
        var auSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        auSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auSeparator);

        // Semester
        var semesterTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        semesterTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.SemesterLine") { Source = Vm });
        stack.Children.Add(semesterTextBlock);

        // Subject filter separator (only shown when subject filter is active)
        var subjectSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        subjectSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.SubjectFilterSummary")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(subjectSeparator);

        // Subject filter
        var subjectTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        subjectTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.SubjectFilterSummary") { Source = Vm });
        subjectTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.SubjectFilterSummary")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(subjectTextBlock);

        // Stats separator
        var statsSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        statsSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsSeparator);

        // Stats
        var statsTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        statsTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.StatsLine") { Source = Vm });
        statsTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsTextBlock);

        // Filter separator
        var filterSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        filterSeparator.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filterSeparator);

        // "Filtered by:" label
        var filteredByLabel = new TextBlock
        {
            Text = "Filtered by:",
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center
        };
        filteredByLabel.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filteredByLabel);

        // Filter summary
        var filterTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        };
        filterTextBlock.Bind(TextBlock.TextProperty, new Binding("Filter.ActiveSummary") { Source = Vm.ScheduleGridVm });
        filterTextBlock.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filterTextBlock);

        return stack;
    }

    // ── Core detach mechanism ───────────────────────────────────────────────

    private void DetachSlot(
        DetachablePanel slot,
        ref DetachedPanelWindow? existingWindow,
        Func<Control> buildContent,
        Action onReattach,
        object? headerContext = null)
    {
        if (existingWindow is not null)
        {
            existingWindow.Activate();
            return;
        }

        slot.IsVisible = false;

        var win = new DetachedPanelWindow { OnReattach = onReattach };
        win.SetContent(slot.Header, buildContent(), headerContext);
        existingWindow = win;
        win.Show(this);
    }
}
