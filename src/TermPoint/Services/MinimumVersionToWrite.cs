using Microsoft.Data.Sqlite;

namespace TermPoint.Services;

/// <summary>
/// MinimumVersionToWrite is not the database's version, and not the version that last wrote it. It is the oldest TermPoint allowed to edit this database; older versions open it read-only. It only ever goes up. It is raised only when a release answers Yes to: 'Could the new version save anything the old version wouldn't have?'
///
/// <para><b>Where it lives:</b> as the <c>MinimumVersionToWrite</c> row of the database's
/// <c>AppConfiguration</c> key/value table (<see cref="ConfigKey"/>), with an optional one-line
/// plain-English explanation under <see cref="ReasonKey"/>. A database with no such row is
/// "unstamped" and opens normally in every version — the gate can never make a normal database
/// read-only. A file that could not be READ at all (locked, held by another program) is a different
/// answer from "unstamped" (<see cref="StampReadKind.Unreadable"/>, spec item 3, step 3c) and is never
/// waved through: the gate retries quietly and, if it still cannot look, refuses write access. A file
/// SQLite reports as damaged is a third answer (<see cref="StampReadKind.Damaged"/>): the gate leaves
/// it to the open-time integrity check, which refuses it and handles the damage.</para>
///
/// <para><b>How it is enforced:</b> <see cref="CheckoutService"/> reads the stamp from the freshly
/// copied working copy D' at each write-access exit and, if <see cref="Blocks"/> says the running
/// app is older than the stamp, abandons the write checkout and opens a read-only snapshot instead
/// (see <c>CheckoutService.ApplyVersionGateAsync</c>). It runs again on the new D' whenever the
/// open-time integrity handler replaces D' with a fresh copy of D. Versions older than 1.2.3 do not
/// know the value exists and ignore it.</para>
///
/// <para><b>How it is raised:</b> only through <see cref="Raise"/>, called from the checkout once a
/// release has set <see cref="ThisReleaseRequires"/>, and only by <see cref="InstallChannel.Store"/>
/// and <see cref="InstallChannel.DirectDownload"/> builds. The stamp is written into D', so it
/// reaches D in the same save as the data it protects. A raise that keeps failing refuses write
/// access (a session that cannot stamp must not save new-format data where older versions can edit
/// it).</para>
///
/// <para>Desktop-only: it uses <c>Microsoft.Data.Sqlite</c>, which the browser build does not
/// reference (the file is excluded from the browser build in the project file).</para>
/// </summary>
public static class MinimumVersionToWrite
{
    // ── Configuration keys ────────────────────────────────────────────────────

    /// <summary>
    /// The <c>AppConfiguration.key</c> under which the minimum writer version is stored. The value
    /// is a dotted version such as <c>1.2.5</c>.
    /// </summary>
    public const string ConfigKey = "MinimumVersionToWrite";

    /// <summary>
    /// The <c>AppConfiguration.key</c> holding the optional one-line, plain-English reason the
    /// stamp was raised (e.g. "Section-type colours are now saved."). Informational only — nothing
    /// in 1.2.3 reads it back; it exists so a support person looking at the table can see why.
    /// </summary>
    public const string ReasonKey = "MinimumVersionToWrite.Reason";

    // ── What THIS release requires (dormant in 1.2.3) ─────────────────────────

    /// <summary>
    /// The stamp this build writes into every database it edits: the version of the release that
    /// introduced the newest data this build can save. <b>Null in 1.2.3: the check ships, the
    /// raising does not.</b> (Full procedure: <c>UPDATE_NUMBERING_README.md</c> at the repo root.)
    ///
    /// <para><b>Once set, it stays set in every later release</b> — never back to null, never
    /// lower. A later release still saves that newer data, so it must keep stamping at least that
    /// version. Change it again only when a later release answers Yes again.</para>
    ///
    /// <para><b>The deciding question for every release:</b> "Could the new version save anything
    /// the old version wouldn't have?" Answer Yes when EITHER</para>
    /// <list type="bullet">
    ///   <item>the same starting database plus the same edits would give a different saved result, OR</item>
    ///   <item>the new version offers an edit the old one can't make.</item>
    /// </list>
    ///
    /// <para><b>Yes:</b> section-type colours (a new saved field); a fix that stops an edit wiping a
    /// field (1.2.3's Flag/Capacity fixes: same edit, different record); a CSV import fix (same file,
    /// different records).<br/>
    /// <b>No:</b> card layout, scrolling, a crash fix in a view, performance work.</para>
    ///
    /// <para><b>When the answer is Yes:</b> set this to this release's full version (e.g.
    /// <c>"1.2.5"</c>) and set <see cref="ThisReleaseReason"/> to a one-line plain-English reason.
    /// The value is written only by <see cref="InstallChannel.Store"/> and
    /// <see cref="InstallChannel.DirectDownload"/> builds — never by Development or Sideload
    /// builds — so a developer's copy can never lock a department out of its database. Versions
    /// older than 1.2.3 ignore the value entirely.</para>
    ///
    /// <para><b>Caution when testing a release that sets this:</b> a locally installed MSIX built
    /// with the Store identity counts as a Store install and WILL stamp. Test such a build on
    /// database copies only, never on a department's live database.</para>
    ///
    /// <para>A test pins this to null for 1.2.3; if you set it deliberately, update that test.</para>
    /// </summary>
    public const string? ThisReleaseRequires = null;

