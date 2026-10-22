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

    /// <summary>
    /// The confirmation that follows Discard on either recovery question (data-persistence review, F1, fixed 2026-10-21):
    /// [Discard] really deletes the kept changes, [Go Back] (or the window's X) asks the original question again.
    /// </summary>
    public const string DiscardUnsavedConfirm = RecoveryConflictPrompt.DiscardConfirmTitle;
}

/// <summary>
/// Maps a <see cref="Violation"/> to a finding id of the data-persistence review (section 3, F1 to F16) where a rule
/// applies, or to <see cref="NewFinding"/> when none does. A walk declares the findings it expects
/// (<see cref="World.ExpectFindings"/>); the set that occurs must equal the declared set.
///
/// <para>The rules:</para>
/// <list type="bullet">
/// <item><description><b>F1</b> (fixed 2026-10-21, 1.2.3: the X on either recovery question asks it again, and Discard asks
/// "Discard Unsaved Changes?" first, so the rule stays as the regression detector): an edit was lost in a step in which its owner
/// answered "Restore Unsaved Changes?" or "Can't Restore Unsaved Changes" with the second button or the window's X and the
/// lifecycle acted on it at once: the second button was not followed by the "Discard Unsaved Changes?" confirmation, or the X was
/// not followed by the same question again. A confirmed discard is a decision (the confirmation records it, see
/// <see cref="World.ConfirmedDiscards"/>) and a Go Back or an X that was followed up loses nothing.</description></item>
/// <item><description><b>F5</b>: the same refusal as F7, but the shared database's set of edits had not changed since the
/// computer's work was kept: nobody changed any data, only the file's bytes changed (every editing session rewrites the
/// database at its first save, even with no edits).</description></item>
/// <item><description><b>F6</b>: a "will offer to restore" promise was broken.</description></item>
/// <item><description><b>F7</b>: "Restore Unsaved Changes?" was answered Restore and was followed by "Can't Restore Unsaved Changes" because someone changed data.</description></item>
/// <item><description><b>F8</b> (fixed 2026-10-21, 1.2.3: the save records its fingerprint in the marker before the rename, so
/// the rule stays as the regression detector): the same refusal, but every edit the computer made that sits in its kept working
/// file is already in the shared database: its own save landed, and the power went before the unsaved-changes marker was
/// deleted. Nothing is lost, yet the person is offered a restore and then refused (step 9.3c-2a).</description></item>
/// <item><description><b>F12</b> (fixed 2026-10-21, 1.2.3: the save re-checks the lock and D just before the rename, so the rules
/// stay as the regression detector): the lock was not the saver's (or the database was not the version it last saw) at the
/// moment of the save's rename (invariant I4, observed at <c>Save.AfterFinalCheck</c>); and, in the same step, an I1 violation
/// whose lost committed edits another computer committed while the save was held before its rename (the rename landed over
/// them). Step 9.3c-2b.</description></item>
/// <item><description><b>F2</b> (spec item 15): an edit was lost whose computer applied it while its own operation was held in the
/// edit-loss window (<see cref="PausePoints.EditLossWindow"/>: from a save's snapshot to the cleanup of the working copy). It
/// missed the save, and what protected it was thrown away when the operation ended: the save's last step deleted the marker and the
/// computer then crashed before another save landed, or, for a close or a switch, the working copy itself was cleaned up. Step 9.3c-2b.</description></item>
/// <item><description><b>F16</b>: any I6 violation: a read-only session holds its database's lock, or a lost-lock demotion
/// carried on against a database other than the one it started on. Step 9.3c-2b.</description></item>
/// </list>
/// </summary>
internal static class Findings
{
    /// <summary>The pseudo finding for a violation no rule explains: a possible new finding to investigate.</summary>
    public const string NewFinding = "new";

    /// <summary>Gives a violation its finding id.</summary>
    /// <param name="violation">The violation to classify.</param>
    /// <returns>"F1", "F2", "F5", "F6", "F7", "F8", "F12", "F16", or <see cref="NewFinding"/>.</returns>
    public static string Classify(Violation violation)
    {
        switch (violation.Kind)
        {
            case ViolationKind.EditLost when DiscardedWithoutConfirmation(violation.ComputerPrompts):
                return "F1";

            // An edit applied while its computer's own operation was held in the edit-loss window (from a save's snapshot to the
            // cleanup of the working copy): it missed the save, and what protected it is thrown away when the operation ends. The
            // save's last step deletes the marker, so a crash before another save lands makes the next start delete the unmarked
            // working file; a close or a switch is followed by the cleanup of the working copy itself (spec item 15, step 9.3c-2b).
            case ViolationKind.EditLost when violation.EditAppliedDuringHeldSave == true:
                return "F2";

            case ViolationKind.RestoreOfferedThenImpossible:
                // Checked first, because it is the more specific reading of the same refusal: the changes are not lost
                // and not stranded, the database already has all of them (the computer's own save landed).
                if (violation.AllKeptEditsAlreadyShared == true) return "F8";

                // The same refusal, but nobody changed any DATA: the database's bytes changed alone (a no-edit save).
                return violation.DataUnchangedSinceKept == true ? "F5" : "F7";

            case ViolationKind.BrokenPromise when violation.Promise == PromiseKind.WillOfferRestore:
                return "F6";

            // Observed at Save.AfterFinalCheck, the true last moment before the rename (after the save's final check of the lock and of
            // D). F12 is fixed: the final check refuses a save that lost its lock or whose D changed during the upload, so this only
            // fires for the microseconds between that check and the rename (or if the final check is ever removed).
            case ViolationKind.WriteWithoutLockOrOnStaleVersion when violation.CrashPoint == "Save.AfterFinalCheck":
                return "F12";

            // The other face of F12: the save's rename landed over what another computer committed while the save was held
            // before the rename, so those committed edits left the shared database (I1) in the same step.
            case ViolationKind.SharedEditsDisappeared when violation.LostEditsCommittedDuringHeldSave == true:
                return "F12";

            // Invariant I6: a read-only session holding its database's lock, or a demotion that carried on against another database.
            case ViolationKind.ReadOnlySessionHoldsLock:
            case ViolationKind.DemotionOnAnotherDatabase:
                return "F16";

            default:
                return NewFinding;
        }
    }

    /// <summary>
    /// True when a recovery question ("Restore Unsaved Changes?" or "Can't Restore Unsaved Changes") was answered with the
    /// second button or the X and the lifecycle did NOT do what the fix requires next: ask "Discard Unsaved Changes?" after
    /// the second button, or ask the same question again after the X. The X is never a decision and Discard is never final
    /// until confirmed, so a negative answer that is not followed up means the kept changes were deleted on an unconfirmed
    /// click (finding F1, which is fixed: this is its regression detector).
    /// </summary>
    /// <param name="prompts">The dialogs the computer was asked in the step.</param>
    private static bool DiscardedWithoutConfirmation(IReadOnlyList<PromptCall> prompts)
    {
        var choices = prompts.Where(p => p.Kind == PromptKind.Choice).ToList();
        for (var i = 0; i < choices.Count; i++)
        {
            var asked = choices[i];
            if (asked.Title != HarnessTitles.RestoreUnsavedChanges && asked.Title != HarnessTitles.CantRestore) continue;

            var next = i + 1 < choices.Count ? choices[i + 1].Title : null;
            if (asked.Answer == ChoiceAnswer.Negative && next != HarnessTitles.DiscardUnsavedConfirm) return true;
            if (asked.Answer == ChoiceAnswer.ClosedWithX && next != asked.Title) return true;
        }
        return false;
    }
}
