namespace TermPoint.Tests.Harness;

/// <summary>
/// The ledger of the multi-computer harness (spec item 9.3, step 9.3b): a snapshot of every place the traceable edits
/// ("bob-1") can be at one moment, read from COPIES only (<see cref="Inspector"/> copies; nothing is ever opened in
/// place).
///
/// <para>The places, as the data-persistence review's table lists them:</para>
/// <list type="bullet">
/// <item><description><b>The shared database D.</b></description></item>
/// <item><description><b>A computer's live session:</b> its working file while the session's connection is open for
/// writing.</description></item>
/// <item><description><b>A computer's kept working file</b> (D' without a live session), and whether its
/// <c>.dirty</c> marker exists: without the marker the next start deletes it as "nothing was edited", so it does not
/// count as a safe place.</description></item>
/// <item><description><b>Saved copies:</b> every file a "Save a Copy" picker answered with (recorded when the prompt is
/// served, so walks need not name their exports).</description></item>
/// <item><description><b>Support archives</b> (<c>*.corrupt-*</c> in a computer's work folder): reported separately,
/// because the user cannot get work back from them.</description></item>
/// </list>
/// </summary>
internal sealed class Ledger
{
    /// <summary>The edits in D, in insertion order; null when D exists but cannot be read (the invariants then say "cannot tell").</summary>
    public IReadOnlyList<string>? Shared { get; private init; }

    /// <summary>Per computer name: the edits in its live write session's working file.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Live { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Per computer name: its kept working file's edits and whether the unsaved-changes marker exists.</summary>
    public IReadOnlyDictionary<string, (IReadOnlyList<string> Edits, bool HasMarker)> Kept { get; private init; }
        = new Dictionary<string, (IReadOnlyList<string>, bool)>();

    /// <summary>Per saved-copy path: the edits in that file (the file exists).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SavedCopies { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Per computer name: the edits found in its support archives (<c>*.corrupt-*</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Archives { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A ledger of an empty world (before the first step): D holds no edits and nothing else exists.</summary>
    public static Ledger Empty => new() { Shared = new List<string>() };

    /// <summary>
    /// Reads every place from the world as it is now.
    /// </summary>
    /// <param name="world">The world to look at.</param>
    /// <returns>The snapshot.</returns>
    public static Ledger Capture(World world)
    {
        var live     = new Dictionary<string, IReadOnlyList<string>>();
        var kept     = new Dictionary<string, (IReadOnlyList<string>, bool)>();
        var archives = new Dictionary<string, IReadOnlyList<string>>();
        var saved    = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var computer in world.Computers)
        {
            if (computer.HasWorkingFile)
            {
                var edits = world.TryEditsInFile(computer.WorkingFile) ?? new List<string>();
                if (computer.HasLiveSession) live[computer.Name] = edits;
                else kept[computer.Name] = (edits, computer.HasDirtyMarker);
            }

            var archived = new List<string>();
            foreach (var file in Directory.GetFiles(computer.WorkDir)
                         .Where(f => IsArchiveFile(Path.GetFileName(f))))
                archived.AddRange(world.TryEditsInFile(file) ?? new List<string>());
            if (archived.Count > 0) archives[computer.Name] = archived;
        }

        foreach (var path in world.SavedCopyPaths)
        {
            if (File.Exists(path) && !saved.ContainsKey(path))
                saved[path] = world.TryEditsInFile(path) ?? new List<string>();
        }

        return new Ledger
        {
            Shared      = File.Exists(world.DatabasePath) ? world.TryEditsInFile(world.DatabasePath) : new List<string>(),
            Live        = live,
            Kept        = kept,
            SavedCopies = saved,
            Archives    = archives
        };
    }

    /// <summary>True for the database file of a support archive (not its <c>-wal</c> / <c>-shm</c> / <c>-journal</c> sidecars).</summary>
    /// <param name="fileName">A file name in a work folder.</param>
    private static bool IsArchiveFile(string fileName)
        => fileName.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith("-journal", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the edit is in D (false as well when D cannot be read).</summary>
    /// <param name="edit">The edit id.</param>
    public bool InShared(string edit) => Shared?.Contains(edit) == true;

    /// <summary>True when the edit is in any computer's live session.</summary>
    /// <param name="edit">The edit id.</param>
    public bool InAnyLiveSession(string edit) => Live.Values.Any(v => v.Contains(edit));

    /// <summary>True when the edit is in a kept working file whose unsaved-changes marker exists.</summary>
    /// <param name="edit">The edit id.</param>
    public bool InKeptFileWithMarker(string edit) => Kept.Values.Any(k => k.HasMarker && k.Edits.Contains(edit));

    /// <summary>True when the edit is in a file a "Save a Copy" picker produced.</summary>
    /// <param name="edit">The edit id.</param>
    public bool InSavedCopy(string edit) => SavedCopies.Values.Any(v => v.Contains(edit));

    /// <summary>True when the edit survives a crash somewhere the user can get it back from: D, a live session, a kept file with its marker, or a saved copy.</summary>
    /// <param name="edit">The edit id.</param>
    public bool IsSafe(string edit)
        => InShared(edit) || InAnyLiveSession(edit) || InKeptFileWithMarker(edit) || InSavedCopy(edit);

    /// <summary>True when the edit exists only in a support archive.</summary>
    /// <param name="edit">The edit id.</param>
    public bool OnlyInSupportArchive(string edit) => !IsSafe(edit) && Archives.Values.Any(v => v.Contains(edit));

    /// <summary>
    /// The edits a computer holds that are not in D: what it would lose if its live session or kept file went away.
    /// </summary>
    /// <param name="computerName">The computer's name ("bob").</param>
    /// <returns>The unsaved edit ids (empty when the computer holds none).</returns>
    public IReadOnlyList<string> UnsavedEditsOf(string computerName)
    {
        var edits = new List<string>();
        if (Live.TryGetValue(computerName, out var live)) edits.AddRange(live);
        if (Kept.TryGetValue(computerName, out var kept)) edits.AddRange(kept.Edits);
        return edits.Distinct().Where(e => !InShared(e)).ToList();
    }

    /// <summary>
    /// Describes, in plain words, every place an edit is in (for the story's "Where every edit is" section).
    /// </summary>
    /// <param name="edit">The edit id.</param>
    /// <returns>The places, or a single "nowhere" entry.</returns>
    public IReadOnlyList<string> PlacesOf(string edit)
    {
        var places = new List<string>();
        if (InShared(edit)) places.Add("the shared database");
        foreach (var (name, edits) in Live)
            if (edits.Contains(edit)) places.Add($"{name}'s live session");
        foreach (var (name, kept) in Kept)
            if (kept.Edits.Contains(edit))
                places.Add(kept.HasMarker
                    ? $"{name}'s kept working file (with its unsaved-changes marker)"
                    : $"{name}'s working file WITHOUT its marker (the next start deletes it)");
        foreach (var (path, edits) in SavedCopies)
            if (edits.Contains(edit)) places.Add($"the saved copy {Path.GetFileName(path)}");
        foreach (var (name, edits) in Archives)
            if (edits.Contains(edit)) places.Add($"only in a support archive on {name}'s computer");
        if (places.Count == 0) places.Add("nowhere");
        return places;
    }
}