    /// <summary>
    /// One-line, plain-English reason stored beside the stamp when it is raised (see
    /// <see cref="ThisReleaseRequires"/>). Describes the most recent raise; like
    /// <see cref="ThisReleaseRequires"/> it is changed only when a release raises the stamp again,
    /// never cleared. Null in 1.2.3.
    /// </summary>
    public const string? ThisReleaseReason = null;

    // ── Store deep link ───────────────────────────────────────────────────────

    /// <summary>
    /// URI that opens the Microsoft Store's Downloads and updates page (the Library page in newer
    /// Store versions). Launched by the banner's "Open Microsoft Store" button for
    /// <see cref="InstallChannel.Store"/> installs.
    ///
    /// <para>Chosen over TermPoint's own product page (<c>ms-windows-store://pdp/?PFN=…</c>)
    /// because this page has a "Get updates" button that makes the Store check right away — a user
    /// who hits the banner usually has an update the Store has not fetched yet, and the product
    /// page then just shows "Open" with no hint of what to do (user check on a Store install,
    /// 2026-10-11). The banner text names the button (see <see cref="BuildMessage"/>).</para>
    /// </summary>
    public const string StoreUri = "ms-windows-store://downloadsandupdates";

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <see cref="ConfigKey"/> value from the SQLite file at <paramref name="dbPath"/>
    /// without modifying it (read-only connection, no pooling, so no handle is left open), and says
    /// HOW the read ended — <see cref="StampReadKind"/> — instead of collapsing "there is no stamp"
    /// and "I could not look" into one null (spec item 3, step 3c). The old null-on-any-error read
    /// let a locked or damaged working copy (antivirus holding a file that was just copied) pass
    /// the version gate as if it were unstamped.
    ///
    /// <para><b>Classification:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="StampReadKind.Found"/> — the file opened, the table and key
    ///         exist and the value is not blank.</description></item>
    ///   <item><description><see cref="StampReadKind.None"/> — the file is genuinely unstamped: it
    ///         opened and has no <c>AppConfiguration</c> table, no such key, or a blank value; OR it
    ///         has no SQLite header at all (it cannot carry a stamp, and the integrity check of the
    ///         local copy refuses such a file moments later), which also keeps the dummy
    ///         non-SQLite files that tests use for D working.</description></item>
    ///   <item><description><see cref="StampReadKind.Unreadable"/> — the file could not be examined:
    ///         it is missing, held by another program (sharing violation), access is denied, an I/O
    ///         error occurred, or SQLite could not get at it (locked, busy, can't open). The caller
    ///         must NOT treat this as "no stamp".</description></item>
    ///   <item><description><see cref="StampReadKind.Damaged"/> — SQLite opened the file and said its
    ///         contents are bad (corrupt, or "not a database" behind a valid header), classified exactly
    ///         as the open-time integrity check classifies it
    ///         (<see cref="BackupService.ClassifyIntegrityException"/>), so the two can never disagree.
    ///         Typical when the first page or the <c>AppConfiguration</c> page is damaged. Waiting cannot
    ///         fix it; the caller hands the file on to the integrity check, which refuses it.</description></item>
    /// </list>
    ///
    /// <para>Never throws and never creates the file (the connection is read-only).</para>
    /// </summary>
    /// <param name="dbPath">Path to a local SQLite file, normally the working copy D'.</param>
    /// <returns>The classified read; <see cref="StampRead.Value"/> is the trimmed stamp for Found.</returns>
    public static StampRead ReadStamp(string dbPath)
    {
        // Step 1 — the 16-byte header, read with sharing so it coexists with any other reader. A
        // failure here (missing file, sharing violation, access denied, I/O error) is "could not
        // look". A file without the SQLite signature has no stamp to find and is not SQLite's to open.
        try
        {
            if (!DatabaseValidator.HasSqliteSignature(dbPath))
                return StampRead.None;
        }
        catch (Exception ex)
        {
            return StampRead.Couldnt(ex);
        }

        // Step 2 — the table and the key. Probing sqlite_master first (rather than catching "no such
        // table") keeps "no table" a plain answer and leaves every exception meaning either "could not
        // look" or "the file is damaged" (StampRead.FromFailure tells them apart).
        try
        {
            using var conn = new SqliteConnection(BuildConnectionString(dbPath, SqliteOpenMode.ReadOnly));
            conn.Open();

            using (var probe = conn.CreateCommand())
            {
                probe.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'AppConfiguration'";
                if (probe.ExecuteScalar() is null)
                    return StampRead.None;
            }

            var value = ReadValue(conn, transaction: null, ConfigKey);
            return value is null ? StampRead.None : StampRead.Found(value);
        }
        catch (Exception ex)
        {
            return StampRead.FromFailure(ex);
        }
    }

