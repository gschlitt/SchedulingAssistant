using Microsoft.Data.Sqlite;
using TermPoint.Data;

namespace TermPoint.Services;

/// <summary>Outcome of <see cref="NewDatabaseFile.CreateAsync"/>.</summary>
public enum NewDatabaseOutcome
{
    /// <summary>The database file now exists at the target path, complete and in rollback-journal mode.</summary>
    Created,

    /// <summary>
    /// A file already exists at the target path (it was there on entry, or appeared while the new
    /// database was being built). It was NOT touched.
    /// </summary>
    AlreadyExists,

    /// <summary>The target location could not be reached or stalled past the network deadline.</summary>
    Unreachable,

    /// <summary>Any other failure; see <see cref="NewDatabaseResult.Error"/>.</summary>
    Failed
}

/// <summary>
/// Result of <see cref="NewDatabaseFile.CreateAsync"/>.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Error">
/// The exception behind a <see cref="NewDatabaseOutcome.Failed"/> outcome (null for every other
/// outcome). Callers feed it to their existing error-message mapping.
/// </param>
public sealed record NewDatabaseResult(NewDatabaseOutcome Outcome, Exception? Error = null)
{
    /// <summary>True when the file was created.</summary>
    public bool Succeeded => Outcome == NewDatabaseOutcome.Created;
}

