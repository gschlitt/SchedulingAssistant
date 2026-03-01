using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SchedulingAssistant.Controls;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.GridView;
using SchedulingAssistant.Views.Management;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.IO;

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
            // Show splash screen for 2 seconds
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

            // Reinitialize DI and set new data context
            // DatabaseContext will create the file if it doesn't exist
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
    }

    private void UpdateLeftColumnWidth(bool isEditing)
    {
        var grid = this.FindControl<Grid>("ThreePanelGrid");
        if (grid is null) return;
        // Collapsed: 220 px (summary cards); Expanded: 500 px (editor form)
        grid.ColumnDefinitions[0].Width = isEditing
            ? new GridLength(500, GridUnitType.Pixel)
            : new GridLength(220, GridUnitType.Pixel);
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
            () => new TextBlock
            {
                Text = "Workload View (coming soon)",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            },
            () => { slot.IsVisible = true; _workloadWindow = null; });
    }

    private void OnScheduleGridDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        DetachSlot(slot, ref _scheduleGridWindow,
            () => new ScheduleGridView { DataContext = Vm.ScheduleGridVm },
            () => { slot.IsVisible = true; _scheduleGridWindow = null; });
    }

    // ── Core detach mechanism ───────────────────────────────────────────────

    private void DetachSlot(
        DetachablePanel slot,
        ref DetachedPanelWindow? existingWindow,
        Func<Control> buildContent,
        Action onReattach)
    {
        if (existingWindow is not null)
        {
            existingWindow.Activate();
            return;
        }

        slot.IsVisible = false;

        var win = new DetachedPanelWindow { OnReattach = onReattach };
        win.SetContent(slot.Header, buildContent());
        existingWindow = win;
        win.Show(this);
    }
}
