namespace TermPoint.Services;

/// <summary>
/// The shared "put a finished LOCAL database file at a path that must not exist yet" pipeline, extracted from
/// <see cref="NewDatabaseFile"/> so a brand-new database (<see cref="NewDatabaseFile.CreateAsync"/>) and a database
/// created from a backup (<see cref="DatabaseRestore.CreateFromBackupAsync"/>) place their file in exactly the same,
/// proven way. Neither ever opens the target with SQLite.
///
/// <para><b>The pipeline,</b> split into three steps because the caller does its own work in between
/// (building the file, or staging and checking a backup):</para>
/// <list type="number">
/// <item><description><see cref="EnsureFolderAsync"/> — make sure the target's folder exists.</description></item>
/// <item><description><see cref="RefuseIfPresentAsync"/> — refuse when a file is already there (or the location is
/// dead). The caller runs it BEFORE doing any work (cheap refusal) and <see cref="PlaceAsync"/> runs it again
/// immediately before writing, because the work takes a moment and the target may have appeared meanwhile.</description></item>
/// <item><description><see cref="PlaceAsync"/> — hash the local file, stream it to <c>target.tmp</c> (stall-aware),
/// verify the bytes arrived intact, then move it into place WITHOUT overwrite, so an existing file can never be
/// clobbered. The <c>.tmp</c> is removed again whenever the file did not become the target.</description></item>
/// </list>
///
/// <para>Every operation on the target's location is deadline-bounded (<see cref="NetworkFileOps"/>). The results
/// reuse <see cref="NewDatabaseResult"/>: its outcomes (Created, AlreadyExists, Unreachable, Failed) are exactly
/// what both callers need.</para>
/// </summary>
internal static class NewFilePlacement
{
    /// <summary>
    /// The sibling a file is written to before it is moved into place: the target path plus <c>.tmp</c>. A stray
    /// one left by a crash is swept by <see cref="CheckoutService.CleanupOrphanedTmpAsync"/> on the next
    /// write-access open.
    /// </summary>
    /// <param name="targetPath">Full path of the file being created.</param>
    /// <returns>Full path of its <c>.tmp</c> sibling.</returns>
    internal static string TmpPathFor(string targetPath) => targetPath + ".tmp";

    /// <summary>
    /// Makes sure the target's folder exists. <see cref="Directory.CreateDirectory(string)"/> is a network round
    /// trip when the target is on a share, so it gets the same deadline as every other operation.
    /// </summary>
    /// <param name="targetPath">Full path of the file to be created; its folder is created if missing.</param>
    /// <param name="logger">Logger for the stall line; defaults to <see cref="App.Logger"/>.</param>
    /// <returns>Null when the folder is ready; an Unreachable result when creating it stalled.</returns>
    /// <exception cref="IOException">The folder could not be created (callers catch and report it as a failure).</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the location was denied.</exception>
    internal static async Task<NewDatabaseResult?> EnsureFolderAsync(string targetPath, IAppLogger? logger = null)
    {
        var folder = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(folder))
            return null;

        // Block lambda on purpose: it selects the void RunAsync overload, which returns bool.
        var ready = await NetworkFileOps.RunAsync(
            () => { Directory.CreateDirectory(folder); }, "create database folder").ConfigureAwait(false);