    /// <summary>
    /// Decides whether a stamp stops the running app from editing.
    ///
    /// <para>A null/blank stamp never blocks. A stamp that does not parse as a version blocks —
    /// fail safe: an app that cannot understand the requirement must not write. Otherwise the
    /// app is blocked when it is older than the stamp. Both sides are normalized to four
    /// components (an undefined build/revision counts as 0) so <c>"1.2.3"</c> equals
    /// <c>1.2.3.0</c> in both directions.</para>
    /// </summary>
    /// <param name="stamp">The stored value (<see cref="StampRead.Value"/> of a <see cref="ReadStamp"/> result); null when there is none.</param>
    /// <param name="appVersion">The running app's version.</param>
    /// <returns>True when the database must open read-only.</returns>
    public static bool Blocks(string? stamp, Version appVersion)
    {
        if (string.IsNullOrWhiteSpace(stamp)) return false;
        if (!Version.TryParse(stamp.Trim(), out var required)) return true;
        return Normalize(appVersion) < Normalize(required);
    }

    // ── Raising ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Raises the stamp in the SQLite file at <paramref name="dbPath"/> to
    /// <paramref name="version"/>. Never lowers it: the value is written only when no stamp exists
    /// yet, or when both the existing stamp and <paramref name="version"/> parse and the new one
    /// is strictly higher. An existing stamp that does not parse is left alone (it already blocks
    /// everything older than anything, which is the safe state). Runs in one transaction and
    /// creates the <c>AppConfiguration</c> table first if the file lacks it. The reason is touched
    /// only if the version was actually raised: it is replaced, or, when <paramref name="reason"/>
    /// is null, an older reason is removed so it never describes a stamp it doesn't belong to.
    /// </summary>
    /// <param name="dbPath">Path to a local SQLite file — the working copy D', before the app opens it.</param>
    /// <param name="version">The version to require, e.g. <c>"1.2.5"</c>.</param>
    /// <param name="reason">One-line plain-English reason, or null to store none.</param>
    /// <returns>True when the stamp was written; false when the existing stamp already covered it.</returns>
    /// <exception cref="ArgumentException"><paramref name="version"/> is not a parseable version.</exception>
    /// <exception cref="SqliteException">The file is not a writable SQLite database. The caller logs.</exception>
    public static bool Raise(string dbPath, string version, string? reason)
    {
        if (!Version.TryParse(version?.Trim(), out var requested))
            throw new ArgumentException($"'{version}' is not a valid version.", nameof(version));

        // Mode=ReadWrite (not ReadWriteCreate): a missing file must fail, never be conjured as an empty DB.
        using var conn = new SqliteConnection(BuildConnectionString(dbPath, SqliteOpenMode.ReadWrite));
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Same schema as DatabaseContext, so a stamped file stays interchangeable with a normal one.
        using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS AppConfiguration (" +
                "key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '')";
            create.ExecuteNonQuery();
        }

        var existing = ReadValue(conn, tx, ConfigKey);
        var shouldWrite = string.IsNullOrWhiteSpace(existing)
            || (Version.TryParse(existing.Trim(), out var current) && Normalize(requested) > Normalize(current));

        if (!shouldWrite)
        {
            tx.Rollback();
            return false;
        }

        WriteValue(conn, tx, ConfigKey, requested.ToString());

        // The reason always describes the CURRENT stamp: replace it, or remove a stale one left
        // by an earlier raise so it can't be mistaken for this raise's explanation.
        if (reason is not null)
            WriteValue(conn, tx, ReasonKey, reason);
        else
            DeleteValue(conn, tx, ReasonKey);

        tx.Commit();
        return true;
    }

