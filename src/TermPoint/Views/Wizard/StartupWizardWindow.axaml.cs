using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using TermPoint.ViewModels.Wizard;

namespace TermPoint.Views.Wizard;

/// <summary>
/// The startup wizard window. Shown on first run (when IsInitialSetupComplete is false).
/// The window sets its own DataContext to StartupWizardViewModel.
/// When the user closes the window before completing the wizard, the app shuts down.
/// </summary>
public partial class StartupWizardWindow : Window
{
    private StartupWizardViewModel? _vm;
    private bool _shutdownInProgress;

    /// <summary>
    /// Raised after the user finishes setup and the wizard has HIDDEN itself. The host
    /// (<see cref="MainWindow.RunStartupAsync"/>) proceeds to open the database on this signal.
    /// We hide rather than close so we never dispose the WinUI composition right after the native
    /// file picker — that disposal deadlocks the UI thread against the compositor thread.
    /// </summary>
    public event EventHandler? SetupCompleted;

    public StartupWizardWindow()
    {
        InitializeComponent();

        // Defer VM construction until after the window handle exists (needed for StorageProvider)
        Opened += (_, _) =>
        {
            _vm = new StartupWizardViewModel(this, WizardServices.FromApp());
            DataContext = _vm;

            // On successful completion, HIDE the wizard (don't Close it) and notify the host.
            // Closing disposes the composition and deadlocks against the compositor thread right
            // after the file picker; hiding sidesteps that. The window is disposed at app shutdown.
            _vm.SetupCompleted += () =>
            {
                Hide();
                SetupCompleted?.Invoke(this, EventArgs.Empty);
            };
        };

        Closing += (_, e) =>
        {
            // Always cancel Close and hide instead — disposing the WinUI composition
            // right after a native file picker deadlocks the compositor (Avalonia 12 bug).
            // The window is cleaned up at process exit.
            e.Cancel = true;
            Hide();

            // If the user manually closes the window before finishing, shut down the app.
            // Guard against re-entry: Shutdown() may re-fire Closing on other windows.
            if (_shutdownInProgress) return;
            if (_vm is not null && !_vm.IsComplete)
            {
                _shutdownInProgress = true;
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
            }
        };
    }

    /// <summary>
    /// Allows the window to be dragged by its custom header bar.
    /// Required because <see cref="WindowDecorations.BorderOnly"/> hides the OS title bar.
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
