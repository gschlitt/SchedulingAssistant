namespace TermPoint.Tests.Harness;

/// <summary>
/// The one central list of findings that are EXPECTED on today's code (spec item 9.3, step 9.3c; data-persistence review,
/// section 3). A generated run that breaks an invariant in a way one of these explains counts it and keeps its smallest
/// story, and carries on: it does not fail. Any violation the classifier (<see cref="Findings"/>) cannot explain is "new"
/// and FAILS the run with its shrunk story.
///
/// <para>As the redesign steps (8.9) fix findings, their ids come out of this list and the harness then proves each is
/// gone: a generated run that meets one again fails as a new violation. F2 needs a pause in the middle of an operation
/// so that another event happens meanwhile (the pause sweep, step 9.3c-2b, finds it). Hand-written walks declare their own
/// findings (<see cref="World.ExpectFindings"/>) and do not read this list.</para>
///
/// <para><b>F1, F8 and F12 are fixed (2026-10-21, 1.2.3)</b> and so are off the list: the X on either recovery question asks it again
/// and Discard asks "Discard Unsaved Changes?" first (F1), the save records its fingerprint in the dirty marker before the rename (F8),
/// and re-checks the lock and D immediately before the rename (F12). Their old routes are kept as proof tests
/// (<see cref="FixedFindingsProofTests"/>) that must now show no violation; the classifier's rules for them stay in
/// <see cref="Findings"/>, so a regression is reported as a new violation.</para>
///
/// <para><b>F19 is fixed (1.2.4, item 4.12)</b> and so is off the list: a lock-file check counts for 2 seconds
/// (<see cref="TermPoint.Services.WriteLockService.LockCheckFreshness"/>), so a heartbeat that was held (the computer slept) between its
/// check and the move onto the lock file repeats the check and moves nothing over a lock another computer took meanwhile. Its old route
/// is a proof test in <see cref="FixedFindingsProofTests"/>. <b>F22 stays</b>: the same 2 second check closes its freeze route, but a
/// delete that is stalled and lands whenever the (simulated) network returns is not closed by it (see its description).</para>
///
/// <para><b>F16, F17 and F23 are fixed (1.2.4, the close/switch sequence, <c>designs/close-switch-sequence.md</c>)</b> and so are off the
/// list. F16 (a lost-lock demotion still running during a switch landed on the new session): the checkout has a session id, the release
/// waits for a running demotion, and a demotion or a session-bound post stops or is dropped when its session has ended. F17 (a banner AND
/// "Changes Not Saved" for one close or switch save problem): while the checkout is ending, the final save's banners and lock loss are not
/// delivered, so only the dialog is shown. F23 (a "changes are kept" banner after the person's Discard): the post-open save runs only when
/// the open really restored kept changes. Their old routes are proof tests in <see cref="FixedFindingsProofTests"/>.
/// <b>F20 is only partly fixed</b>: switching back to a database whose leftover lock names our own session takes the lock back; the route
/// that remains (the other program still holds the lock file when the session goes read-only) is accepted and stays known.
/// <b>F18 is fixed too (1.2.4)</b>: a save that puts back a removed lock file shows the "lock put back" notice only if it then succeeds, so a save that fails shows its failure banner alone. Its old route is a proof test.</para>
/// </summary>
internal static class KnownFindings
{
    /// <summary>Finding id to a one-line description, in id order.</summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F2"]  = "An edit made while a save is running (after the save's snapshot) is not in the save, and what protected it is thrown away when the save ends: its unsaved-changes marker (a crash before the next save deletes the edit) or, for the exit or switch save, the whole working copy.",
        ["F5"]  = "A restore is refused although nobody changed any data: every editing session rewrites the shared database at its first save, even with no edits.",
        ["F6"]  = "A kept-changes message promises that TermPoint will offer the changes back at the next open, unless someone else changes the database first, and the offer fails although nobody changed any data (the F5 shape), or is not made, or does not bring the work back.",
        ["F7"]  = "\"Restore Unsaved Changes?\" is asked, Restore is chosen, and then \"Can't Restore Unsaved Changes\" follows because someone changed data.",
        ["F20"] = "Another program still holds the lock file of a database when the person's session goes read-only on it (for example Save a Copy on the save-conflict banner, or a lost-lock demotion whose delete of the lock file is refused): the lock file is left naming their own session, so the read-only session holds that database's lock until the other program lets go or the lock goes stale. (Invariant I6 clause a. The old route, switching away and back, is fixed in 1.2.4: a leftover lock naming our own session is taken back. This remaining route is accepted.)",
        ["F22"] = "A release whose delete of the lock file is stalled (a blocked or hung delete that lands whenever the network returns) deletes the lock of whoever took over meanwhile: the new writer's lock file disappears although it is alive, so a third computer can open for editing alongside it. (Invariant I9, a computer rewrites only its own lock file; the delete is the release's background task, WriteLockService.DeleteLockFileGatedAsync.) The freeze or pause route (the computer sleeps between the release's check that the lock file is its own and its delete) is CLOSED by the 2 second check (WriteLockService.LockCheckFreshness: the check is repeated right before the delete). What remains is a stalled operation landing late; in reality that is covered by the 170 second takeover confirmation (WriteLockService.StaleConfirmationSeconds), as long as the operation dies within about 85 s (Windows' own limit, experiment E1). The harness does not yet model that 85 s limit, so a hung delete that lands after a takeover still shows here.",
        ["A13"] = "When someone else holds the lock and the copy of the database for reading fails (the network drops), the person sees two messages for one problem: \"Checkout Failed\" (which says the program will open read-only) and then \"Couldn't Open the Database\" (Try again / Close). Spec item 6.5."
    };

    /// <summary>Every known finding id, in order (F2, F5, F6, F7, F20, F22, A13). F1, F8, F12, F16, F17, F18, F19, F21 and F23 are fixed and so are no longer on the list.</summary>
    public static IReadOnlyList<string> Ids { get; } = new[] { "F2", "F5", "F6", "F7", "F20", "F22", "A13" };

    /// <summary>True when <paramref name="findingId"/> is a finding expected on today's code.</summary>
    /// <param name="findingId">A finding id as the classifier gives it ("F1" ... or "new").</param>
    public static bool IsKnown(string findingId) => Descriptions.ContainsKey(findingId);

    /// <summary>The one-line description of a known finding, or an empty string for anything else.</summary>
    /// <param name="findingId">A finding id.</param>
    public static string Describe(string findingId)
        => Descriptions.TryGetValue(findingId, out var text) ? text : "";
}
