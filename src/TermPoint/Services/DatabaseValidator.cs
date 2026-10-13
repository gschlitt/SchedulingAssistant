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

    /// <summary>
    /// The file exists but is not a valid SQLite database or fails integrity check. Reserved for
    /// positive evidence of damage — a file that merely could not be read is
    /// <see cref="Unreadable"/>, never this.
    /// </summary>
    Corrupt,

    /// <summary>The file could not be checked because the network share is unreachable.</summary>
    Unreachable,

    /// <summary>
    /// The file exists but can't be read right now — another program has it open exclusively, or
    /// access is denied. Says nothing about the file's health: it must never be described to the
    /// user as damaged, and the user may well succeed on a retry.
    /// </summary>
    Unreadable
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
    /// a valid SQLite database or fails the integrity check (positive evidence of damage);
    /// <see cref="DatabaseValidationResult.Unreachable"/> when a network failure or
    /// timeout prevented the check from completing;
    /// <see cref="DatabaseValidationResult.Unreadable"/> when the file exists but could not be
    /// opened for the check (held exclusively by another program, access denied, I/O error) —
    /// never reported as Corrupt.
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

        // Tri-state integrity check: a file another program holds open (sharing violation) or that
        // we may not read is Unreadable, NOT Corrupt — a user told their database is damaged may
        // restore a backup over a perfectly good, current file. A timeout stays Unreachable.
        var (integrityCompleted, integrity) = await NetworkFileOps.CheckIntegrityDetailedAsync(path);
        if (!integrityCompleted)
            return DatabaseValidationResult.Unreachable;

        return integrity switch
        {
            IntegrityCheckResult.Ok         => DatabaseValidationResult.Ok,
            IntegrityCheckResult.Unreadable => DatabaseValidationResult.Unreadable,
            _                               => DatabaseValidationResult.Corrupt
        };
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
    /// <see cref="DatabaseValidationResult.Unreadable"/> when the file exists but the header read
    /// failed because another program holds it open exclusively (sharing/lock violation) or access
    /// is denied — never reported as Corrupt;
    /// <see cref="DatabaseValidationResult.Unreachable"/> when the probe or the header read failed
    /// for any other reason or stalled past the deadline.
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
            // The probe just succeeded, so a failure here is a read that died mid-flight.
            // Either way the file could not be checked — report that rather than guessing it is
            // damaged. A sharing/lock violation or access denied means the file is there but
            // another program holds it or we may not read it (Unreadable: retry may work); any
            // other I/O failure is most likely the share dropping (Unreachable).
            App.Logger.LogInfo($"DatabaseValidator: could not read database header: {ex.Message}");
            return IsLockedOrAccessDenied(ex)
                ? DatabaseValidationResult.Unreadable
                : DatabaseValidationResult.Unreachable;
        }
    }

    /// <summary>
    /// True when <paramref name="ex"/>, thrown while opening or reading a file that is known to
    /// exist, means "another program has it open" or "we may not read it" rather than "the
    /// location went away": an <see cref="UnauthorizedAccessException"/> (access denied), or an
    /// <see cref="IOException"/> whose HRESULT wraps Win32 <c>ERROR_SHARING_VIOLATION</c> (32) or
    /// <c>ERROR_LOCK_VIOLATION</c> (33).
    ///
    /// <para>Limitation: on macOS/Linux .NET emulates <see cref="FileShare"/> with advisory locks
    /// and reports a contended lock as an <see cref="IOException"/> whose HRESULT is a raw errno,
    /// which this does not recognise; such a failure stays <see cref="DatabaseValidationResult.Unreachable"/>
    /// (the previous behaviour). It is never reported as damaged either way.</para>
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns>True for access-denied and sharing/lock-violation failures.</returns>
    internal static bool IsLockedOrAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return true;

        if (ex is IOException io)
        {
            const int Win32Facility = unchecked((int)0x80070000);
            const int ErrorSharingViolation = 32;
            const int ErrorLockViolation    = 33;

            var hr = io.HResult;
            return (hr & unchecked((int)0xFFFF0000)) == Win32Facility
                && (hr & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
        }

        return false;
    }

    /// <summary>
    /// Reads the first 16 bytes of <paramref name="path"/> and compares them with
    /// <see cref="SqliteHeaderMagic"/>. Blocking I/O — call it only through a deadline wrapper.
    /// Opened <see cref="FileShare.ReadWrite"/> so it coexists with any other process using the file.
    /// </summary>
    /// <param name="path">File to inspect.</param>
    /// <returns>True when the file starts with the SQLite signature.</returns>
    /// <exception cref="IOException">The file could not be opened or read (including a sharing violation).</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file is denied.</exception>
    private static bool HasSqliteSignature(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return read == header.Length && header.SequenceEqual(SqliteHeaderMagic);
    }
}