    // ── Banner wording ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the read-only banner text shown when a database's stamp blocks this app. The advice
    /// depends on how the app was installed: Store installs are told to click "Get updates" in the
    /// Microsoft Store (the banner's button opens that page, see <see cref="StoreUri"/>); every
    /// other channel is told to install the update from its usual source.
    ///
    /// <para>This wording ships frozen in every release that carries it and may first be seen
    /// years later, so keep it independent of anything that could change (no release-specific
    /// details beyond the required version).</para>
    /// </summary>
    /// <param name="channel">How this copy of TermPoint was installed.</param>
    /// <param name="stamp">The database's stamp. If it is not a parseable version the text says
    /// "a newer version of TermPoint" rather than echoing it.</param>
    /// <returns>The banner message, beginning "Read-only —".</returns>
    public static string BuildMessage(InstallChannel channel, string stamp)
    {
        var trimmed = stamp?.Trim() ?? string.Empty;
        var needs = Version.TryParse(trimmed, out _)
            ? $"TermPoint {trimmed} or later"
            : "a newer version of TermPoint";

        return channel == InstallChannel.Store
            ? $"Read-only — this database needs {needs} to edit. In the Microsoft Store, click Get updates, then restart TermPoint."
            : $"Read-only — this database needs {needs} to edit. Install the update from your usual source, then restart TermPoint.";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a connection string for a single short-lived, non-pooled connection. Pooling is off
    /// so the file handle is released on dispose (a pooled handle would block the later
    /// <c>File.Delete</c>/<c>Move</c> of D').
    /// </summary>
    /// <param name="dbPath">Path to the SQLite file.</param>
    /// <param name="mode">Open mode (read-only for probing, read-write for raising).</param>
    private static string BuildConnectionString(string dbPath, SqliteOpenMode mode)
        => new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode       = mode,
            Pooling    = false,
        }.ToString();

