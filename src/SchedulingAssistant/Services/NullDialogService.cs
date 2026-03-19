namespace SchedulingAssistant.Services;

/// <summary>
/// A no-op <see cref="IDialogService"/> implementation used in environments where native
/// window dialogs are unavailable (e.g. the WASM browser demo build).
///
/// <para><see cref="Confirm"/> always returns <c>true</c> so that any code path that
/// depends on a confirmation result can proceed safely. <see cref="ShowError"/> is a
/// silent no-op — errors are not surfaced to the user but are still captured by the
/// application logger.</para>
/// </summary>
public sealed class NullDialogService : IDialogService
{
    /// <summary>
    /// Immediately returns <c>true</c> without displaying any UI.
    /// </summary>
    /// <param name="message">The confirmation message (ignored).</param>
    /// <param name="confirmLabel">The label for the confirm button (ignored).</param>
    /// <returns>A completed task whose result is always <c>true</c>.</returns>
    public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        => Task.FromResult(true);

    /// <summary>
    /// Swallows the error silently without displaying any UI.
    /// </summary>
    /// <param name="message">The error message (ignored).</param>
    /// <returns>A completed task.</returns>
    public Task ShowError(string message)
        => Task.CompletedTask;
}
