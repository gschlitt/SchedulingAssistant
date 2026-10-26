namespace TermPoint.Services;

/// <summary>
/// Result of <c>CheckoutService.RefreshReadOnlySnapshotAsync</c>. Lives in its own file (it used to sit at the bottom of
/// <c>CheckoutService.cs</c>) because <see cref="RefreshPrompt"/> and <c>MainWindowViewModel</c> compile on both targets
/// (desktop and browser) while <c>CheckoutService</c> compiles on the desktop only.
/// </summary>
public enum RefreshOutcome
{
    /// <summary>D'' was successfully re-copied from D.</summary>
    Updated,
    /// <summary>D could not be reached or the copy hash never matched; D'' is unchanged.</summary>
    SourceUnavailable,
}
