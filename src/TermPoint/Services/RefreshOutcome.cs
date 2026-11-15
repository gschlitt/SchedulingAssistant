namespace TermPoint.Services;

/// <summary>
/// Result of <c>CheckoutService.RefreshReadOnlySnapshotAsync</c>. Lives in its own file (it used to sit at the bottom of
/// <c>CheckoutService.cs</c>) because <see cref="RefreshPrompt"/> and <c>MainWindowViewModel</c> compile on both targets
/// (desktop and browser) while <c>CheckoutService</c> compiles on the desktop only.
/// </summary>
public enum RefreshOutcome
{
    /// <summary>D'' was successfully re-copied from D, and the connection is open on the fresh copy.</summary>
    Updated,
    /// <summary>
    /// D could not be reached or the copy hash never matched; D'' is unchanged. Always means the connection to D'' was
    /// never closed.
    /// </summary>
    SourceUnavailable,
    /// <summary>
    /// The swap failed (the rename, or the callback that closes the connection, threw), and the connection is open
    /// again on the previous data in D''.
    /// </summary>
    ReplaceFailed,
    /// <summary>
    /// The connection was closed for the swap and every reopen attempt failed, or the session ended (a close or a
    /// switch) before a retry. Nothing is open on D''.
    /// </summary>
    ConnectionLost,
}
