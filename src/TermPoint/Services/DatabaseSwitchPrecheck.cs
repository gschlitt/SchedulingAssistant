using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// The outcome of <see cref="DatabaseSwitchPrecheck"/>: either "go ahead and switch", or "refuse,
/// and tell the user this".
/// </summary>
/// <param name="CanProceed">True when the switch may begin.</param>
/// <param name="Title">Dialog title for the refusal (empty when <paramref name="CanProceed"/>).</param>
/// <param name="Message">User-facing refusal text (empty when <paramref name="CanProceed"/>).</param>
public sealed record SwitchPrecheckResult(bool CanProceed, string Title = "", string Message = "")
{
    /// <summary>The "go ahead" result.</summary>
    public static SwitchPrecheckResult Proceed { get; } = new(true);
}

/// <summary>
/// Decides whether a database switch (Recent…, File → Open, File → New, "Switch to edit mode", the
/// post-wizard open) may begin — BEFORE anything is torn down.
///
/// <para><b>Why:</b> <c>MainWindow.SwitchDatabaseAsync</c> used to write the settings, save and
/// dispose the current database, and only then discover that the target could not be opened: a file
/// held open by another program, a damaged file, an unreachable share — and, worst, a missing file,
/// for which it silently created and opened a brand-new EMPTY database at the old path. The user was
/// left with a disconnected window, a leaked write lock and a technical error. This pre-check runs the
/// same validator the database chooser uses, first, so a refusal leaves the current database open and
/// the settings and recent list untouched.</para>
///
/// <para><b>Creating is explicit:</b> only File → New passes <c>createIfMissing: true</c>, for which a
/// missing file means "create it" and an EXISTING file is refused ("already exists" — File → New never
/// adopts a file). Every other caller treats a missing file as an error.</para>
///
/// <para>The decision itself (<see cref="Decide"/>) is pure and unit-tested; <see cref="RunAsync"/> only
/// adds the deadline-bounded <see cref="DatabaseValidator.ValidateAsync"/> call.</para>
/// </summary>
public static class DatabaseSwitchPrecheck
{
    /// <summary>Dialog title when the target is held open by another program / unreadable.</summary>
    public const string UnreadableTitle = "Database Can't Be Read";

    /// <summary>Dialog title when the target file does not exist.</summary>
    public const string MissingTitle = "Database Not Found";

    /// <summary>Dialog title when the target fails its integrity check.</summary>
    public const string DamagedTitle = "Database Integrity Check Failed";

    /// <summary>Dialog title when the target's location cannot be reached.</summary>
    public const string UnreachableTitle = "Network Unreachable";

    /// <summary>
    /// The message for a database file that does not exist. Also used by the checkout flow's
    /// "source vanished" dialog, so both say the same thing.
    /// </summary>
    /// <param name="path">Full path of the missing database.</param>
    /// <returns>The user-facing text, including the path.</returns>
    public static string MissingMessage(string path) =>
        $"The database file could not be found:\n{path}\n\n" +
        "It may have been moved, renamed or deleted.";

    /// <summary>Dialog title when File → New is aimed at a file that already exists.</summary>
    public const string AlreadyExistsTitle = "Database Already Exists";

    /// <summary>
    /// The message for File → New aimed at a file that already exists. Shared by the pre-check and by
    /// <c>SwitchDatabaseAsync</c>'s "someone created it while we were building ours" path
    /// (<see cref="NewDatabaseOutcome.AlreadyExists"/>), so both say the same thing.
    /// </summary>
    /// <param name="path">Full path of the existing database.</param>
    /// <returns>The user-facing text, including the path.</returns>
    public static string AlreadyExistsMessage(string path) =>
        $"A database file already exists at:\n{path}\n\n" +
        "It was not changed. Choose a different name, or open that database instead.";

    /// <summary>
    /// Validates <paramref name="path"/> with <see cref="DatabaseValidator.ValidateAsync"/> (all network
    /// I/O deadline-bounded) and applies <see cref="Decide"/>.
    /// </summary>
    /// <param name="path">Full path of the database to switch to.</param>
    /// <param name="createIfMissing">True only for File → New: a missing file is then created later.</param>
    /// <returns>Proceed, or a refusal with the title and message to show.</returns>
    public static async Task<SwitchPrecheckResult> RunAsync(string path, bool createIfMissing)
        => Decide(await DatabaseValidator.ValidateAsync(path), path, createIfMissing);

