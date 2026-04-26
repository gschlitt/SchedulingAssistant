namespace SchedulingAssistant.Services;

/// <summary>
/// No-op <see cref="IDialogService"/> for environments where native window dialogs
/// are unavailable (e.g. the WASM browser demo build).
/// <see cref="Confirm"/> always returns <c>true</c>; <see cref="ShowError"/> is silent.
/// </summary>
public sealed class NullDialogService : IDialogService
{
    /// <inheritdoc/>
    public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        => Task.FromResult(true);

    /// <inheritdoc/>
    public Task ShowError(string message)
        => Task.CompletedTask;
}