        return ready ? null : UnreachableResult("creating the database folder stalled", logger ?? App.Logger);
    }

    /// <summary>
    /// Probes the target path and converts anything other than "answered: absent" into the result the caller
    /// should return.
    /// </summary>
    /// <param name="targetPath">The path that must not exist.</param>
    /// <returns>
    /// Null when the target is confirmed absent (carry on); <see cref="NewDatabaseOutcome.AlreadyExists"/> when a
    /// file is there; <see cref="NewDatabaseOutcome.Unreachable"/> when the location did not answer.
    /// </returns>
    internal static async Task<NewDatabaseResult?> RefuseIfPresentAsync(string targetPath)
    {
        switch (await NetworkFileOps.ProbeFileAsync(targetPath).ConfigureAwait(false))
        {
            case FileProbeResult.Exists:
                return new NewDatabaseResult(NewDatabaseOutcome.AlreadyExists);
            case FileProbeResult.Unreachable:
                return new NewDatabaseResult(NewDatabaseOutcome.Unreachable);
            default:
                return null;
        }
    }

    /// <summary>
    /// Places the finished local file at <paramref name="targetPath"/>, which must not exist: late probe, hash,
    /// copy to <c>.tmp</c>, verify, no-overwrite move, and removal of the <c>.tmp</c> on every path where the file
    /// did not become the target. Failures that are not exceptions come back as results; exceptions (an IO error
    /// on the share, access denied) propagate after the cleanup, for the caller to report.
    /// </summary>
    /// <param name="localPath">The finished local file to place (a quiescent, rollback-journal database).</param>
    /// <param name="targetPath">Full path to create. It is never overwritten.</param>
    /// <param name="awaitTmpCleanup">
    /// True to wait (deadline-bounded) for the removal of a leftover <c>.tmp</c> before returning, so the caller can
    /// promise "nothing is left behind" (the restore does). False to remove it in the background so the caller is
    /// not delayed on a dead share (a new database does; anything missed is swept on the next write-access open).
    /// </param>
    /// <param name="localHash">The local file's SHA-256 if the caller already has it; computed here when null.</param>
    /// <param name="copyAsync">
    /// Replaces <see cref="NetworkFileOps.CopyAsync"/> for the transfer to <c>.tmp</c>: takes (source, destination) and
    /// returns false when the copy stalled. Null in production; the restore's DEBUG fault plan uses it to cut the
    /// transfer on purpose.
    /// </param>
    /// <param name="logger">Logger for the stall and refusal lines; defaults to <see cref="App.Logger"/>.</param>
    /// <returns>
    /// Created when the file now exists at the target; AlreadyExists when a file is (or appeared) there, untouched;
    /// Unreachable on a stall; Failed when the copy did not match the local file.
    /// </returns>
    /// <exception cref="IOException">A file operation failed other than by an existing target.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the location was denied.</exception>
    internal static async Task<NewDatabaseResult> PlaceAsync(
        string localPath,
        string targetPath,
        bool awaitTmpCleanup,
        string? localHash = null,
        Func<string, string, Task<bool>>? copyAsync = null,
        IAppLogger? logger = null)
    {
        var log          = logger ?? App.Logger;
        var tmpPath      = TmpPathFor(targetPath);
        var tmpTouched   = false;

        try
        {
            // Re-check immediately before writing: the caller's work took a moment and the target may have
            // appeared (another user created the same name). Never overwrite.
            var late = await RefuseIfPresentAsync(targetPath).ConfigureAwait(false);
            if (late is not null) return late;

            // Stream to .tmp (stall-aware), verify the bytes arrived intact, then move into place WITHOUT
            // overwrite so an existing file can never be clobbered.
            localHash ??= NetworkFileOps.ComputeHash(localPath);

            tmpTouched = true;
            var copied = copyAsync is null
                ? await NetworkFileOps.CopyAsync(localPath, tmpPath).ConfigureAwait(false)
                : await copyAsync(localPath, tmpPath).ConfigureAwait(false);
            if (!copied)
                return UnreachableResult("copy to the share stalled", log);

            var (hashCompleted, remoteHash) = await NetworkFileOps.ComputeHashAsync(tmpPath).ConfigureAwait(false);
            if (!hashCompleted)
                return UnreachableResult("verification of the copy stalled", log);
            if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                return new NewDatabaseResult(NewDatabaseOutcome.Failed,
                    new IOException("The new database did not copy to the destination correctly."));

            try
            {
                if (!await NetworkFileOps.MoveAsync(tmpPath, targetPath, overwrite: false).ConfigureAwait(false))
                    return UnreachableResult("move into place stalled", log);
            }
            catch (IOException)
            {
                // The no-overwrite move refused. If the target is there it appeared after the check above;
                // report that distinctly (and leave it untouched). Anything else is a genuine failure.
                if (await TargetExistsAsync(targetPath).ConfigureAwait(false))
                    return new NewDatabaseResult(NewDatabaseOutcome.AlreadyExists);
                throw;
            }

            tmpTouched = false;   // .tmp became the target — nothing to clean up
            return new NewDatabaseResult(NewDatabaseOutcome.Created);
        }
        finally
        {
            // A failed attempt may leave a partial .tmp on the share. The delete is deadline-bounded and
            // non-throwing, so waiting for it is safe; not waiting leaves it to run in the background.
            if (tmpTouched)
            {
                var cleanup = NetworkFileOps.DeleteAsync(tmpPath);
                if (awaitTmpCleanup)
                    await cleanup.ConfigureAwait(false);
                else
                    _ = cleanup;
            }
        }
    }

    /// <summary>Deadline-bounded existence check used to classify a refused no-overwrite move.</summary>
    /// <param name="path">File to test.</param>
    /// <returns>True only when the location answered and the file is there.</returns>
    private static async Task<bool> TargetExistsAsync(string path)
        => await NetworkFileOps.ProbeFileAsync(path).ConfigureAwait(false) == FileProbeResult.Exists;

    /// <summary>Logs a stall and returns the matching <see cref="NewDatabaseOutcome.Unreachable"/> result.</summary>
    /// <param name="what">Which step stalled (log text only).</param>
    /// <param name="log">Logger to write the line to.</param>
    private static NewDatabaseResult UnreachableResult(string what, IAppLogger log)
    {
        log.LogInfo($"NewFilePlacement: {what} — location unreachable");
        return new NewDatabaseResult(NewDatabaseOutcome.Unreachable);
    }
}
