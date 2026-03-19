// MainWindow.axaml.cs
//
// WHY THIS CLASS IS A THIN SHELL:
// All application UI lives in MainView (UserControl) so the browser (WASM) build can
// host the same UI via ISingleViewApplicationLifetime without a Window.
//
// What stays here: everything that *requires* a Window —
//   • Startup: splash screen, DB-path dialog, DI initialization
//   • Runtime: switching databases, write-lock acquisition
//   • Window management: OnClosing (disposes DI container), panel detaching
//     (creates secondary windows for the Workload and Schedule Grid panels)
//
// What moved to MainView.axaml.cs:
//   • OnDataContextChanged — ViewModel event wiring, EditRequested callback
//   • OnWorkloadItemClicked — cross-panel selection sync
//   • OnSectionViewHeaderPointerPressed — sort-mode context menu
//   • OnMainWindowVmPropertyChanged — flyout-close workload refresh

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.GridView;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SchedulingAssistant;

public partial class MainWindow : Window
{
    private DetachedPanelWindow? _workloadWindow;
    private DetachedPanelWindow? _scheduleGridWindow;

    /// <summary>
    /// Reference to the schedule grid view instance, resolved inside MainView after
    /// DataContext is bound.  Delegated here for callers that hold a MainWindow reference.
    /// </summary>
    public ScheduleGridView? ScheduleGridViewInstance =>
        this.FindControl<MainView>("MainViewInstance")?.ScheduleGridViewInstance;

    public MainWindow()
    {
        InitializeComponent();

        // Wire detach events from the DetachablePanels that live inside MainView.
        // We subscribe here (in the desktop Window) rather than in MainView because
        // panel detaching creates secondary Window instances — a concept that does not
        // exist in the browser build and therefore must not leak into shared view code.
        var mainView = this.FindControl<MainView>("MainViewInstance");
        var workloadPanel   = mainView?.FindControl<DetachablePanel>("WorkloadPanel");
        var schedulePanel   = mainView?.FindControl<DetachablePanel>("ScheduleGridPanel");
        if (workloadPanel  is not null) workloadPanel.DetachRequested  += OnWorkloadDetach;
        if (schedulePanel  is not null) schedulePanel.DetachRequested  += OnScheduleGridDetach;

#if DEBUG
        KeyDown += (_, e) =>
        {
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
        };
#endif
    }

    /// <summary>
    /// Called whenever the window is about to close — whether via Files → Exit or the title-bar X.
    /// Disposes the DI container, which closes the SQLite connection cleanly.
    /// </summary>
    /// <param name="e">Closing event args; set <c>e.Cancel = true</c> to abort the close.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (App.Services as IDisposable)?.Dispose();
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
        var settings = AppSettings.Current;
        string? dbPath = null;

        if (!string.IsNullOrWhiteSpace(settings.DatabasePath) && File.Exists(settings.DatabasePath))
        {
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
                await ShowCancelMessageAsync();
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }

            settings.DatabasePath = dbPath;
            settings.Save();
        }

        settings.AddRecentDatabase(dbPath);

#if !BROWSER
        var vm = App.InitializeServices(dbPath);
        DataContext = vm;

        vm.MainWindowReference = this;
        vm.LoadRecentDatabases();

        IsVisible = true;
        Activate();
#endif
    }

    /// <summary>
    /// Switches to a different database without restarting the app.
    /// Called from the Files menu to open a different database file.
    /// The database file will be created if it does not already exist.
    /// </summary>
    /// <param name="newDatabasePath">Absolute path to the target database file.</param>
    public async Task SwitchDatabaseAsync(string newDatabasePath)
    {
        try
        {
            var settings = AppSettings.Current;
            settings.DatabasePath = newDatabasePath;
            settings.Save();
            settings.AddRecentDatabase(newDatabasePath);

#if !BROWSER
            var vm = App.InitializeServices(newDatabasePath);
            DataContext = vm;

            vm.MainWindowReference = this;
            vm.LoadRecentDatabases();
#endif
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Failed to switch database");
            await ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");
        }
    }

    // ── Detach handlers ─────────────────────────────────────────────────────
    // These create secondary Window instances and therefore cannot live in MainView,
    // which is shared with the browser build where secondary windows do not exist.

    private void OnWorkloadDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        DetachSlot(slot, ref _workloadWindow,
            () => new WorkloadPanelView { DataContext = ((MainWindowViewModel)DataContext!).WorkloadPanelVm },
            () => { slot.IsVisible = true; _workloadWindow = null; });
    }

    private void OnScheduleGridDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        var headerContext = CreateScheduleGridHeaderContext();
        DetachSlot(slot, ref _scheduleGridWindow,
            () => new ScheduleGridView { DataContext = ((MainWindowViewModel)DataContext!).ScheduleGridVm },
            () => { slot.IsVisible = true; _scheduleGridWindow = null; },
            headerContext);
    }

    private StackPanel CreateScheduleGridHeaderContext()
    {
        var vm = (MainWindowViewModel)DataContext!;

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        var auTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        auTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.AcademicUnitName") { Source = vm });
        auTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auTextBlock);

        var auSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        auSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auSeparator);

        var semFontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d);
        var semItemsControl = new ItemsControl
        {
            VerticalAlignment = VerticalAlignment.Center,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            }),
            ItemTemplate = new FuncDataTemplate<SemesterLineSegment>((seg, _) =>
                new Border
                {
                    Padding          = new Thickness(6, 2),
                    Background       = seg.Background,
                    BorderBrush      = seg.Border,
                    BorderThickness  = new Thickness(1),
                    CornerRadius     = new CornerRadius(2),
                    Child = new TextBlock
                    {
                        FontSize = semFontSize,
                        Text     = seg.DisplayText
                    }
                })
        };
        semItemsControl.Bind(ItemsControl.ItemsSourceProperty,
            new Binding("ScheduleGridVm.SemesterLineSegments") { Source = vm });
        stack.Children.Add(semItemsControl);

        var subjectSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        subjectSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.SubjectFilterSummary")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(subjectSeparator);

        var subjectTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        subjectTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.SubjectFilterSummary") { Source = vm });
        subjectTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.SubjectFilterSummary")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(subjectTextBlock);

        var statsSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        statsSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsSeparator);

        var statsTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        statsTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.StatsLine") { Source = vm });
        statsTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsTextBlock);

        var filterSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        filterSeparator.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = vm.ScheduleGridVm });
        stack.Children.Add(filterSeparator);

        var filteredByLabel = new TextBlock
        {
            Text = "Filtered by:",
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center
        };
        filteredByLabel.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = vm.ScheduleGridVm });
        stack.Children.Add(filteredByLabel);

        var filterTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        };
        filterTextBlock.Bind(TextBlock.TextProperty, new Binding("Filter.ActiveSummary") { Source = vm.ScheduleGridVm });
        filterTextBlock.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = vm.ScheduleGridVm });
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

    // ── Dialog helpers ───────────────────────────────────────────────────────

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
        var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 16 };
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
        var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 16 };
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
}
