using TermPoint.Services;

namespace TermPoint.Exceptions;

/// <summary>
/// The named exception of a DAMAGE REPORT (spec item 5, step 5e): it is never thrown to anyone — the damaged
/// database was already refused and the user already told — it exists so BugSnag receives an error that has a name
/// and groups sensibly. <see cref="DatabaseDamageReporter"/> creates it and hands it to
/// <see cref="IAppLogger.ReportHandled"/>.
///
/// <para><b>The message is stable per kind</b> (see <see cref="MessageFor"/>) and never contains a path, a file
/// name, a user name or SQLite's summary, so BugSnag shows one issue per kind of file. The summary travels in the
/// report's metadata instead (already sanitized by the reporter), and is also kept on <see cref="Summary"/>.</para>
/// </summary>
public sealed class DatabaseDamagedException : Exception
{
    /// <summary>Which database file was found damaged.</summary>
    public DamagedFileKind Kind { get; }

    /// <summary>
    /// SQLite's short, sanitized account of the damage (the first lines of <c>integrity_check</c> output, or its
    /// code and message), or null when none is available. Names pages and tables; never data and never a path.
    /// </summary>
    public string? Summary { get; }

    /// <param name="kind">Which database file was found damaged.</param>
    /// <param name="summary">The sanitized SQLite summary, or null.</param>
    public DatabaseDamagedException(DamagedFileKind kind, string? summary = null)
        : base(MessageFor(kind))
    {
        Kind    = kind;
        Summary = summary;
    }

    /// <summary>
    /// The exception message for <paramref name="kind"/>: fixed text, so every report of one kind reads — and
    /// groups — the same.
    /// </summary>
    /// <param name="kind">The kind of file.</param>
    /// <returns>The message, e.g. "Damaged shared database detected by the integrity check".</returns>
    public static string MessageFor(DamagedFileKind kind) => kind switch
    {
        DamagedFileKind.SharedDatabase      => "Damaged shared database detected by the integrity check",
        DamagedFileKind.LocalCopy           => "Damaged local copy of the database detected by the integrity check",
        DamagedFileKind.BackupSnapshot      => "Damaged backup snapshot detected by the integrity check",
        DamagedFileKind.BackupBeingRestored => "Damaged backup being restored detected by the integrity check",
        _                                   => "Damaged database detected by the integrity check"
    };
}
