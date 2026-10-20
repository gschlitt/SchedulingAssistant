namespace TermPoint.Tests.Harness;

/// <summary>
/// The one central list of findings that are EXPECTED on today's code (spec item 9.3, step 9.3c; data-persistence review,
/// section 3). A generated run that breaks an invariant in a way one of these explains counts it and keeps its smallest
/// story, and carries on: it does not fail. Any violation the classifier (<see cref="Findings"/>) cannot explain is "new"
/// and FAILS the run with its shrunk story.
///
/// <para>As the redesign steps (8.9) fix findings, their ids come out of this list and the harness then proves each is
/// gone: a generated run that meets one again fails as a new violation. F8 needs a crash in the middle of an operation
/// (the crash sweep, step 9.3c-2a, finds it). F2, F12 and F16 need a pause in the middle of an operation so that another
/// event happens meanwhile (step 9.3c-2b). Hand-written walks declare their own findings
/// (<see cref="World.ExpectFindings"/>) and do not read this list.</para>
/// </summary>
internal static class KnownFindings
{
    /// <summary>Finding id to a one-line description, in id order.</summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"]  = "The X (or Discard, unconfirmed) on \"Restore Unsaved Changes?\" / \"Can't Restore Unsaved Changes\" deletes the kept changes.",
        ["F5"]  = "A restore is refused although nobody changed any data: every editing session rewrites the shared database at its first save, even with no edits.",
        ["F6"]  = "A message promises that TermPoint will offer to restore unsaved changes next time, and it does not (or cannot).",
        ["F7"]  = "\"Restore Unsaved Changes?\" is asked, Restore is chosen, and then \"Can't Restore Unsaved Changes\" follows because someone changed data.",
        ["F8"]  = "A crash between the rename and the marker delete of a save: \"Can't Restore Unsaved Changes\" follows although the shared database already holds every kept change.",
        ["F12"] = "The save does not re-check the lock (or the database) just before the rename onto the shared database."
    };

    /// <summary>Every known finding id, in order (F1, F5, F6, F7, F8, F12).</summary>
    public static IReadOnlyList<string> Ids { get; } = new[] { "F1", "F5", "F6", "F7", "F8", "F12" };

    /// <summary>True when <paramref name="findingId"/> is a finding expected on today's code.</summary>
    /// <param name="findingId">A finding id as the classifier gives it ("F1" ... or "new").</param>
    public static bool IsKnown(string findingId) => Descriptions.ContainsKey(findingId);

    /// <summary>The one-line description of a known finding, or an empty string for anything else.</summary>
    /// <param name="findingId">A finding id.</param>
    public static string Describe(string findingId)
        => Descriptions.TryGetValue(findingId, out var text) ? text : "";
}
