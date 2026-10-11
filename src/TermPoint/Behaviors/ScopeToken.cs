namespace TermPoint.Behaviors;

/// <summary>
/// A tiny reusable "scope" object: runs an action exactly once, the first time it is disposed.
/// Behaviours hand one out from methods that open a counted scope (for example
/// <see cref="ListScrollAnchorBehavior.Pause"/> and <see cref="SuppressPopupScrollBehavior.Bypass"/>)
/// so that the caller can write <c>using (...)</c>. Disposing twice is harmless: only the first call
/// runs the action, so a double dispose can never release somebody else's still-open scope.
/// </summary>
internal sealed class ScopeToken : IDisposable
{
    /// <summary>A shared token that does nothing; returned when there is no scope to open.</summary>
    public static readonly ScopeToken None = new(null);

    /// <summary>The action to run on the first dispose; null once it has run (or for <see cref="None"/>).</summary>
    private Action? _onDispose;

    /// <summary>Creates a token that runs <paramref name="onDispose"/> when it is first disposed.</summary>
    /// <param name="onDispose">The release action; null makes a token that does nothing.</param>
    public ScopeToken(Action? onDispose) => _onDispose = onDispose;

    /// <summary>Runs the release action, once. Later calls do nothing.</summary>
    public void Dispose()
    {
        var action = _onDispose;
        _onDispose = null;
        action?.Invoke();
    }
}
