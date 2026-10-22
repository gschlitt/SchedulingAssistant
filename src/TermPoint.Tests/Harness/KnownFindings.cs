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
/// </summary>
internal static class KnownFindings
{
    /// <summary>Finding id to a one-line description, in id order.</summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F2"]  = "An edit made while a save is running (after the save's snapshot) is not in the save, and what protected it is thrown away when the save ends: its unsaved-changes marker (a crash before the next save deletes the edit) or, for the exit or switch save, the whole working copy.",
        ["F5"]  = "A restore is refused although nobody changed any data: every editing session rewrites the shared database at its first save, even with no edits.",
        ["F6"]  = "A message promises that TermPoint will offer to restore unsaved changes next time, and it does not (or cannot).",
        ["F7"]  = "\"Restore Unsaved Changes?\" is asked, Restore is chosen, and then \"Can't Restore Unsaved Changes\" follows because someone changed data.",
        ["F16"] = "A lost-lock demotion still running when the user switches database lands on the NEW session: it copies the new database and leaves a session that holds the new lock read-only."
    };

    /// <summary>Every known finding id, in order (F2, F5, F6, F7, F16). F1, F8 and F12 are fixed and so are no longer on the list.</summary>
    public static IReadOnlyList<string> Ids { get; } = new[] { "F2", "F5", "F6", "F7", "F16" };

    /// <summary>True when <paramref name="findingId"/> is a finding expected on today's code.</summary>
    /// <param name="findingId">A finding id as the classifier gives it ("F1" ... or "new").</param>
    public static bool IsKnown(string findingId) => Descriptions.ContainsKey(findingId);

    /// <summary>The one-line description of a known finding, or an empty string for anything else.</summary>
    /// <param name="findingId">A finding id.</param>
    public static string Describe(string findingId)
        => Descriptions.TryGetValue(findingId, out var text) ? text : "";
}
