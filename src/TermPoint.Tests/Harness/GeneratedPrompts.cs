namespace TermPoint.Tests.Harness;

/// <summary>
/// The dialogs of a generated run (spec item 9.3, step 9.3c). A hand-written walk scripts every answer before the action
/// that raises the dialog; a generated run cannot, so each computer's <see cref="ScriptedPrompts"/> asks this policy
/// instead, which asks the run's decision source. The call is recorded exactly as a scripted one would be, so the story,
/// the findings and the promise tracker work unchanged.
///
/// <para><b>The choice points.</b></para>
/// <list type="bullet">
/// <item><description>A two-button choice: <c>Affirmative</c> (the first button), <c>Negative</c> (the second) or <c>X</c> (the
/// window closed). The X is a separate answer because it is not a decision: the lifecycle takes it for the second button,
/// and the harness must see the difference (finding F1).</description></item>
/// <item><description>The save-file picker: <c>pick</c> (a fresh path under the world's scratch folder) or <c>cancel</c>.</description></item>
/// <item><description>The stale-lock wait: <c>completed</c> (the confirmation time passes on the fake clock) or <c>cancelled</c>.</description></item>
/// <item><description>The database chooser (shown when nothing could be opened): <c>exit</c>, or one of the two databases.</description></item>
/// <item><description>A message or a wait dialog has nothing to decide.</description></item>
/// </list>
///
/// <para><b>Dialog storms.</b> Some lifecycle loops ask again after a refusal ("Try again", a cancelled picker). A random
/// source would not always leave them, so after <see cref="RunContext.MaxDialogsPerEvent"/> dialogs in one event every
/// further dialog gets its terminal answer without asking the source (second button, a picked path, a completed wait, Exit).
/// The rule depends only on the count, so a replay behaves identically.</para>
/// </summary>
internal static class GeneratedPrompts
{
    /// <summary>The choices of a two-button dialog, simplest first.</summary>
    public static readonly string[] ChoiceOptions = { "Affirmative", "Negative", "X" };

    /// <summary>The choices of the save-file picker, simplest first.</summary>
    public static readonly string[] PickerOptions = { "pick", "cancel" };

    /// <summary>The choices of the stale-lock wait, simplest first.</summary>
    public static readonly string[] WaitOptions = { "completed", "cancelled" };

    /// <summary>The choices of the database chooser, simplest first.</summary>
    public static readonly string[] ChooserOptions = { "exit", "dept.db", "other.db" };

    /// <summary>Builds the policy for a run.</summary>
    /// <param name="ctx">The run, whose decision source answers and whose world supplies paths and the clock.</param>
    /// <returns>The policy to put in <see cref="World.PromptPolicy"/>.</returns>
    public static Func<Computer, PromptCall, PromptAnswer> Create(RunContext ctx)
        => (computer, call) => Answer(ctx, computer, call);

    /// <summary>Answers one dialog.</summary>
    /// <param name="ctx">The run.</param>
    /// <param name="computer">The computer that was asked.</param>
    /// <param name="call">The dialog.</param>
    /// <returns>The answer.</returns>
    private static PromptAnswer Answer(RunContext ctx, Computer computer, PromptCall call)
    {
        var world = ctx.World;
        var force = ctx.CountDialogAndShouldForce();
        var label = $"{computer.Name}: {(string.IsNullOrEmpty(call.Title) ? call.Kind.ToString() : call.Title)}";

        switch (call.Kind)
        {
            case PromptKind.Choice:
            {
                var picked = force ? "Negative" : ctx.Decide(label, ChoiceOptions);
                return new PromptAnswer(picked switch
                {
                    "Affirmative" => ChoiceAnswer.Affirmative,
                    "X"           => ChoiceAnswer.ClosedWithX,
                    _             => ChoiceAnswer.Negative
                });
            }

            case PromptKind.PickSaveFile:
            {
                var picked = force ? "pick" : ctx.Decide(label, PickerOptions);
                return new PromptAnswer(picked == "pick" ? world.NextSavedCopyPath() : null);
            }

            case PromptKind.StaleLockWait:
            {
                var picked = force ? "completed" : ctx.Decide(label, WaitOptions);
                if (picked != "completed") return new PromptAnswer(false);

                // A real wait lets the confirmation time pass; the fake clock is moved instead.
                var seconds = int.TryParse(call.Text, out var remaining) ? remaining : 0;
                return new PromptAnswer(true, () => world.AdvanceTime(TimeSpan.FromSeconds(seconds + 1)));
            }

            case PromptKind.Chooser:
            {
                var picked = force ? "exit" : ctx.Decide(label, ChooserOptions);
                return new PromptAnswer(picked switch
                {
                    "dept.db"  => world.DatabasePath,
                    "other.db" => world.EnsureOtherDatabase(),
                    _          => null
                });
            }

            default:   // a message or a wait dialog: nothing to decide
                return new PromptAnswer(null);
        }
    }
}
