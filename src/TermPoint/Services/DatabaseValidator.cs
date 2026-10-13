namespace TermPoint.Services;

/// <summary>
/// The result of a database file validation check.
/// </summary>
public enum DatabaseValidationResult
{
    /// <summary>The file exists and passes SQLite integrity check.</summary>
    Ok,

    /// <summary>The path is null, empty, or the file does not exist on disk.</summary>
    Missing,

    /// <summary>The file exists but is not a valid SQLite database or fails integrity check.</summary>
    Corrupt,

    /// <summary>The file could not be checked because the network share is unreachable.</summary>
    Unreachable
}

/// <summary>
/// Static helper for validating a database file before it is opened by the application.
/// Called during startup routing and in the database-recovery flow.
/// </summary>
/// <remarks>
/// Deliberately has no DI dependencies — it must be callable during startup
/// before the service container has been built.
/// </remarks>
public static class DatabaseValidator
{
    /// <summary>
    /// Validates the database file at <paramref name="path"/>.
    /// Uses <see cref="NetworkFileOps"/> for timeout-aware I/O so the caller does not
    /// hang when the path is on an unreachable network share.
    /// </summary>
    /// <param name="path">
    /// Full path to the SQLite file to validate. May be <c>null</c> or empty.
    /// </param>
    /// <returns>
    /// <see cref="DatabaseValidationResult.Ok"/> when the file exists and passes
    /// <c>PRAGMA integrity_check</c>;
    /// <see cref="DatabaseValidationResult.Missing"/> when <paramref name="path"/> is
    /// null, empty, or the location responded that the file does not exist;
    /// <see cref="DatabaseValidationResult.Corrupt"/> when the file exists but is not
    /// a valid SQLite database or fails the integrity check;
    /// <see cref="DatabaseValidationResult.Unreachable"/> when a network failure or
    /// timeout prevented the check from completing.
    /// </returns>
    public static async Task<DatabaseValidationResult> ValidateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DatabaseValidationResult.Missing;

        // Tri-state probe: only report Missing when the location actually answered.
        // A dead share that fails fast must surface as Unreachable, not Missing,
        // or the user is routed to "database not found" UX during a network outage.
        var probe = await NetworkFileOps.ProbeFileAsync(path);
        if (probe == FileProbeResult.Unreachable)
            return DatabaseValidationResult.Unreachable;
        if (probe == FileProbeResult.Missing)
            return DatabaseValidationResult.Missing;

        var (integrityCompleted, passed) = await NetworkFileOps.CheckIntegrityAsync(path);
        if (!integrityCompleted)
            return DatabaseValidationResult.Unreachable;

        return passed
            ? DatabaseValidationResult.Ok
            : DatabaseValidationResult.Corrupt;
    }

    /// <summary>
    /// The 16-byte signature every SQLite database file starts with ("SQLite format 3" followed
    /// by a NUL). Anything else is not a SQLite database.
    /// </summary>
    internal static ReadOnlySpan<byte> SqliteHeaderMagic => "SQLite format 3\0"u8;

    /// <summary>
    /// Validates a database file at <paramref name="path"/> WITHOUT opening it with SQLite.
    ///
    /// <para><b>Why this exists:</b> <see cref="ValidateAsync"/> opens the file with SQLite to run
    /// <c>PRAGMA integrity_check</c>. For the shared database D that is exactly what must never
    /// happen: even a read-only open leaves a pooled file handle on D, which makes the active
    /// editor's save rename (D.tmp → D) fail, and any non-read-only open flips the journal-mode
    /// header and so changes D's hash (spec item 4). Use this method wherever D itself is being
    /// checked; the real integrity check runs later, on the local working copy.</para>
    ///
    /// <para>What it checks: the location answers (tri-state probe), the file exists, and its
    /// first 16 bytes are the SQLite signature. All network I/O is deadline-bounded via
    /// <see cref="NetworkFileOps"/>, and the file is opened with <see cref="FileShare.ReadWrite"/>
    /// so a concurrently active editor is never disturbed.</para>
    ///
    /// <para>Differences from <see cref="ValidateAsync"/>: no integrity check is run (a truncated or
    /// otherwise damaged file that still starts with the signature reports Ok here and is caught
    /// later, on the working copy); and a zero-byte file reports Corrupt, because it is not a
    /// TermPoint database someone can join, whereas SQLite itself treats an empty file as a valid
    /// empty database.</para>
    /// </summary>
    /// <param name="path">Full path to the file to validate. May be <c>null</c> or empty.</param>
    /// <returns>
    /// <see cref="DatabaseValidationResult.Ok"/> when the file exists and carries the SQLite
    /// signature (no integrity check is run);
    /// <see cref="DatabaseValidationResult.Missing"/> when <paramref name="path"/> is null/empty or
    /// the location answered that the file is absent;
    /// <see cref="DatabaseValidationResult.Corrupt"/> when the file exists but is not a SQLite
    /// database (wrong signature, or shorter than the signature);
    /// <see cref="DatabaseValidationResult.Unreachable"/> when the probe or the header read failed
    /// or stalled past the deadline.
    /// </returns>
    public static async Task<DatabaseValidationResult> ValidateWithoutSqliteAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DatabaseValidationResult.Missing;

        // Same tri-state rule as ValidateAsync: Missing only when the location actually answered.
        var probe = await NetworkFileOps.ProbeFileAsync(path);
        if (probe == FileProbeResult.Unreachable)
            return DatabaseValidationResult.Unreachable;
        if (probe == FileProbeResult.Missing)
            return DatabaseValidationResult.Missing;

        try
        {
            var (completed, hasSignature) = await NetworkFileOps.RunAsync(
                () => HasSqliteSignature(path), "ReadDatabaseHeader");
            if (!completed)
                return DatabaseValidationResult.Unreachable;

            return hasSignature
                ? DatabaseValidationResult.Ok
                : DatabaseValidationResult.Corrupt;
        }
        catch (Exception ex)
        {
            // The probe just succeeded, so a failure here is a read that died mid-flight
            // (share dropped, access revoked, file locked exclusively). The file could not
            // be checked — report it as such rather than guessing it is damaged.
            App.Logger.LogInfo($"DatabaseValidator: could not read database header: {ex.Message}");
            return DatabaseValidationResult.Unreachable;
        }
    }

    /// <summary>
    /// Reads the first 16 bytes of <paramref name="path"/> and compares them with
    /// <see cref="SqliteHeaderMagic"/>. Blocking I/O — call it only through a deadline wrapper.
    /// Opened <see cref="FileShare.ReadWrite"/> so it coexists with any other process using the file.
    /// </summary>
    /// <param name="path">File to inspect.</param>
    /// <returns>True when the file starts with the SQLite signature.</returns>
    /// <exception cref="IOException">The file could not be opened or read.</exception>
    private static bool HasSqliteSignature(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return read == header.Length && header.SequenceEqual(SqliteHeaderMagic);
    }
}