/// <summary>
/// Creates a brand-new, empty TermPoint database file on a shared (possibly network) path
/// WITHOUT ever opening that path with SQLite.
///
/// <para><b>Why:</b> the shared database D is only ever copied, never opened. Building the schema
/// directly at D (<c>new DatabaseContext(D)</c>) made SQLite open a file over the share and leave
/// a WAL-mode header on it until the first save (spec item 4, path 3). Instead the schema and seed
/// data are built in a LOCAL temporary file, the file is converted to rollback-journal mode (WAL
/// must never be active on a network file), and the finished, quiescent file is streamed to the
/// share with the stall-aware <see cref="NetworkFileOps"/> copy — the same pipeline the save path
/// uses for D' → D.</para>
///
/// <para><b>Safety:</b> the copy goes to <c>D.tmp</c> first and is moved into place without
/// overwrite, so (a) a partly written D is never visible to a colleague and (b) an existing D can
/// never be replaced, even if one appears while the new database is being built. A stray
/// <c>D.tmp</c> left by a crash is cleaned up by the next open that gets write access
/// (<see cref="CheckoutService.CleanupOrphanedTmpAsync"/>).</para>
/// </summary>
public static class NewDatabaseFile
{
    /// <summary>
    /// Creates a new database at <paramref name="targetPath"/>. The target must not exist.
    /// Every operation on the target's location is deadline-bounded; the local build runs on a
    /// thread-pool thread. Never throws: failures are reported through the result.
    /// </summary>
    /// <param name="targetPath">Full path of the database file to create (D).</param>
    /// <param name="localDirectory">
    /// Folder for the temporary local build. Defaults to the system temp folder; tests supply their
    /// own so they can assert nothing is left behind. The temporary file is always deleted.
    /// </param>
    /// <returns>The outcome; <see cref="NewDatabaseOutcome.Created"/> means D exists and is complete.</returns>
    public static async Task<NewDatabaseResult> CreateAsync(string targetPath, string? localDirectory = null)
    {
        var localPath = Path.Combine(
            localDirectory ?? Path.GetTempPath(),
            $"termpoint-new-{Guid.NewGuid():N}.db");
        var shareTmpPath    = targetPath + ".tmp";
        var shareTmpTouched = false;

        try
        {
            // Make sure the target folder exists. Directory.CreateDirectory is a network round trip
            // when the target is on a share, so it gets the same deadline as every other operation.
            // (Block lambda on purpose: it selects the void RunAsync overload, which returns bool.)
            var folder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(folder))
            {
                var folderReady = await NetworkFileOps.RunAsync(
                    () => { Directory.CreateDirectory(folder); }, "create database folder").ConfigureAwait(false);
                if (!folderReady)
                    return UnreachableResult("creating the database folder stalled");
            }

            // Refuse before doing any work if the target is already there (or the location is dead).
            var early = await ProbeTargetAsync(targetPath).ConfigureAwait(false);
            if (early is not null) return early;

            // Build the finished file locally (schema + migrations + seed), in rollback mode.
            await Task.Run(() => BuildLocalFile(localPath)).ConfigureAwait(false);

            // Re-check immediately before writing: building takes a moment and the target may have
            // appeared (another user created the same name). Never overwrite.
            var late = await ProbeTargetAsync(targetPath).ConfigureAwait(false);
            if (late is not null) return late;

            // Stream to D.tmp (stall-aware), verify the bytes arrived intact, then move into place
            // WITHOUT overwrite so an existing D can never be clobbered.
            var localHash = NetworkFileOps.ComputeHash(localPath);

            shareTmpTouched = true;
            if (!await NetworkFileOps.CopyAsync(localPath, shareTmpPath).ConfigureAwait(false))
                return UnreachableResult("copy to the share stalled");

            var (hashCompleted, remoteHash) = await NetworkFileOps.ComputeHashAsync(shareTmpPath).ConfigureAwait(false);
            if (!hashCompleted)
                return UnreachableResult("verification of the copy stalled");
            if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                return new NewDatabaseResult(NewDatabaseOutcome.Failed,
                    new IOException("The new database did not copy to the destination correctly."));

            try
            {
                if (!await NetworkFileOps.MoveAsync(shareTmpPath, targetPath, overwrite: false).ConfigureAwait(false))
                    return UnreachableResult("move into place stalled");
            }
            catch (IOException)
            {
                // The no-overwrite move refused. If D is there it appeared after the check above;
                // report that distinctly (and leave D untouched). Anything else is a genuine failure.
                if (await TargetExistsAsync(targetPath).ConfigureAwait(false))
                    return new NewDatabaseResult(NewDatabaseOutcome.AlreadyExists);
                throw;
            }

            shareTmpTouched = false;   // D.tmp became D — nothing to clean up
            return new NewDatabaseResult(NewDatabaseOutcome.Created);
        }
        catch (Exception ex)
        {
            // Logged at info level only: the caller turns the result into a user message and logs
            // the error itself, so reporting it here too would double-count it.
            App.Logger.LogInfo($"NewDatabaseFile: creating a new database failed: {ex.Message}");
            return new NewDatabaseResult(NewDatabaseOutcome.Failed, ex);
        }
        finally
        {
            DeleteLocalFiles(localPath);

            // A failed attempt may leave a partial D.tmp on the share. Remove it, but don't make the
            // caller wait: the delete is deadline-bounded and non-throwing, and anything it misses
            // is swept by CleanupOrphanedTmpAsync on the next write-access open.
            if (shareTmpTouched)
                _ = NetworkFileOps.DeleteAsync(shareTmpPath);
        }
    }

    /// <summary>
    /// Probes the target path and converts anything other than "answered: absent" into the result
    /// the caller should return.
    /// </summary>
    /// <param name="targetPath">The path that must not exist.</param>
    /// <returns>
    /// <c>null</c> when the target is confirmed absent (carry on);
    /// <see cref="NewDatabaseOutcome.AlreadyExists"/> when a file is there;
    /// <see cref="NewDatabaseOutcome.Unreachable"/> when the location did not answer.
    /// </returns>
    private static async Task<NewDatabaseResult?> ProbeTargetAsync(string targetPath)
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

    /// <summary>Deadline-bounded existence check used to classify a refused no-overwrite move.</summary>
    /// <param name="path">File to test.</param>
    /// <returns>True only when the location answered and the file is there.</returns>
    private static async Task<bool> TargetExistsAsync(string path)
        => await NetworkFileOps.ProbeFileAsync(path).ConfigureAwait(false) == FileProbeResult.Exists;

    /// <summary>Logs a stall and returns the matching <see cref="NewDatabaseOutcome.Unreachable"/> result.</summary>
    /// <param name="what">Which step stalled (log text only).</param>
    private static NewDatabaseResult UnreachableResult(string what)
    {
        App.Logger.LogInfo($"NewDatabaseFile: {what} — location unreachable");
        return new NewDatabaseResult(NewDatabaseOutcome.Unreachable);
    }

    /// <summary>
    /// Builds a finished, ready-to-ship database at the LOCAL path <paramref name="localPath"/>:
    /// schema, migrations and seed data via <see cref="DatabaseContext"/>, then normalised to
    /// rollback-journal mode with no WAL sidecars.
    /// </summary>
    /// <param name="localPath">A local, not-yet-existing file path.</param>
    private static void BuildLocalFile(string localPath)
    {
        // DatabaseContext opens in WAL mode and creates the schema + seed data. Its Dispose()
        // closes the connection and clears the connection pool, so no handle is left on the file.
        using (new DatabaseContext(localPath)) { }

        // WAL must never be active on a network file (the shipped file must be a self-contained
        // single .db). Same normalisation CheckoutService.BackupSqliteDatabase applies to D.
        // Pooling=False is essential: a pooled connection would keep the file open after Dispose.
        using (var conn = new SqliteConnection($"Data Source={localPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=DELETE";
            cmd.ExecuteScalar();
        }

        // Closing the last connection normally removes these already; make sure none can ship.
        DeleteWalSidecars(localPath);
    }

    /// <summary>Deletes the temporary local database and every sidecar SQLite/DatabaseContext may have made. Non-throwing.</summary>
    /// <param name="localPath">The temporary local database path.</param>
    private static void DeleteLocalFiles(string localPath)
    {
        TryDelete(localPath);
        DeleteWalSidecars(localPath);
        TryDelete(localPath + "-journal");
        TryDelete(localPath + ".pre-migration.bak");
    }

    /// <summary>Deletes the <c>-wal</c> and <c>-shm</c> sidecars of <paramref name="dbPath"/>. Non-throwing.</summary>
    /// <param name="dbPath">Database whose sidecars to remove.</param>
    private static void DeleteWalSidecars(string dbPath)
    {
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }

    /// <summary>Best-effort local file deletion; failures are ignored.</summary>
    /// <param name="path">File to delete.</param>
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort — temp file in a temp folder */ }
    }
}
