using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Inline error message shown in the view when an operation fails or is blocked by a
    /// business rule. Set to a descriptive string on failure; clear to <c>null</c> at the
    /// start of each new operation. Replaces modal error dialogs, which are unavailable in
    /// the WASM browser demo and are heavier than necessary for routine validation feedback.
    /// Views bind a <c>TextBlock</c> to this property, hidden when null via
    /// <c>ObjectConverters.IsNotNull</c>.
    /// </summary>
    [ObservableProperty] private string? _lastErrorMessage;

    /// <summary>
    /// Monitors an <see cref="IAsyncRelayCommand"/> for unhandled exceptions. When the
    /// command's <c>ExecutionTask</c> faults, logs the error via <see cref="App.Logger"/>
    /// (which surfaces it in the notification banner) and sets <see cref="LastErrorMessage"/>
    /// so the user sees inline feedback.
    ///
    /// <para>Call once per command, typically at the end of the constructor, for any async
    /// command whose body does not have its own try-catch. Commands that already catch and
    /// set <c>LastErrorMessage</c> internally do not need this.</para>
    /// </summary>
    /// <param name="command">The async relay command to monitor.</param>
    /// <param name="context">Optional context string for the log entry (e.g. "Save section").</param>
    protected void WatchCommandErrors(IAsyncRelayCommand command, string? context = null)
    {
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IAsyncRelayCommand.ExecutionTask)) return;
            var task = command.ExecutionTask;
            if (task is { IsFaulted: true, Exception: { } ex })
            {
                var inner = ex.InnerException ?? ex;
                LastErrorMessage = inner.Message;
                App.Logger.LogError(inner, context ?? "Command failed");
            }
        };
    }
}
