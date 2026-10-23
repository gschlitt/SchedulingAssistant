using TermPoint.Services;

namespace NetworkProbe;

/// <summary>Where in the life of the operation the network is cut (spec section 6).</summary>
internal enum CutPoint
{
    /// <summary>"CB": the network is cut first, then the call is made.</summary>
    BeforeCall,

    /// <summary>"CM": the call is started on a slowed network and the cut lands while it is demonstrably in progress.</summary>
    MidOperation
}

/// <summary>The test files a case needs to exist before it starts (spec section 5.5).</summary>
[Flags]
internal enum Fixtures
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A small text file on the share.</summary>
    SmallFile = 1,

    /// <summary>A second small text file on the share (the move case moves the first one over it).</summary>
    SecondFile = 2,

    /// <summary>The large random file, uploaded to the share (the hash case reads it there).</summary>
    BigOnShare = 4,

    /// <summary>The large random file on the local disk (the copy case reads it there).</summary>
    BigLocal = 8,

    /// <summary>A small valid SQLite database on the share (the integrity case checks it there).</summary>
    SmallDb = 16
}

/// <summary>
/// What one call to a <see cref="NetworkFileOps"/> method came back with, boiled down to the two things a verdict
/// needs.
/// </summary>
/// <param name="GaveUp">
/// True when the method reported the timeout or stall (<c>Completed == false</c>, a <c>false</c> bool, a null hash,
/// <c>FileProbeResult.Unreachable</c>). False when it returned a normal answer.
/// </param>
/// <param name="Text">The method's return value, written out for the report.</param>
internal sealed record OpResult(bool GaveUp, string Text);

/// <summary>
/// The paths one case works with. All of them are built for every case; <see cref="Fixtures"/> says which are
/// actually created before the case starts.
/// </summary>
/// <param name="Dir">The case's own folder on the share (deleted when the case ends).</param>
/// <param name="SmallFile">A small text file in <paramref name="Dir"/>.</param>
/// <param name="SecondFile">A second small text file name in <paramref name="Dir"/>.</param>
/// <param name="BigShareFile">The large file's name on the share, in <paramref name="Dir"/>.</param>
/// <param name="BigLocalFile">The large file on the local disk (shared by every case of the run).</param>
/// <param name="SmallDb">The small SQLite database's name on the share, in <paramref name="Dir"/>.</param>
/// <param name="CopyDest">Where a copy case writes on the share, in <paramref name="Dir"/>.</param>
/// <param name="MissingFile">A name in <paramref name="Dir"/> that never exists.</param>
internal sealed record CaseFiles(
    string Dir,
    string SmallFile,
    string SecondFile,
    string BigShareFile,
    string BigLocalFile,
    string SmallDb,
    string CopyDest,
    string MissingFile);

/// <summary>
/// One row of the case matrix (spec section 6): a name, when the network is cut, which real
/// <see cref="NetworkFileOps"/> call is made, and what that call needs set up first.
/// </summary>
/// <param name="Name">The case name used by <c>--only</c>, in the report and in <c>--list</c>.</param>
/// <param name="Cut">Cut before the call, or mid-operation.</param>
/// <param name="CallText">The call, written the way the spec writes it (for <c>--list</c>).</param>
/// <param name="Why">One line on why this case exists (for <c>--list</c>).</param>
/// <param name="Needs">The test files to create while the network is normal.</param>
/// <param name="Execute">
/// Makes the real call and reports what it returned. It is invoked on a dedicated thread (see
/// <see cref="OperationRun"/>) so that a call which blocks its caller cannot block the probe.
/// </param>
/// <param name="WatchDestination">
/// Mid-operation only: also require the file at <see cref="CaseFiles.CopyDest"/> to be larger than 0 before the cut,
/// so the cut provably lands on a copy that has started writing.
/// </param>
/// <param name="PassWindow">
/// How long after the cut is confirmed the call may take to give up and still pass; null means
/// <see cref="NetworkFileOps.TimeoutMs"/> + 2 s (spec section 5.6).
/// </param>
/// <param name="WatchProcessReads">
/// Mid-operation only: also require this process to have read at least 1 MB since the call started before the cut
/// (<see cref="ProcessIo"/>), so the cut provably lands on a read loop that is running, not on the open.
/// </param>
/// <param name="RequiredLanding">
/// The step the stall must be caught in for a PASS (for example <c>ComputeHash (read)</c>); a pass caught in any other
/// step is NOT TESTED, because it did not test what the case is about. Null: any step counts.
/// </param>
internal sealed record CaseDefinition(
    string Name,
    CutPoint Cut,
    string CallText,
    string Why,
    Fixtures Needs,
    Func<CaseFiles, Task<OpResult>> Execute,
    bool WatchDestination = false,
    TimeSpan? PassWindow = null,
    bool WatchProcessReads = false,
    string? RequiredLanding = null)
{
    /// <summary>The short mode label used in tables: <c>CB</c> or <c>CM</c>.</summary>
    public string ModeLabel => Cut == CutPoint.BeforeCall ? "CB" : "CM";
}