    /// <summary>
    /// <see cref="RunAsync"/> plus the bookkeeping that must happen ONLY when the pre-check passes:
    /// pointing <see cref="AppSettings.DatabasePath"/> at the target, saving the settings and adding
    /// the target to the recent list. A refusal touches none of them, so a failed switch leaves the
    /// settings exactly as they were. Kept here (not in the window) so that ordering is unit-testable.
    /// </summary>
    /// <param name="path">Full path of the database to switch to.</param>
    /// <param name="createIfMissing">True only for File → New.</param>
    /// <param name="settings">The settings to update on success (production: <c>AppSettings.Current</c>).</param>
    /// <returns>Proceed (settings updated), or a refusal (settings untouched).</returns>
    public static async Task<SwitchPrecheckResult> CheckAndRecordAsync(
        string path, bool createIfMissing, AppSettings settings)
    {
        var result = await RunAsync(path, createIfMissing);
        if (result.CanProceed)
        {
            settings.DatabasePath = path;
            settings.Save();
            settings.AddRecentDatabase(path);
        }
        return result;
    }

    /// <summary>
    /// Maps a validation result to proceed / refuse. Pure (no I/O), so it is directly unit-testable.
    ///
    /// <para><b>Opening an existing database</b> (<paramref name="createIfMissing"/> false):</para>
    /// <list type="bullet">
    /// <item><description>Ok → proceed.</description></item>
    /// <item><description>Missing → refuse: "database file could not be found".</description></item>
    /// <item><description>Unreadable → refuse with the chooser's "can't read it right now" wording — never "damaged".</description></item>
    /// <item><description>Corrupt → refuse: "failed its integrity check and may be damaged".</description></item>
    /// <item><description>Unreachable → refuse: the standard network message.</description></item>
    /// </list>
    ///
    /// <para><b>Creating a database</b> (<paramref name="createIfMissing"/> true, File → New): the
    /// opposite question — only a file that does NOT exist may proceed (the caller creates it).
    /// Unreachable still refuses with the network message. A file that already exists — healthy
    /// (Ok), damaged (Corrupt) or merely held open (Unreadable) — is refused as "already exists" and
    /// left untouched: File → New must never adopt an existing file, because the configuration
    /// transfer that follows would insert duplicates into it.</para>
    /// </summary>
    /// <param name="validation">The result of <see cref="DatabaseValidator.ValidateAsync"/>.</param>
    /// <param name="path">Full path of the target (shown in the "not found" / "already exists" messages).</param>
    /// <param name="createIfMissing">True only for File → New.</param>
    /// <returns>Proceed, or a refusal with the title and message to show.</returns>
    public static SwitchPrecheckResult Decide(
        DatabaseValidationResult validation, string path, bool createIfMissing)
    {
        if (createIfMissing)
        {
            return validation switch
            {
                DatabaseValidationResult.Missing => SwitchPrecheckResult.Proceed,
                DatabaseValidationResult.Unreachable =>
                    new(false, UnreachableTitle, NetworkFileOps.UnreachableMessage),
                // Ok, Corrupt, Unreadable — and any result added later: something is already there.
                _ => new(false, AlreadyExistsTitle, AlreadyExistsMessage(path))
            };
        }

        return validation switch
        {
            DatabaseValidationResult.Ok => SwitchPrecheckResult.Proceed,

            DatabaseValidationResult.Missing => new(false, MissingTitle, MissingMessage(path)),

            DatabaseValidationResult.Unreadable =>
                new(false, UnreadableTitle, DatabaseChooserViewModel.UnreadableDatabaseMessage),

            DatabaseValidationResult.Corrupt =>
                new(false, DamagedTitle, DatabaseChooserViewModel.DamagedDatabaseMessage),

            DatabaseValidationResult.Unreachable =>
                new(false, UnreachableTitle, NetworkFileOps.UnreachableMessage),

            // Defensive: a result added to the enum later must not silently be treated as "safe to
            // switch". Refuse with the neutral, retryable wording (it never claims damage or absence).
            _ => new(false, UnreadableTitle, DatabaseChooserViewModel.UnreadableDatabaseMessage)
        };
    }
}
