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
using SchedulingAssistant.Models;
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
using System.Reflection;
using SchedulingAssistant.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Views.Wizard;

namespace SchedulingAssistant;

public partial class MainWindow : Window
{
    private DetachedPanelWindow? _workloadWindow;
    private DetachedPanelWindow? _scheduleGridWindow;
    private MainWindowViewModel? _previousVm;

    public ScheduleGridView? ScheduleGridViewInstance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"TermPoint v{version?.Major}.{version?.Minor}.{version?.Build}";

        // Escape-to-close-flyout is handled declaratively by DismissBehaviors.EscapeCommand
        // on the Window element in AXAML. The KeyDown handler below is retained only for
        // the DEBUG-only error simulation hotkeys.
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
    /// All shutdown logic (e.g. backup, save-state) belongs here so both paths are handled identically.
    /// </summary>
    /// <param name="e">Closing event args; set <c>e.Cancel = true</c> to abort the close.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Stop the periodic backup timer before disposing the container so the timer
        // callback cannot fire against a half-torn-down DI graph.
        if (App.Services?.GetService(typeof(BackupService)) is BackupService bs)
            bs.StopSession();

        // Dispose the DI container, which closes the SQLite connection cleanly.
        (App.Services as IDisposable)?.Dispose();
    }
    //debug
    
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

        // ── First-run wizard ──────────────────────────────────────────────────
        // When IsInitialSetupComplete is false, show the wizard instead of the normal
        // DB-path dialog. The wizard calls App.InitializeServices() internally (step 2),
        // so we must NOT call it again below.
        if (!settings.IsInitialSetupComplete)
        {
            var wizard = new StartupWizardWindow();
            WindowState = WindowState.Minimized;
            wizard.Show(this);

            var tcs = new TaskCompletionSource();
            wizard.Closed += (_, _) => tcs.TrySetResult();
            await tcs.Task;

            WindowState = WindowState.Normal;

            // Reload settings — the wizard saves DatabasePath and sets IsInitialSetupComplete.
            AppSettings.Load();
            settings = AppSettings.Current;

            if (!settings.IsInitialSetupComplete)
            {
                // User closed the wizard before finishing — exit the app.
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }

            // Wizard completed and App.Services is already initialized.
            // Skip straight to wiring up the main window.
            var dbPath = settings.DatabasePath!;
            settings.AddRecentDatabase(dbPath);
            await SetupMainWindowAsync(dbPath);
            return;
        }

        // ── Returning user ────────────────────────────────────────────────────
        await RunReturningUserStartupAsync(settings);
    }

    /// <summary>
    /// Normal startup path for a returning user who already has a database configured.
    /// Validates the saved database path, shows the recovery window if needed, then
    /// calls App.InitializeServices and shows the main window.
    /// </summary>
    private async Task RunReturningUserStartupAsync(AppSettings settings)
    {
        string dbPath;

        var validation = DatabaseValidator.Validate(settings.DatabasePath);

        if (validation == DatabaseValidationResult.Ok)
        {
            // Happy path — file exists and passes integrity check.
            dbPath = settings.DatabasePath!;
        }
        else
        {
            // File is missing or corrupt — show the recovery window.
            var reason = validation == DatabaseValidationResult.Corrupt
                ? RecoveryReason.Corrupt
                : RecoveryReason.NotFound;

            var recovery = new DatabaseRecoveryWindow(reason, settings.DatabasePath);
            await recovery.ShowDialog(this);

            switch (recovery.Vm.Outcome)
            {
                case RecoveryOutcome.Resolved:
                    dbPath = recovery.Vm.ResolvedPath!;
                    settings.DatabasePath = dbPath;
                    settings.Save();
                    break;

                case RecoveryOutcome.StartWizard:
                    // User wants a fresh start — clear the saved path and re-run
                    // startup so the wizard is shown in the normal first-run path.
                    settings.IsInitialSetupComplete = false;
                    settings.DatabasePath           = null;
                    settings.Save();
                    await RunStartupAsync();
                    return;

                default: // RecoveryOutcome.None — user exited
                    await ShowCancelMessageAsync();
                    (Avalonia.Application.Current?.ApplicationLifetime as
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                        ?.Shutdown();
                    return;
            }
        }

        // Record this database in recent list
        settings.AddRecentDatabase(dbPath);

        // Initialize DI and DB, wire up the view model, then reveal the window.
        // DatabaseCorruptException is thrown when integrity_check fails; we handle
        // it by offering a restore dialog before the app opens normally.
        MainWindowViewModel vm;
        try
        {
            vm = App.InitializeServices(dbPath);
        }
        catch (DatabaseCorruptException corruptEx)
        {
            var restored = await ShowCorruptDatabaseDialogAsync(corruptEx.DatabasePath);
            if (!restored)
            {
                await ShowCancelMessageAsync();
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }
            // The restore copied a backup over dbPath — re-initialize with the restored file.
            vm = App.InitializeServices(dbPath);
        }

        await SetupMainWindowAsync(dbPath, vm);
    }

    /// <summary>
    /// Final step of startup: wires the main window VM and makes the window visible.
    /// Used by both the first-run (wizard) path and the returning-user path.
    /// When called from the wizard path, <paramref name="vm"/> is resolved from App.Services
    /// (it was already initialized); otherwise it is provided directly.
    /// </summary>
    private async Task SetupMainWindowAsync(string dbPath, MainWindowViewModel? vm = null)
    {
        if (vm is null)
        {
            // Wizard path — App.Services was already initialized; just resolve the root VM.
            vm = App.Services.GetRequiredService<MainWindowViewModel>();
            vm.SetDatabaseName(Path.GetFileNameWithoutExtension(dbPath));
        }

        // Start the automated backup session if this instance acquired the write lock.
        // Fire-and-forget: the first backup runs asynchronously in the background.
        if (App.Services.GetService(typeof(BackupService)) is BackupService backup
            && App.Services.GetService(typeof(WriteLockService)) is WriteLockService wl
            && wl.IsWriter)
        {
            _ = backup.StartSessionAsync(dbPath);
        }

        DataContext = vm;

        // Set the main window reference and load recent databases
        vm.MainWindowReference = this;
        vm.LoadRecentDatabases();

        // Enqueue any feature announcements the user hasn't seen yet.
        if (App.Services.GetService(typeof(AppNotificationService)) is AppNotificationService notifier)
            notifier.EnqueueUnseenAnnouncements();

        IsVisible = true;
        Activate();

        await Task.CompletedTask;
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
            var settings = AppSettings.Current;
            settings.DatabasePath = newDatabasePath;
            settings.Save();
            settings.AddRecentDatabase(newDatabasePath);

            // Reinitialize DI and set new data context.
            // DatabaseContext will create the file if it doesn't exist.
            var vm = App.InitializeServices(newDatabasePath);

            // Start backup session for the new database if we are the writer.
            if (App.Services.GetService(typeof(BackupService)) is BackupService switchBackup
                && App.Services.GetService(typeof(WriteLockService)) is WriteLockService switchWl
                && switchWl.IsWriter)
            {
                _ = switchBackup.StartSessionAsync(newDatabasePath);
            }

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
            "TermPoint", "Logs");

        var msg = new Window
        {
            Title = "TermPoint — Startup Error",
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

    /// <summary>
    /// Shown when the startup integrity check fails. Lists backups from the configured
    /// backup folder and lets the user pick one to restore before the app opens.
    /// Returns true when a backup was successfully restored over <paramref name="dbPath"/>.
    /// Returns false when the user dismisses without restoring (app will shut down).
    /// </summary>
    private async Task<bool> ShowCorruptDatabaseDialogAsync(string dbPath)
    {
        // Enumerate backups for this database from the configured folder.
        var folder = AppSettings.Current.BackupFolderPath;
        var dbName = Path.GetFileNameWithoutExtension(dbPath);
        var backups = new List<string>();

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            backups = Directory.GetFiles(folder, $"{dbName}_*.db")
                               .OrderByDescending(f => f)
                               .ToList();
        }

        var dlg = new Window
        {
            Title  = "Database Integrity Check Failed",
            Width  = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };

        string? chosen = null;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = "The database failed its integrity check and may be corrupt.",
            FontSize = 13, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Database: {dbPath}",
            FontSize = 11, Foreground = Brushes.DarkRed,
            TextWrapping = TextWrapping.Wrap
        });

        if (backups.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No backups were found. You can exit and manually replace the database file with a backup, or continue at your own risk.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Select a backup to restore, or exit and handle it manually:",
                TextWrapping = TextWrapping.Wrap, FontSize = 12
            });

            foreach (var backupPath in backups)
            {
                var label = Path.GetFileNameWithoutExtension(backupPath);
                var btn   = new Button
                {
                    Content = $"Restore: {label}",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4)
                };
                btn.Click += (_, _) => { chosen = backupPath; dlg.Close(); };
                panel.Children.Add(btn);
            }
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var continueBtn = new Button { Content = "Continue anyway (risky)", FontSize = 11, Padding = new Thickness(8, 4) };
        var exitBtn     = new Button { Content = "Exit", FontSize = 11, Padding = new Thickness(8, 4) };
        continueBtn.Click += (_, _) => { chosen = "continue"; dlg.Close(); };
        exitBtn.Click     += (_, _) => { chosen = null; dlg.Close(); };
        buttonRow.Children.Add(continueBtn);
        buttonRow.Children.Add(exitBtn);
        panel.Children.Add(buttonRow);

        dlg.Content = panel;
        await dlg.ShowDialog(this);

        if (chosen is null) return false;       // Exit
        if (chosen == "continue") return true;  // Proceed with corrupt DB

        // User chose a backup — copy it over the main database file.
        try
        {
            File.Copy(chosen, dbPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Restore Failed", $"Could not restore backup:\n\n{ex.Message}");
            return false;
        }
    }

    private async Task ShowCancelMessageAsync()
    {
        var msg = new Window
        {
            Title = "TermPoint",
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

    // ── Data-context wiring ──────────────────────────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // Unsubscribe from the old VM to prevent memory leaks and stale event callbacks
            // when the DataContext is replaced (e.g. on database switch).
            if (_previousVm is not null)
            {
                _previousVm.PropertyChanged -= OnMainWindowVmPropertyChanged;
                _previousVm.WorkloadPanelVm.ItemClicked -= OnWorkloadItemClicked;
            }

            // Wire the grid's double-click-to-edit callback at the view level.
            // This keeps SectionListViewModel and ScheduleGridViewModel decoupled —
            // neither holds a reference to the other.
            vm.ScheduleGridVm.EditRequested = vm.SectionListVm.EditSectionById;

            vm.PropertyChanged += OnMainWindowVmPropertyChanged;
            vm.WorkloadPanelVm.ItemClicked += OnWorkloadItemClicked;

            _previousVm = vm;

            ScheduleGridViewInstance = this.FindControl<ScheduleGridView>("ScheduleGridViewControl");

#if DEBUG
            // Show debug menu in DEBUG mode
            var debugMenu = this.FindControl<Menu>("DebugMenu");
            if (debugMenu is not null)
                debugMenu.IsVisible = true;
#endif
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
        var sectionItem = Vm.SectionListVm.SectionItems.OfType<SectionListItemViewModel>()
            .FirstOrDefault(s => s.Section.Id == sectionId);

        if (sectionItem is not null)
        {
            Vm.SectionListVm.SelectedItem = sectionItem;
            // SelectedItem change will automatically sync to ScheduleGridViewModel.SelectedSectionId
        }
    }

    // ── Detach handlers ─────────────────────────────────────────────────────

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

        // Remove the view from the ContentPresenter entirely while detached.
        // Just hiding the slot leaves the view in the visual tree with live bindings,
        // which causes the right-click popup (and others) to fire on both the hidden
        // view and the detached window simultaneously.
        slot.PanelContent = null;

        DetachSlot(slot, ref _scheduleGridWindow,
            () => new ScheduleGridView { DataContext = Vm.ScheduleGridVm },
            () =>
            {
                // On reattach, create a fresh view and update the instance reference
                // used by ExportViewModel for PNG export.
                var freshView = new ScheduleGridView { DataContext = Vm.ScheduleGridVm };
                ScheduleGridViewInstance = freshView;
                slot.PanelContent = freshView;
                slot.IsVisible = true;
                _scheduleGridWindow = null;
            },
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

        // Semester — ItemsControl of colored segment pills, matching the main-window AXAML layout.
        // SemesterLineSegment is a record (immutable), so properties are read directly in the
        // template factory; ItemsControl rebuilds from scratch whenever the binding fires.
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
            new Binding("ScheduleGridVm.SemesterLineSegments") { Source = Vm });
        stack.Children.Add(semItemsControl);

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