/// <summary>
/// The case matrix of spec section 6, as data: the eleven cut-before-call cases (plus the optional boolean integrity
/// variant), the two mid-operation cases, and the known-good control of section 5.3. Every case calls the REAL
/// <see cref="NetworkFileOps"/> method; nothing is re-implemented here.
/// </summary>
internal static class CaseCatalog
{
    /// <summary>
    /// The known-good control (spec section 5.3): <c>ExistsAsync</c> on a file that does not exist, cut before the
    /// call. This path was proven in the July field test, so if it does not report a timeout in time the rig, not
    /// TermPoint, is at fault. Not selectable with <c>--only</c>; it always runs first.
    /// </summary>
    public static CaseDefinition ControlGood { get; } = new(
        "control-good", CutPoint.BeforeCall,
        "ExistsAsync(no-such-file)",
        "Control: a path proven to give up in time (July field test). Must report a timeout within TimeoutMs + 1.5 s.",
        Fixtures.None,
        async f => FromCompleted(await NetworkFileOps.ExistsAsync(f.MissingFile).ConfigureAwait(false)),
        PassWindow: TimeSpan.FromMilliseconds(NetworkFileOps.TimeoutMs + 1500));

    /// <summary>The regular cases, in the order the spec lists them: cut-before-call first, then mid-operation.</summary>
    public static IReadOnlyList<CaseDefinition> All { get; } = new List<CaseDefinition>
    {
        // ── Cut BEFORE the call (CB), each once ──────────────────────────────────────────────────────────────
        new("exists", CutPoint.BeforeCall,
            "ExistsAsync(file)", "The lock-file and database existence checks.",
            Fixtures.SmallFile,
            async f => FromCompleted(await NetworkFileOps.ExistsAsync(f.SmallFile).ConfigureAwait(false))),

        new("dir-exists", CutPoint.BeforeCall,
            "DirectoryExistsAsync(folder)", "The reachability probe used to decide whether the share is there.",
            Fixtures.SmallFile,
            async f => FromCompleted(await NetworkFileOps.DirectoryExistsAsync(f.Dir).ConfigureAwait(false))),

        new("probe-file", CutPoint.BeforeCall,
            "ProbeFileAsync(file)", "The tri-state probe that tells 'missing' from 'unreachable'.",
            Fixtures.SmallFile,
            async f =>
            {
                var probe = await NetworkFileOps.ProbeFileAsync(f.SmallFile).ConfigureAwait(false);
                return new OpResult(probe == FileProbeResult.Unreachable, $"FileProbeResult.{probe}");
            }),

        new("stat", CutPoint.BeforeCall,
            "RunAsync(() => { var i = new FileInfo(f); return (i.Length, i.LastWriteTimeUtc); }, \"File.Stat\")",
            "The save's stat of D (size and last-write time) that proves D is still as we last saw it.",
            Fixtures.SmallFile,
            async f =>
            {
                var (completed, stat) = await NetworkFileOps.RunAsync(
                    () => { var i = new FileInfo(f.SmallFile); return (i.Length, i.LastWriteTimeUtc); },
                    "File.Stat").ConfigureAwait(false);
                return new OpResult(!completed, completed ? $"Completed=True, Length={stat.Item1}" : "Completed=False");
            }),

        new("read-text", CutPoint.BeforeCall,
            "ReadAllTextAsync(file)", "Reading the lock file.",
            Fixtures.SmallFile,
            async f =>
            {
                var (completed, text) = await NetworkFileOps.ReadAllTextAsync(f.SmallFile).ConfigureAwait(false);
                return new OpResult(!completed, completed ? $"Completed=True, {text?.Length ?? 0} characters" : "Completed=False");
            }),

        new("write-text", CutPoint.BeforeCall,
            "WriteAllTextAsync(file, \"x\")", "Writing the lock file.",
            Fixtures.SmallFile,
            async f => FromBool(await NetworkFileOps.WriteAllTextAsync(f.SmallFile, "x").ConfigureAwait(false))),

        new("move", CutPoint.BeforeCall,
            "MoveAsync(fileA, fileB, overwrite: true)", "The save's rename of D.tmp over D, and the heartbeat's rename.",
            Fixtures.SmallFile | Fixtures.SecondFile,
            async f => FromBool(await NetworkFileOps.MoveAsync(f.SmallFile, f.SecondFile, overwrite: true).ConfigureAwait(false))),

        new("delete", CutPoint.BeforeCall,
            "DeleteAsync(file)", "Deleting a lock or temporary file.",
            Fixtures.SmallFile,
            async f => FromBool(await NetworkFileOps.DeleteAsync(f.SmallFile).ConfigureAwait(false))),

        new("hash-cb", CutPoint.BeforeCall,
            "ComputeHashAsync(big file on share)", "The save's hash check of the copied database, cut before it starts.",
            Fixtures.BigOnShare,
            async f => FromHash(await NetworkFileOps.ComputeHashAsync(f.BigShareFile).ConfigureAwait(false))),

        new("copy-cb", CutPoint.BeforeCall,
            "CopyAsync(local big file, share\\copy.bin)", "The save's copy of D' to D.tmp, cut before it starts.",
            Fixtures.BigLocal,
            async f => FromBool(await NetworkFileOps.CopyAsync(f.BigLocalFile, f.CopyDest).ConfigureAwait(false))),

        new("integrity", CutPoint.BeforeCall,
            "CheckIntegrityWithSummaryAsync(small db on share)", "The integrity check of a database on the share.",
            Fixtures.SmallDb,
            async f =>
            {
                var (completed, check) = await NetworkFileOps.CheckIntegrityWithSummaryAsync(f.SmallDb).ConfigureAwait(false);
                return new OpResult(!completed, completed ? $"Completed=True, {check.Result}" : "Completed=False");
            }),

        // The spec allows this one "if cheap to add": the older boolean entry point (it shares RunAsync with the one above).
        new("integrity-bool", CutPoint.BeforeCall,
            "CheckIntegrityAsync(small db on share)", "Optional extra: the older boolean integrity entry point.",
            Fixtures.SmallDb,
            async f =>
            {
                var (completed, passed) = await NetworkFileOps.CheckIntegrityAsync(f.SmallDb).ConfigureAwait(false);
                return new OpResult(!completed, completed ? $"Completed=True, Passed={passed}" : "Completed=False");
            }),

        // ── Cut MID-operation (CM), each --repeat times ─────────────────────────────────────────────────────
        new("copy-cm", CutPoint.MidOperation,
            "CopyAsync(local big file, share\\copy.bin)",
            "THE A1 SUSPECT: a write on an open handle after the cut (the chunk write is raced against the deadline only after WriteAsync returns).",
            Fixtures.BigLocal,
            async f => FromBool(await NetworkFileOps.CopyAsync(f.BigLocalFile, f.CopyDest).ConfigureAwait(false)),
            WatchDestination: true),

        new("hash-cm", CutPoint.MidOperation,
            "ComputeHashAsync(big file on share)",
            "The read path, cut part-way through a read: only after the probe has provably read 1 MB of the file, and only a stall caught in 'ComputeHash (read)' counts.",
            Fixtures.BigOnShare,
            async f => FromHash(await NetworkFileOps.ComputeHashAsync(f.BigShareFile).ConfigureAwait(false)),
            WatchProcessReads: true,
            RequiredLanding: "ComputeHash (read)")
    };

