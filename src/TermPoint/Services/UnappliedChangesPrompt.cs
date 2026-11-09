namespace TermPoint.Services;

/// <summary>
/// The wording of the one question asked before a close or a database switch when an open editor holds changes the
/// user has not applied (spec item 3.1). Pure text so a test can pin it; follows the four rules (rule 4: say what
/// happened and what the choices do, in plain words).
/// </summary>
public static class UnappliedChangesPrompt
{
    /// <summary>Dialog title.</summary>
    public const string Title = "Unapplied Changes";

    /// <summary>Dialog body.</summary>
    public const string Message = "You have changes in the open editor that you haven't applied.";

    /// <summary>First button: apply the changes (the editor's normal checks run) and carry on.</summary>
    public const string ApplyButton = "Apply";

    /// <summary>Second button: drop the changes and carry on.</summary>
    public const string DiscardButton = "Discard";

    /// <summary>Third button (and the window's X): stop the close or switch; nothing changes.</summary>
    public const string CancelButton = "Cancel";
}
