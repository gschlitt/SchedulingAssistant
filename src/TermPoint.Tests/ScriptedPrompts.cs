using TermPoint.Services;
using TermPoint.ViewModels;

namespace TermPoint.Tests;

/// <summary>The kind of question or notice a <see cref="PromptCall"/> was.</summary>
internal enum PromptKind
{
    /// <summary>A one-button message (<see cref="ILifecyclePrompts.ShowMessageAsync"/>).</summary>
    Message,

    /// <summary>A two-button choice (<see cref="ILifecyclePrompts.ShowChoiceAsync"/>).</summary>
    Choice,

    /// <summary>Work run under a wait dialog (<see cref="ILifecyclePrompts.RunWithWaitAsync{T}"/>).</summary>
    Wait,

    /// <summary>The save-file picker (<see cref="ILifecyclePrompts.PickSaveFileAsync"/>).</summary>
    PickSaveFile,

    /// <summary>The cancellable stale-lock wait (<see cref="ILifecyclePrompts.ShowStaleLockWaitAsync"/>).</summary>
    StaleLockWait,

    /// <summary>The database chooser (<see cref="ILifecyclePrompts.ChooseDatabaseAsync"/>).</summary>
    Chooser
}

/// <summary>
/// One call the lifecycle made to <see cref="ILifecyclePrompts"/>, as <see cref="ScriptedPrompts"/> recorded it.
/// </summary>
/// <param name="Kind">Which prompt it was.</param>
/// <param name="Title">The dialog title (the picker's title for a picker; the wait dialog's for a wait; empty for the stale-lock wait and the chooser).</param>
/// <param name="Text">
/// The body text for a message, choice or wait; for the picker "suggestedFileName|defaultExtension"; for the
/// chooser "mode|reason|lastKnownPath"; for the stale-lock wait the remaining seconds, rounded down.
/// </param>
/// <param name="Buttons">
/// The button labels: one for a message; "affirmative|negative" for a choice; empty for everything else.
/// </param>
/// <param name="Answer">
/// For a <see cref="PromptKind.Choice"/>: how the "user" answered it (see <see cref="ChoiceAnswer"/>), set once the
/// script's answer is known; null for every other kind of prompt (and briefly null for a choice, before it is answered).
/// </param>
/// <param name="PickedPath">For a <see cref="PromptKind.PickSaveFile"/>: the path the "user" picked, or null when cancelled / not a picker.</param>
internal sealed record PromptCall(
    PromptKind Kind, string Title, string Text, string Buttons,
    ChoiceAnswer? Answer = null, string? PickedPath = null);

/// <summary>
/// How a two-button choice dialog was answered. The real dialog returns <c>true</c> only for its first button;
/// the second button and the window's X both return <c>false</c>, so the lifecycle cannot tell them apart. The
/// harness records the difference anyway, because the X is not a decision: it must never be taken as consent to
/// delete someone's unsaved work (data-persistence review, F1).
/// </summary>
internal enum ChoiceAnswer
{
    /// <summary>The first (affirmative) button was pressed; the dialog returned <c>true</c>.</summary>
    Affirmative,

    /// <summary>The second (negative) button was pressed; the dialog returned <c>false</c>.</summary>
    Negative,

    /// <summary>The window was closed with its X; the dialog returned <c>false</c>, exactly as for the second button.</summary>
    ClosedWithX
}

/// <summary>
/// Thrown by <see cref="ScriptedPrompts"/> when the lifecycle asks something the script did not expect, so an
/// unexpected dialog fails the test with a message that names both what was asked and what was expected.
/// </summary>
internal sealed class UnexpectedPromptException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was asked and what the script expected instead.</param>
    public UnexpectedPromptException(string message) : base(message) { }
}

/// <summary>
/// A scripted stand-in for <see cref="ILifecyclePrompts"/> (spec item 9.1, step 9.1b): it answers from an ordered
/// script the test sets up, RECORDS every call in order as a <see cref="PromptCall"/>, and throws
/// <see cref="UnexpectedPromptException"/> the moment the lifecycle asks something the next script step does not
/// describe (wrong kind, wrong title, or no step left). Because the lifecycle may swallow an exception thrown from
/// a dialog, every violation is also kept in <see cref="Violations"/>, which the test class asserts empty at the
/// end of each test.
///
/// <para>Closing a choice dialog with the window's X is modelled as answering <c>false</c> (that is exactly what
/// the production dialog returns). A test that wants "X" scripts <see cref="ExpectChoice(string, ChoiceAnswer, string?, Action?)"/>
/// with <see cref="ChoiceAnswer.ClosedWithX"/>; the kind of answer is recorded in <see cref="PromptCall.Answer"/>.</para>
/// </summary>
internal sealed class ScriptedPrompts : ILifecyclePrompts
{
    /// <summary>One step of the script.</summary>
    /// <param name="Kind">The kind of prompt expected.</param>
    /// <param name="Title">The exact title expected, or null to accept any.</param>
    /// <param name="Text">The exact body text expected, or null to accept any.</param>
    /// <param name="Answer">The answer to give: <see cref="ChoiceAnswer"/> (choice), bool (stale wait), string? (picker, chooser), or null.</param>
    /// <param name="Before">Optional action run when the prompt is asked, before the answer is given.</param>
    private sealed record Step(PromptKind Kind, string? Title, string? Text, object? Answer, Action? Before);

