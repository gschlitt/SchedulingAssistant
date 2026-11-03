namespace TermPoint.Tests.Harness;

/// <summary>
/// The one central list of findings that are EXPECTED on today's code (spec item 9.3, step 9.3c; data-persistence review,
/// section 3). A generated run that breaks an invariant in a way one of these explains counts it and keeps its smallest
/// story, and carries on: it does not fail. Any violation the classifier (<see cref="Findings"/>) cannot explain is "new"
/// and FAILS the run with its shrunk story.
///
/// <para>As the redesign steps (8.9) fix findings, their ids come out of this list and the harness then proves each is
/// gone: a generated run that meets one again fails as a new violation. F2 and F16 need a pause in the middle of an operation
/// so that another event happens meanwhile (the pause sweep, step 9.3c-2b, finds them; F16 is also found by the invariant I6 in
/// the generated search). Hand-written walks declare their own findings (<see cref="World.ExpectFindings"/>) and do not read this
/// list.</para>
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
        ["F16"] = "A lost-lock demotion still running when the user switches database lands on the NEW session: it copies the new database and leaves a session that holds the new lock read-only.",
        ["F17"] = "A save during a close or a switch finds a problem (someone took over, the location can't be reached, the lock file was removed, the file is in use), and the person is shown the problem's banner and then \"Changes Not Saved\": two messages for one problem, both saying what happens to their changes. 1.2.4: show only the dialog.",
        ["F18"] = "Two different lock-lost banners for one loss (\"Another program removed the file...\" and \"... has taken over editing\"), seen when a held heartbeat check finishes while the person switches database. Not yet investigated.",
        ["F20"] = "Another program has a file of the share open (the database, so a switch's final save fails; or the lock file, so the release can't delete it) when the person switches to another database: the lock file of the old database is left behind naming their own session, and when they switch back they are read-only (\"Someone else is editing this database right now\") although it is their own leftover lock, and their kept unsaved changes cannot be restored yet. (Invariant I6 clause a. By code reading, the leftover lock's holder is their own live process, so it is not reclaimed on its own.)",
        ["F22"] = "A release whose delete of the lock file is stalled (a blocked or hung delete that lands whenever the network returns) deletes the lock of whoever took over meanwhile: the new writer's lock file disappears although it is alive, so a third computer can open for editing alongside it. (Invariant I9, a computer rewrites only its own lock file; the delete is the release's background task, WriteLockService.DeleteLockFileGatedAsync.) The freeze or pause route (the computer sleeps between the release's check that the lock file is its own and its delete) is CLOSED by the 2 second check (WriteLockService.LockCheckFreshness: the check is repeated right before the delete). What remains is a stalled operation landing late; in reality that is covered by the 170 second takeover confirmation (WriteLockService.StaleConfirmationSeconds), as long as the operation dies within about 85 s (Windows' own limit, experiment E1). The harness does not yet model that 85 s limit, so a hung delete that lands after a takeover still shows here.",
        ["F23"] = "Another program has the shared database open when the person switches back to it, and the kept changes cannot be restored; they choose Save a Copyâ€¦ and cancel the save dialog, then Discard (and confirm it). The open's banner (\"TermPoint couldn't save because the database file is in use by another program...\") then promises that their changes are kept, although the person has just discarded them. (Invariant I5, ChangesKept. Found by the random walks in 1.2.4 item 8.4; not clock related, the changed event catalogue drew a new route. Not yet investigated: the banner text is read from the story, not from the code.)",
        ["A13"] = "When someone else holds the lock and the copy of the database for reading fails (the network drops), the person sees two messages for one problem: \"Checkout Failed\" (which says the program will open read-only) and then \"Couldn't Open the Database\" (Try again / Close). Spec item 6.5."
    };

    /// <summary>Every known finding id, in order (F2, F5, F6, F7, F16, F17, F18, F20, F22, F23, A13). F1, F8, F12, F19 and F21 are fixed and so are no longer on the list.</summary>
    public static IReadOnlyList<string> Ids { get; } = new[] { "F2", "F5", "F6", "F7", "F16", "F17", "F18", "F20", "F22", "F23", "A13" };

    /// <summary>True when <paramref name="findingId"/> is a finding expected on today's code.</summary>
    /// <param name="findingId">A finding id as the classifier gives it ("F1" ... or "new").</param>
    public static bool IsKnown(string findingId) => Descriptions.ContainsKey(findingId);

    /// <summary>The one-line description of a known finding, or an empty string for anything else.</summary>
    /// <param name="findingId">A finding id.</param>
    public static string Describe(string findingId)
        => Descriptions.TryGetValue(findingId, out var text) ? text : "";
}