    /// <summary>
    /// Picks the cases to run.
    /// </summary>
    /// <param name="only">The names given to <c>--only</c>, or null for all.</param>
    /// <returns>The matching cases, in matrix order.</returns>
    public static IReadOnlyList<CaseDefinition> Select(IReadOnlyCollection<string>? only)
        => only is null
            ? All
            : All.Where(c => only.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Finds names that are not cases.</summary>
    /// <param name="names">Names given to <c>--only</c>.</param>
    /// <returns>The names that match no case.</returns>
    public static IReadOnlyList<string> Unknown(IEnumerable<string> names)
        => names.Where(n => !All.Any(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Boils a <c>(Completed, Exists)</c> answer down to an <see cref="OpResult"/>.</summary>
    /// <param name="answer">What <c>ExistsAsync</c> or <c>DirectoryExistsAsync</c> returned.</param>
    /// <returns>Gave up when <c>Completed</c> is false.</returns>
    private static OpResult FromCompleted((bool Completed, bool Exists) answer)
        => new(!answer.Completed, $"Completed={answer.Completed}, Exists={answer.Exists}");

    /// <summary>Boils a plain success bool (false means the timeout was reported) down to an <see cref="OpResult"/>.</summary>
    /// <param name="ok">What the method returned.</param>
    /// <returns>Gave up when <paramref name="ok"/> is false.</returns>
    private static OpResult FromBool(bool ok) => new(!ok, $"returned {ok}");

    /// <summary>Boils a <c>(Completed, Hash)</c> answer down to an <see cref="OpResult"/>.</summary>
    /// <param name="answer">What <c>ComputeHashAsync</c> returned.</param>
    /// <returns>Gave up when the hash is null.</returns>
    private static OpResult FromHash((bool Completed, string? Hash) answer)
        => new(!answer.Completed || answer.Hash is null,
            answer.Completed ? $"Completed=True, hash {(answer.Hash is null ? "null" : answer.Hash[..8] + "...")}" : "Completed=False, hash null");
}
