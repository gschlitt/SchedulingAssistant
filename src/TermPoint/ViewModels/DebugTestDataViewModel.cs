using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TermPoint.ViewModels;

/// <summary>
/// The Dev Tools "Debug View..." flyout (Debug builds only; the menu item is hidden in Release). It holds the
/// developer switch that makes <see cref="Services.IAppLogger.LogError"/> re-throw, and a button to test it.
/// </summary>
public partial class DebugTestDataViewModel : ViewModelBase
{
#if DEBUG
    /// <summary>The result of the last <see cref="TestThrowCommand"/>, shown under the controls.</summary>
    [ObservableProperty] private string? _statusMessage;

    /// <summary>
    /// When true, exceptions passed to <see cref="Services.IAppLogger.LogError"/> are re-thrown
    /// after being written to the log file, so they surface immediately in the debugger
    /// rather than being silently swallowed by catch blocks.
    /// Mirrors <see cref="Services.IAppLogger.ThrowOnError"/> on the singleton logger.
    /// </summary>
    [ObservableProperty]
    private bool _throwOnError = true;

    partial void OnThrowOnErrorChanged(bool value) => App.Logger.ThrowOnError = value;

    private readonly MainWindowViewModel _mainWindowVm;

    /// <summary>Creates the flyout's view model and pushes the default <see cref="ThrowOnError"/> to the logger.</summary>
    /// <param name="mainWindowVm">The main window's view model, used to close the flyout.</param>
    public DebugTestDataViewModel(MainWindowViewModel mainWindowVm)
    {
        _mainWindowVm = mainWindowVm;
        // Sync the logger with the default field value — OnThrowOnErrorChanged
        // does not fire for field initializers, so we push the default manually.
        App.Logger.ThrowOnError = _throwOnError;
    }

    /// <summary>
    /// Fires a synthetic exception through <see cref="App.Logger"/> so you can verify
    /// that <see cref="ThrowOnError"/> is working as expected. When the flag is off the
    /// exception is silently logged; when on it propagates back up to the caller.
    /// </summary>
    [RelayCommand]
    private void TestThrow()
    {
        try
        {
            throw new InvalidOperationException("Test exception — ThrowOnError verification.");
        }
        catch (Exception ex)
        {
            // If ThrowOnError is true this will re-throw, which propagates out of this
            // method and will be caught by Avalonia's unhandled-exception handler or
            // break into the debugger, depending on how the IDE is configured.
            App.Logger.LogError(ex, "DebugTestDataViewModel.TestThrow");

            // Only reached when ThrowOnError is false.
            StatusMessage = "Test exception was logged (not re-thrown). Enable the checkbox to re-throw.";
        }
    }

    /// <summary>Closes the flyout.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _mainWindowVm.CloseFlyoutCommand.Execute(null);
    }
#endif
}
