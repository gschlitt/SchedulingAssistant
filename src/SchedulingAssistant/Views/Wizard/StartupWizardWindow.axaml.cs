using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using SchedulingAssistant.ViewModels.Wizard;

namespace SchedulingAssistant.Views.Wizard;

/// <summary>
/// The startup wizard window. Shown on first run (when IsInitialSetupComplete is false).
/// The window sets its own DataContext to StartupWizardViewModel.
/// When the user closes the window before completing the wizard, the app shuts down.
/// </summary>
public partial class StartupWizardWindow : Window
{
    private StartupWizardViewModel? _vm;
    private bool _shutdownInProgress;

    public StartupWizardWindow()
    {
        InitializeComponent();

        // Defer VM construction until after the window handle exists (needed for StorageProvider)
        Opened += (_, _) =>
        {
            _vm = new StartupWizardViewModel(this, WizardServices.FromApp());
            DataContext = _vm;
        };

        Closing += (_, _2) =>
        {
            // If the user manually closes the window before finishing, shut down the app.
            // Guard against re-entry: Shutdown() closes all windows, which re-fires Closing.
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