    /// <summary>
    /// Pads a version to four components, treating undefined build/revision (-1) as 0, so that
    /// <c>1.2.3</c> and <c>1.2.3.0</c> compare equal.
    /// </summary>
    /// <param name="v">The version to normalize.</param>
    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>
    /// Reads one <c>AppConfiguration</c> value on an open connection.
    /// </summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="transaction">Transaction to enlist in, or null.</param>
    /// <param name="key">The configuration key.</param>
    /// <returns>The trimmed value, or null when the row is missing or blank.</returns>
    private static string? ReadValue(SqliteConnection conn, SqliteTransaction? transaction, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var trimmed = (cmd.ExecuteScalar() as string)?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Upserts one <c>AppConfiguration</c> row.
    /// </summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="transaction">Transaction to enlist in.</param>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value to store.</param>
    private static void WriteValue(SqliteConnection conn, SqliteTransaction transaction, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes one <c>AppConfiguration</c> row, if present.
    /// </summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="transaction">Transaction to enlist in.</param>
    /// <param name="key">The configuration key.</param>
    private static void DeleteValue(SqliteConnection conn, SqliteTransaction transaction, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM AppConfiguration WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// How a read of a database's <see cref="MinimumVersionToWrite"/> stamp ended (spec item 3, step 3c).
/// </summary>
public enum StampReadKind
{
    /// <summary>The stamp was read; <see cref="StampRead.Value"/> holds it.</summary>
    Found,

    /// <summary>
    /// The file was examined (or has no SQLite header, so it cannot carry a stamp) and there is no stamp:
    /// no <c>AppConfiguration</c> table, no such key, or a blank value. The database is genuinely
    /// unstamped and opens normally in every version.
    /// </summary>
    None,

    /// <summary>
    /// The file could NOT be examined — missing, held by another program, access denied, an I/O error, or
    /// SQLite could not get at it (locked, busy, can't open). Says nothing about whether a stamp exists, so
    /// the version gate must not let write access through on the strength of it. Often clears by itself.
    /// </summary>
    Unreadable,

    /// <summary>
    /// SQLite opened the file and reported its contents as bad — SQLITE_CORRUPT ("database disk image is
    /// malformed"), SQLITE_NOTADB or SQLITE_FORMAT — exactly the errors the open-time integrity check calls
    /// damage (<see cref="BackupService.ClassifyIntegrityException"/>). Waiting cannot fix it. The version
    /// gate does not refuse on it: it lets the open-time integrity check, which runs next on every path into
    /// editing, refuse the file and handle the damage (fresh copy, recovery chooser, report). A damaged file
    /// can never be edited, so its stamp never needs to be known.
    /// </summary>
    Damaged
}

/// <summary>
/// The result of <see cref="MinimumVersionToWrite.ReadStamp"/>: a classified answer instead of a nullable
/// string, so "there is no stamp", "I could not look" and "the file is damaged" can never be mistaken for
/// each other.
/// </summary>
/// <param name="Kind">How the read ended.</param>
/// <param name="Value">The trimmed stamp when <paramref name="Kind"/> is <see cref="StampReadKind.Found"/>; otherwise null.</param>
/// <param name="Detail">
/// For <see cref="StampReadKind.Unreadable"/> and <see cref="StampReadKind.Damaged"/>: a short technical account
/// of why (exception type and message), written to the local log only. Never shown to the user and never
/// reported. Null otherwise.
/// </param>
public readonly record struct StampRead(StampReadKind Kind, string? Value = null, string? Detail = null)
{
    /// <summary>The answer for a genuinely unstamped file.</summary>
    public static StampRead None => new(StampReadKind.None);

    /// <summary>
    /// The answer for a stamp that was read.
    /// </summary>
    /// <param name="value">The stamp (already trimmed and non-blank).</param>
    /// <returns>A <see cref="StampReadKind.Found"/> result carrying <paramref name="value"/>.</returns>
    public static StampRead Found(string value) => new(StampReadKind.Found, value);

    /// <summary>
    /// The answer for a file that could not be examined, with the reason for the log.
    /// </summary>
    /// <param name="detail">Short, log-only description of why.</param>
    /// <returns>An <see cref="StampReadKind.Unreadable"/> result.</returns>
    public static StampRead Couldnt(string detail) => new(StampReadKind.Unreadable, null, detail);

    /// <summary>
    /// The answer for a file that could not be examined because of <paramref name="ex"/>.
    /// </summary>
    /// <param name="ex">The failure; only its type and message go into <see cref="Detail"/>.</param>
    /// <returns>An <see cref="StampReadKind.Unreadable"/> result.</returns>
    public static StampRead Couldnt(Exception ex) => Couldnt(Describe(ex));

    /// <summary>
    /// The answer for a file SQLite reported as damaged, with the reason for the log.
    /// </summary>
    /// <param name="ex">The SQLite error; only its type and message go into <see cref="Detail"/>.</param>
    /// <returns>A <see cref="StampReadKind.Damaged"/> result.</returns>
    public static StampRead DamagedFile(Exception ex) => new(StampReadKind.Damaged, null, Describe(ex));

    /// <summary>
    /// Classifies a failure of the SQLite part of the read with the open-time integrity check's own rule
    /// (<see cref="BackupService.ClassifyIntegrityException"/>): only a SQLite error that positively says the
    /// contents are bad (corrupt, not a database) is <see cref="StampReadKind.Damaged"/>; everything else —
    /// locked, busy, can't open, I/O errors, any other exception — is <see cref="StampReadKind.Unreadable"/>.
    /// </summary>
    /// <param name="ex">The exception caught while opening the file or reading the stamp.</param>
    /// <returns>A <see cref="StampReadKind.Damaged"/> or <see cref="StampReadKind.Unreadable"/> result.</returns>
    public static StampRead FromFailure(Exception ex) =>
        BackupService.ClassifyIntegrityException(ex) == IntegrityCheckResult.Damaged
            ? DamagedFile(ex)
            : Couldnt(ex);

    /// <summary>True when the file could not be examined (<see cref="StampReadKind.Unreadable"/>).</summary>
    public bool IsUnreadable => Kind == StampReadKind.Unreadable;

    /// <summary>True when SQLite reported the file as damaged (<see cref="StampReadKind.Damaged"/>).</summary>
    public bool IsDamaged => Kind == StampReadKind.Damaged;

    /// <summary>Short, log-only description of an exception: its type name and message.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>"TypeName: message".</returns>
    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
