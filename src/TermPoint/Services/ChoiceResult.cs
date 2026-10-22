namespace TermPoint.Services;

/// <summary>
/// How a two-button choice dialog ended. The plain <c>bool</c> dialogs (<c>ILifecyclePrompts.ShowChoiceAsync</c>)
/// fold the window's X into the negative button; this enum keeps them apart, for the few questions where the X must
/// never be taken as a decision (data-persistence review, F1: the X on the crash-recovery questions used to delete
/// the only copy of the user's unsaved work).
/// </summary>
public enum ChoiceResult
{
    /// <summary>The first (affirmative) button was pressed.</summary>
    Affirmative,

    /// <summary>The second (negative) button was pressed.</summary>
    Negative,

    /// <summary>The window was closed with its X (or otherwise dismissed) without pressing either button: not a decision.</summary>
    Closed
}
