using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// The first of the two seams between the database-session lifecycle (open, switch, leave, lost lock, kept and
/// stranded work) and the window it runs in (spec item 9.1): everything the lifecycle uses to ASK or TELL the
/// user. <see cref="MainWindow"/> implements it with the modal dialogs it already has; a test supplies a scripted
/// fake that answers from a script and records every title and text.
///
/// <para>Every member runs on the UI thread and completes only when the user has answered or the work has
/// ended, exactly as the dialog helpers it wraps do today.</para>
/// </summary>
public interface ILifecyclePrompts
{
    /// <summary>
    /// Shows a modal one-button message dialog and completes when the user dismisses it.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="buttonLabel">Label of the single button; "OK" unless a message calls for another word (for example "Close").</param>
    /// <returns>A task that completes when the dialog has closed.</returns>
    Task ShowMessageAsync(string title, string message, string buttonLabel = "OK");

    /// <summary>
    /// Shows a modal two-button dialog with caller-supplied button labels (for example "Restore" / "Discard").
    /// Closing the dialog without pressing a button (the window's X) counts as the negative answer. A question
    /// whose negative answer destroys something must use <see cref="ShowChoiceOrCloseAsync"/> instead.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button (answers true).</param>
    /// <param name="negative">Label of the declining button (answers false).</param>
    /// <returns>True when the affirmative button was clicked; false for the negative button OR for closing the dialog without a button.</returns>
    Task<bool> ShowChoiceAsync(string title, string message, string affirmative, string negative);

    /// <summary>
    /// Shows the same modal two-button dialog as <see cref="ShowChoiceAsync"/>, but reports a window closed with
    /// its X as <see cref="ChoiceResult.Closed"/> instead of folding it into the negative button. For questions
    /// where the negative answer destroys something and the X must therefore never count as consent (the
    /// crash-recovery questions: closing the window must not delete the user's only copy of their unsaved work).
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button.</param>
    /// <param name="negative">Label of the declining button.</param>
    /// <returns><see cref="ChoiceResult.Affirmative"/>, <see cref="ChoiceResult.Negative"/>, or <see cref="ChoiceResult.Closed"/> when the dialog was closed without a button.</returns>
    Task<ChoiceResult> ShowChoiceOrCloseAsync(string title, string message, string affirmative, string negative);

    /// <summary>
    /// Runs <paramref name="work"/> under a modal wait dialog that the user cannot dismiss until the work ends.
    /// The work is started before the dialog is shown and is always awaited to completion, even if the dialog
    /// itself fails to show.
    /// </summary>
    /// <typeparam name="T">The type the work produces.</typeparam>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog body text.</param>
    /// <param name="work">The work to run; started immediately on the calling (UI) thread.</param>
    /// <returns>Whatever <paramref name="work"/> returns.</returns>
    /// <exception cref="Exception">Any exception <paramref name="work"/> throws is rethrown after the dialog has closed.</exception>
    Task<T> RunWithWaitAsync<T>(string title, string message, Func<Task<T>> work);

    /// <summary>
    /// Shows the operating system's "save file" picker for a new database file. The picker's own overwrite
    /// prompt is OFF (an existing file is never replaced; the caller refuses it and asks again).
    /// </summary>
    /// <param name="title">Picker title.</param>
    /// <param name="suggestedFileName">File name offered to the user.</param>
    /// <param name="defaultExtension">Extension added when the user types none, without the dot (for example "db").</param>
    /// <returns>The chosen full local path, or null when the user cancelled or the choice has no local path.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string defaultExtension);

    /// <summary>
    /// Shows the cancellable wait dialog that precedes taking over a lock that looks stale. Closing the dialog
    /// cancels the wait.
    /// </summary>
    /// <param name="remaining">How long the wait will take at most; shown to the user in seconds.</param>
    /// <returns>True when the wait ran to the end and the takeover may proceed; false when the user cancelled (treat exactly like "Open read-only").</returns>
    Task<bool> ShowStaleLockWaitAsync(TimeSpan remaining);

    /// <summary>
    /// Shows the database chooser window and waits for the user to make a selection.
    ///
    /// <para><b>Side effects (unchanged from today):</b> a choice is saved to the application settings; the user
    /// may instead be routed to the first-run wizard, or the application may be shut down when the user
    /// exits. In both of those cases the result is null.</para>
    /// </summary>
    /// <param name="mode">Normal chooser or recovery mode.</param>
    /// <param name="reason">Why recovery was triggered (null in Normal mode).</param>
    /// <param name="lastKnownPath">The path from settings, shown in recovery mode.</param>
    /// <returns>The resolved database path, or null if the user exited or chose to start the wizard.</returns>
    Task<string?> ChooseDatabaseAsync(ChooserMode mode, RecoveryReason? reason, string? lastKnownPath);
}
