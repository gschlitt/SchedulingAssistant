using System.IO;

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
    Corrupt
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
    /// a valid SQLite database or fails the integrity check.
    /// </returns>
    public static DatabaseValidationResult Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return DatabaseValidationResult.Missing;

        return BackupService.CheckIntegrity(path)
            ? DatabaseValidationResult.Ok
            : DatabaseValidationResult.Corrupt;
    }
}
