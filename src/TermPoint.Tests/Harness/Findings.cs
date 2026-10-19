using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The dialog titles the harness's rules are written against. Titles that production keeps in a shared constant are
/// read from it; the two that production writes inline in <c>SessionLifecycle</c> are repeated here.
/// </summary>
internal static class HarnessTitles
{
    /// <summary>The question asked when a previous session left unsaved changes (written inline in the lifecycle).</summary>
    public const string RestoreUnsavedChanges = "Restore Unsaved Changes?";

    /// <summary>The question asked when the kept changes cannot be restored.</summary>
    public const string CantRestore = RecoveryConflictPrompt.ConflictTitle;

    /// <summary>The confirmation of the conflict banner's "Discard Changes" (spec item 10a).</summary>
    public const string DiscardConfirm = SaveConflictPrompt.DiscardConfirmTitle;
}

/// <summary>
/// Maps a <see cref="Violation"/> to a finding id of the data-persistence review (section 3, F1 to F15) where a rule
/// applies, or to <see cref="NewFinding"/> when none does. A walk declares the findings it expects
/// (<see cref="World.ExpectFindings"/>); the set that occurs must equal the declared set.
///
/// <para>The rules:</para>
/// <list type="bullet">
/// <item><description><b>F1</b>: an edit was lost in a step in which its owner answered "Restore Unsaved Changes?" or
/// "Can't Restore Unsaved Changes" with the second button (Discard, never confirmed today) or the window's X.</description></item>
/// <item><description><b>F6</b>: a "will offer to restore" promise was broken.</description></item>
/// <item><description><b>F7</b>: "Restore Unsaved Changes?" was answered Restore and was followed by "Can't Restore Unsaved Changes".</description></item>
/// <item><description><b>F12</b>: the lock was not the saver's (or the database was not the version it last saw) at the
/// moment of the save's rename.</description></item>
/// </list>
/// </summary>
internal static class Findings
{
    /// <summary>The pseudo finding for a violation no rule explains: a possible new finding to investigate.</summary>
    public const string NewFinding = "new";

    /// <summary>Gives a violation its finding id.</summary>
    /// <param name="violation">The violation to classify.</param>
    /// <returns>"F1", "F6", "F7", "F12", or <see cref="NewFinding"/>.</returns>
    public static string Classify(Violation violation)
    {
        switch (violation.Kind)
        {
            case ViolationKind.EditLost when DiscardedWithoutConfirmation(violation.ComputerPrompts):
                return "F1";

            case ViolationKind.RestoreOfferedThenImpossible:
                return "F7";

            case ViolationKind.BrokenPromise when violation.Promise == PromiseKind.WillOfferRestore:
                return "F6";

            case ViolationKind.WriteWithoutLockOrOnStaleVersion when violation.CrashPoint == "Save.AfterTmpVerify":
                return "F12";

            default:
                return NewFinding;
        }
    }

    /// <summary>
    /// True when one of the dialogs was a recovery question ("Restore Unsaved Changes?" or "Can't Restore Unsaved
    /// Changes") answered with the second button or the X. Neither is a confirmed discard: Discard asks for no
    /// confirmation today and the X is never a decision.
    /// </summary>
    /// <param name="prompts">The dialogs the computer was asked in the step.</param>
    private static bool DiscardedWithoutConfirmation(IReadOnlyList<PromptCall> prompts)
        => prompts.Any(p => p.Kind == PromptKind.Choice
                            && (p.Title == HarnessTitles.RestoreUnsavedChanges || p.Title == HarnessTitles.CantRestore)
                            && p.Answer is ChoiceAnswer.Negative or ChoiceAnswer.ClosedWithX);
}