    /// <summary>The steps still to be consumed, in order.</summary>
    private readonly Queue<Step> _script = new();

    /// <summary>Where each call is also written (the test's shared, ordered trace), or null.</summary>
    private readonly Action<string>? _trace;

    /// <summary>Every call the lifecycle made, in order.</summary>
    public List<PromptCall> Calls { get; } = new();

    /// <summary>
    /// Called once for every prompt that matched the script, right after its answer is known (and recorded in
    /// <see cref="Calls"/>) and BEFORE the step's <c>before</c> action runs. The multi-computer harness uses it to
    /// write the dialog into its story and to feed the invariants; null for none.
    /// </summary>
    public Action<PromptCall>? OnCall { get; set; }

    /// <summary>Every unexpected prompt, described; empty when the lifecycle only asked what the script said.</summary>
    public List<string> Violations { get; } = new();

    /// <summary>How many script steps have not been consumed yet.</summary>
    public int Remaining => _script.Count;

    /// <summary>Creates the fake.</summary>
    /// <param name="trace">Receives a line "prompt:Kind:Title" for every call, so it can be ordered against host calls; may be null.</param>
    public ScriptedPrompts(Action<string>? trace = null) => _trace = trace;

    // ── Script builders ───────────────────────────────────────────────────────

    /// <summary>Expects a one-button message.</summary>
    /// <param name="title">The exact title.</param>
    /// <param name="text">The exact body text, or null to accept any.</param>
    /// <param name="before">Optional action run when the message is shown.</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectMessage(string title, string? text = null, Action? before = null)
    {
        _script.Enqueue(new Step(PromptKind.Message, title, text, null, before));
        return this;
    }

    /// <summary>Expects a two-button choice and answers it (the original, simple form: true is Affirmative, false is Negative).</summary>
    /// <param name="title">The exact title.</param>
    /// <param name="answer">True for the affirmative button; false for the negative button (recorded as <see cref="ChoiceAnswer.Negative"/>; use the <see cref="ChoiceAnswer"/> overload for the window's X).</param>
    /// <param name="text">The exact body text, or null to accept any.</param>
    /// <param name="before">Optional action run when the choice is shown (for example, to advance a clock).</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectChoice(string title, bool answer, string? text = null, Action? before = null)
        => ExpectChoice(title, answer ? ChoiceAnswer.Affirmative : ChoiceAnswer.Negative, text, before);

    /// <summary>
    /// Expects a two-button choice and answers it precisely. <see cref="ChoiceAnswer.Negative"/> and
    /// <see cref="ChoiceAnswer.ClosedWithX"/> both make the dialog return <c>false</c> (exactly what the real dialog
    /// returns); the kind is RECORDED with the call so a checker can tell a decision from a dismissal.
    /// </summary>
    /// <param name="title">The exact title.</param>
    /// <param name="answer">How the "user" answers.</param>
    /// <param name="text">The exact body text, or null to accept any.</param>
    /// <param name="before">Optional action run when the choice is shown (for example, to advance a clock).</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectChoice(string title, ChoiceAnswer answer, string? text = null, Action? before = null)
    {
        _script.Enqueue(new Step(PromptKind.Choice, title, text, answer, before));
        return this;
    }

    /// <summary>Expects work run under a wait dialog; the work then runs, and its result is returned to the caller.</summary>
    /// <param name="title">The exact dialog title.</param>
    /// <param name="text">The exact body text, or null to accept any.</param>
    /// <param name="before">Optional action run when the dialog is shown, before the work starts (to observe the world at that moment).</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectWait(string title, string? text = null, Action? before = null)
    {
        _script.Enqueue(new Step(PromptKind.Wait, title, text, null, before));
        return this;
    }

    /// <summary>Expects the save-file picker and answers it.</summary>
    /// <param name="title">The exact picker title.</param>
    /// <param name="chosenPath">The path the "user" picks, or null for a cancelled picker.</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectPick(string title, string? chosenPath)
    {
        _script.Enqueue(new Step(PromptKind.PickSaveFile, title, null, chosenPath, null));
        return this;
    }

