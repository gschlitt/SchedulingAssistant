using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// What the close question reads about the open database (design: <c>designs/1.2.5/undo.md</c>, story 5).
/// </summary>
/// <param name="HasUnsavedChanges">True when something is unsaved (<see cref="SectionHistory.HasUnsavedChanges"/>).</param>
/// <param name="LastSaveTime">When the user last saved (<see cref="SectionHistory.LastSaveTime"/>).</param>
/// <param name="SavedThisSession">False when nothing has been saved since the database was opened, so there is no save time to name.</param>
/// <param name="CardHeading">The heading of an existing section's card that has unapplied changes; null otherwise.</param>
public sealed record UnsavedState(bool HasUnsavedChanges, DateTime LastSaveTime, bool SavedThisSession, string? CardHeading);

/// <summary>How the close question was answered.</summary>
public enum UnsavedChangesAnswer
{
    /// <summary>Nothing unsaved and no editor with changes: nothing was asked; carry on as before.</summary>
    NothingToAsk,

    /// <summary>Save: every open editor's changes were applied; carry on and save.</summary>
    Save,

    /// <summary>Don't Save: every open editor's changes were dropped; carry on and throw the unsaved changes away.</summary>
    DontSave,

    /// <summary>Cancel, the window's X, an editor that refused to apply, or the session stopped being a writer: stop.</summary>
    Stop
}

/// <summary>
/// The one question asked before a close, a database switch or a restore when anything is unsaved or an open editor
/// holds unapplied changes: "Save Changes?" with Save, Don't Save and Cancel. It replaces the earlier "Unapplied
/// Changes" question, folding the editor into one question (rule 4: one message per problem). Holds only the decision;
/// what Save and Don't Save then do to the session is <see cref="SessionLifecycle"/>'s.
/// </summary>
public static class UnsavedChangesGate
{
    /// <summary>
    /// Asks the question when there is something to ask, and carries out the editor part of the answer.
    /// <list type="bullet">
    /// <item><description>Not a writer, or nothing unsaved and no editor with changes: nothing is asked.</description></item>
    /// <item><description><b>Save</b>: applies each open editor with changes through its normal Apply (validation
    /// included); a refusal stops (the editor shows its own error line), as does the same editor still holding changes
    /// after an Apply it accepted.</description></item>
    /// <item><description><b>Don't Save</b>: drops each open editor's changes.</description></item>
    /// <item><description>Either answer then stops if the session is no longer a writer (the lock was lost while the
    /// question showed): the lost-lock path keeps the changes, exactly as today, and its banner explains.</description></item>
    /// </list>
    /// </summary>
    /// <param name="title">The question's title (<see cref="HistoryText.CloseTitle"/>, <see cref="HistoryText.SwitchTitle"/> or <see cref="HistoryText.RestoreTitle"/>).</param>
    /// <param name="state">The open database's unsaved state; null when none is open.</param>
    /// <param name="findEditor">Finds an open editor with unapplied changes, or null.</param>
    /// <param name="prompts">Where the three-button question is shown.</param>
    /// <param name="isWriter">True while the session holds the write lock and is not ending; read before and after the question.</param>
    /// <returns>The answer (see <see cref="UnsavedChangesAnswer"/>).</returns>
    public static async Task<UnsavedChangesAnswer> ResolveAsync(
        string title, UnsavedState? state, Func<IUnappliedChanges?> findEditor, ILifecyclePrompts prompts,
        Func<bool> isWriter)
    {
        if (!isWriter()) return UnsavedChangesAnswer.NothingToAsk;

        var editor = findEditor();
        var unsaved = state?.HasUnsavedChanges == true;
        if (!unsaved && editor is null) return UnsavedChangesAnswer.NothingToAsk;

        var answer = await prompts.ShowThreeChoiceAsync(
            title, Body(state, editor is not null),
            HistoryText.SaveButton, HistoryText.DontSaveButton, HistoryText.CancelButton);

        switch (answer)
        {
            case ThreeChoiceResult.First:
                IUnappliedChanges? previous = null;
                while (findEditor() is { } next)
                {
                    if (ReferenceEquals(next, previous)) return UnsavedChangesAnswer.Stop;
                    if (!await next.TryApplyAsync()) return UnsavedChangesAnswer.Stop;
                    previous = next;
                }
                return isWriter() ? UnsavedChangesAnswer.Save : UnsavedChangesAnswer.Stop;

            case ThreeChoiceResult.Second:
                if (!isWriter()) return UnsavedChangesAnswer.Stop;
                IUnappliedChanges? dropped = null;
                while (findEditor() is { } next && !ReferenceEquals(next, dropped))
                {
                    next.DiscardUnapplied();
                    dropped = next;
                }
                return UnsavedChangesAnswer.DontSave;

            default:
                return UnsavedChangesAnswer.Stop;
        }
    }

    /// <summary>
    /// The question's body: the unsaved sentence (with the last save's time when there was a save this session), plus
    /// the card or editor sentence; or the card or editor sentence alone when nothing else is unsaved.
    /// </summary>
    /// <param name="state">The unsaved state; null counts as nothing unsaved.</param>
    /// <param name="editorHasChanges">True when an open editor has unapplied changes.</param>
    /// <returns>The body text.</returns>
    public static string Body(UnsavedState? state, bool editorHasChanges)
    {
        var heading = editorHasChanges ? state?.CardHeading : null;
        if (state?.HasUnsavedChanges != true)
            return heading is not null ? HistoryText.CloseBodyCardOnly(heading) : HistoryText.CloseBodyEditorOnly;

        var body = state.SavedThisSession ? HistoryText.CloseBody(state.LastSaveTime) : HistoryText.CloseBodyNotSavedYet;
        if (!editorHasChanges) return body;
        return body + " " + (heading is not null ? HistoryText.CloseCardSentence(heading) : HistoryText.CloseEditorSentence);
    }
}
