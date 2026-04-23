namespace SchedulingAssistant.Services;

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
    /// null, empty, or the file does not exist;
    /// <see cref="DatabaseValidationResult.Corrupt"/> when the file exists but is not
    /// a valid SQLite database or fails the integrity check;
    /// <see cref="DatabaseValidationResult.Unreachable"/> when a network timeout
    /// prevented the check from completing.
    /// </returns>
    public static async Task<DatabaseValidationResult> ValidateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DatabaseValidationResult.Missing;

        var (existsCompleted, exists) = await NetworkFileOps.ExistsAsync(path);
        if (!existsCompleted)
            return DatabaseValidationResult.Unreachable;
        if (!exists)
            return DatabaseValidationResult.Missing;

        var (integrityCompleted, passed) = await NetworkFileOps.CheckIntegrityAsync(path);
        if (!integrityCompleted)
            return DatabaseValidationResult.Unreachable;

        return passed
            ? DatabaseValidationResult.Ok
            : DatabaseValidationResult.Corrupt;
    }
}
