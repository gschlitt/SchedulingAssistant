namespace TermPoint.Services;

/// <summary>
/// How a three-button choice dialog (<see cref="ILifecyclePrompts.ShowThreeChoiceAsync"/>) ended. The window's X is
/// reported as <see cref="Third"/>: the third button is the one that backs out ("Cancel"), so closing the window is
/// always the safe answer.
/// </summary>
public enum ThreeChoiceResult
{
    /// <summary>The first button was pressed (for the unapplied-changes question: "Apply").</summary>
    First,

    /// <summary>The second button was pressed ("Discard").</summary>
    Second,

    /// <summary>The third button was pressed, or the window was closed with its X ("Cancel").</summary>
    Third
}
