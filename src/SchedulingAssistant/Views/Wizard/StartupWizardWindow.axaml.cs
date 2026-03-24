using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
            _vm = new StartupWizardViewModel(this);
            DataContext = _vm;
        };

        Closing += (_, e) =>
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
}
