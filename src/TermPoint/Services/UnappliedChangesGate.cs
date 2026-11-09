using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// The one reusable decision behind "you have unapplied changes" (spec item 3.1), asked before a close or a
/// database switch. Holds all the behaviour so it is testable without any window: who is asked, what each answer
/// does, and when the answer lets the close or switch carry on.
/// </summary>
public static class UnappliedChangesGate
{
    /// <summary>
    /// Asks the user what to do with <paramref name="editor"/>'s unapplied changes, and does it.
    /// <list type="bullet">
    /// <item><description>No question, and carry on, when there is no editor, or the session is not a writer (a read-only session cannot save anything anyway).</description></item>
    /// <item><description><b>Apply</b>: runs the editor's normal validation and Apply. Carries on only if it succeeded and the session is still a writer afterwards; a refusal stops the close or switch (the editor shows its own error line, no extra message).</description></item>
    /// <item><description><b>Discard</b>: drops the editor's changes and carries on.</description></item>
    /// <item><description><b>Cancel</b>, or the window's X: stops; nothing changes.</description></item>
    /// </list>
    /// </summary>
    /// <param name="editor">The open editor with unapplied changes, or null when there is none.</param>
    /// <param name="isWriter">True when this session holds the write lock and is not ending; false means no question is asked.</param>
    /// <param name="prompts">Where the three-button question is shown.</param>
    /// <param name="isStillWriter">
    /// Optional. Asked again after a successful Apply: false (the session went read-only while the question was
    /// showing, so the apply cannot be saved) turns the Apply into a refusal. Null means "still a writer".
    /// </param>
    /// <returns>True to carry on with the close or switch; false to stop.</returns>
    public static async Task<bool> ResolveAsync(
        IUnappliedChanges? editor, bool isWriter, ILifecyclePrompts prompts, Func<bool>? isStillWriter = null)
    {
        if (editor is null || !isWriter) return true;

        var answer = await prompts.ShowThreeChoiceAsync(
            UnappliedChangesPrompt.Title, UnappliedChangesPrompt.Message,
            UnappliedChangesPrompt.ApplyButton, UnappliedChangesPrompt.DiscardButton, UnappliedChangesPrompt.CancelButton);

        switch (answer)
        {
            case ThreeChoiceResult.First:
                // The editor's own validation text is the explanation if it refuses, so no message is shown here.
                if (!await editor.TryApplyAsync()) return false;
                return isStillWriter?.Invoke() ?? true;

            case ThreeChoiceResult.Second:
                editor.DiscardUnapplied();
                return true;

            default:
                return false;
        }
    }
}