    /// <summary>Expects the stale-lock wait dialog and answers it.</summary>
    /// <param name="completed">True when the wait ran to the end; false when the user cancelled.</param>
    /// <param name="before">
    /// Optional action run when the wait is shown, before the answer is given (the harness advances its fake clock
    /// here, because a real wait would let that much time pass).
    /// </param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectStaleLockWait(bool completed, Action? before = null)
    {
        _script.Enqueue(new Step(PromptKind.StaleLockWait, null, null, completed, before));
        return this;
    }

    /// <summary>Expects the database chooser and answers it.</summary>
    /// <param name="chosenPath">The database the "user" picks, or null for Exit / Create New.</param>
    /// <param name="text">The exact "mode|reason|lastKnownPath" expected, or null to accept any.</param>
    /// <returns>This instance, for chaining.</returns>
    public ScriptedPrompts ExpectChooser(string? chosenPath, string? text = null)
    {
        _script.Enqueue(new Step(PromptKind.Chooser, null, text, chosenPath, null));
        return this;
    }

    // ── ILifecyclePrompts ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ShowMessageAsync(string title, string message, string buttonLabel = "OK")
    {
        Take(new PromptCall(PromptKind.Message, title, message, buttonLabel));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> ShowChoiceAsync(string title, string message, string affirmative, string negative)
    {
        var step = Take(new PromptCall(PromptKind.Choice, title, message, affirmative + "|" + negative));
        return Task.FromResult((ChoiceAnswer)step.Answer! == ChoiceAnswer.Affirmative);
    }

    /// <inheritdoc/>
    public async Task<T> RunWithWaitAsync<T>(string title, string message, Func<Task<T>> work)
    {
        Take(new PromptCall(PromptKind.Wait, title, message, ""));
        return await work();
    }

    /// <inheritdoc/>
    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string defaultExtension)
    {
        var step = Take(new PromptCall(PromptKind.PickSaveFile, title, suggestedFileName + "|" + defaultExtension, ""));
        return Task.FromResult((string?)step.Answer);
    }

    /// <inheritdoc/>
    public Task<bool> ShowStaleLockWaitAsync(TimeSpan remaining)
    {
        var step = Take(new PromptCall(PromptKind.StaleLockWait, "", ((int)remaining.TotalSeconds).ToString(), ""));
        return Task.FromResult((bool)step.Answer!);
    }

    /// <inheritdoc/>
    public Task<string?> ChooseDatabaseAsync(ChooserMode mode, RecoveryReason? reason, string? lastKnownPath)
    {
        var step = Take(new PromptCall(PromptKind.Chooser, "", $"{mode}|{reason}|{lastKnownPath}", ""));
        return Task.FromResult((string?)step.Answer);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the call, then checks it against the next script step: wrong kind, wrong title, wrong text or an
    /// empty script is recorded in <see cref="Violations"/> and thrown as <see cref="UnexpectedPromptException"/>.
    /// </summary>
    /// <param name="call">What the lifecycle asked.</param>
    /// <returns>The matching step (its answer is the caller's to give).</returns>
    /// <exception cref="UnexpectedPromptException">The call is not what the script expected.</exception>
    private Step Take(PromptCall call)
    {
        Calls.Add(call);
        _trace?.Invoke($"prompt:{call.Kind}:{call.Title}");

        if (!_script.TryPeek(out var step))
            throw Violation($"unexpected prompt (script is empty): {Describe(call)}");

        _script.Dequeue();
        if (step.Kind != call.Kind
            || (step.Title is not null && step.Title != call.Title)
            || (step.Text is not null && step.Text != call.Text))
        {
            throw Violation(
                $"unexpected prompt: {Describe(call)}; the script expected {step.Kind} " +
                $"title='{step.Title ?? "(any)"}' text='{step.Text ?? "(any)"}'");
        }

        // Record the answer with the call, so the kind of answer survives: the lifecycle only ever sees a bool.
        var answered = call.Kind switch
        {
            PromptKind.Choice       => call with { Answer = (ChoiceAnswer)step.Answer! },
            PromptKind.PickSaveFile => call with { PickedPath = (string?)step.Answer },
            _                       => call
        };
        Calls[^1] = answered;
        OnCall?.Invoke(answered);

        step.Before?.Invoke();
        return step;
    }

    /// <summary>Records a violation and returns the exception to throw for it.</summary>
    /// <param name="message">The description.</param>
    private UnexpectedPromptException Violation(string message)
    {
        Violations.Add(message);
        return new UnexpectedPromptException(message);
    }

    /// <summary>Formats a call for a failure message.</summary>
    /// <param name="c">The call.</param>
    private static string Describe(PromptCall c)
        => $"{c.Kind} title='{c.Title}' text='{c.Text}' buttons='{c.Buttons}'";
}
