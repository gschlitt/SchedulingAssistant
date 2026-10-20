using System.Text;
using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The abstract state of a generated run (spec item 9.3, step 9.3c): a short text that keeps what matters for what can
/// happen next and leaves out what does not. The exhaustive search remembers it after every event and does not go deeper
/// from a state it has already explored at an equal or smaller depth.
///
/// <para><b>What the key holds.</b></para>
/// <list type="bullet">
/// <item><description>Per computer: process alive; session (none, writer, reader) and whether it is on D or the other
/// database; network up or down; conflict banner showing; working file exists; marker exists; whether the shared
/// database's FILE still matches the fingerprint the computer's work started from (restoring depends on bytes, not on
/// edits); a pending "will offer to restore" promise, and which database it is about.</description></item>
/// <item><description>The lock files: who holds each (a computer's current session, a computer's earlier process, or a
/// stranger), and how old the heartbeat is, in buckets that follow the thresholds the lock service uses (a heartbeat 4
/// minutes old and one 30 seconds old are different states; 4 minutes and 10 minutes are the same).</description></item>
/// <item><description>The sets of edit ids in D, in each computer's live session and kept file, in each saved copy and in
/// each support archive.</description></item>
/// <item><description>Whether a snapshot is held, and which files sit on the share beside the databases.</description></item>
/// </list>
///
/// <para><b>What it deliberately leaves out</b> (so two runs that differ only in these are treated as the same
/// state): the fake clock's absolute time, counters inside the services (failed heartbeats, retries), the exact bytes of
/// the files, the story so far, and violations already found. This makes the search an approximation that can skip a
/// path whose outcome depends on one of those; the report says so.</para>
/// </summary>
internal static class AbstractState
{
    /// <summary>
    /// Computes the key from the world as it is after the last step. Reads the ledger the World captured at the end of
    /// that step, plus a few small files.
    /// </summary>
    /// <param name="ctx">The run.</param>
    /// <returns>The key.</returns>
    public static string Key(RunContext ctx)
    {
        var world  = ctx.World;
        var ledger = world.LastLedger;
        var sb     = new StringBuilder();
        var hashNow = world.CurrentDatabaseHash();

        foreach (var c in ctx.Computers)
        {
            sb.Append(c.Name).Append('[');
            sb.Append(c.IsRunning ? 'p' : '-');
            sb.Append(SessionCode(c));
            sb.Append(c.NetworkIsDown ? 'x' : 'n');
            sb.Append(c.ConflictBannerShown ? 'c' : '-');
            sb.Append(c.HasWorkingFile ? 'f' : '-');
            sb.Append(c.HasDirtyMarker ? 'm' : '-');
            var fingerprint = c.FingerprintOfWork();
            sb.Append(fingerprint is null ? '-' : string.Equals(fingerprint, hashNow, StringComparison.OrdinalIgnoreCase) ? '=' : '!');
            var pending = world.PendingPromiseCode(c);   // which database(s) a restore is promised for
            sb.Append(pending.Length == 0 ? "-" : "q" + pending);
            sb.Append(']');
        }

        sb.Append("|lockD=").Append(LockHolder(ctx, world.LockFilePath));
        sb.Append("|lockO=").Append(LockHolder(ctx, world.OtherLockFilePath));
        sb.Append("|snap=").Append(ctx.Snapshot is null ? '0' : '1');
        sb.Append("|share=").Append(string.Join(",", world.ShareFiles().Where(IsInteresting)));

        sb.Append("|D=").Append(Set(ledger.Shared));
        foreach (var c in ctx.Computers)
        {
            if (ledger.Live.TryGetValue(c.Name, out var live)) sb.Append('|').Append(c.Name).Append("live=").Append(Set(live));
            if (ledger.Kept.TryGetValue(c.Name, out var kept)) sb.Append('|').Append(c.Name).Append("kept=").Append(Set(kept.Edits));
            if (ledger.Archives.TryGetValue(c.Name, out var archived)) sb.Append('|').Append(c.Name).Append("arch=").Append(Set(archived));
        }
        foreach (var copy in ledger.SavedCopies.Values.Select(v => Set(v)).OrderBy(s => s, StringComparer.Ordinal))
            sb.Append("|copy=").Append(copy);

        return sb.ToString();
    }

    /// <summary>The session of a computer as one letter plus the database letter: "-" none, "W"/"R" writer/reader, then "d"/"o".</summary>
    /// <param name="c">The computer.</param>
    private static string SessionCode(Computer c)
    {
        if (!c.HasOpenSession) return "-";
        var mode = c.Checkout.Mode == CheckoutMode.WriteAccess ? "W" : "R";
        return mode + (c.SessionIsOnSharedDatabase ? "d" : "o");
    }

    /// <summary>A sorted, comma-separated set of edit ids; "null" when the database could not be read.</summary>
    /// <param name="edits">The ids, or null.</param>
    private static string Set(IEnumerable<string>? edits)
        => edits is null ? "null" : "{" + string.Join(",", edits.OrderBy(e => e, StringComparer.Ordinal)) + "}";

    /// <summary>True for a share file worth putting in the key (an orphaned temp file or a stray sidecar), not the databases and locks themselves.</summary>
    /// <param name="fileName">A file name on the share.</param>
    private static bool IsInteresting(string fileName)
        => !fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(fileName, "dept.db", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(fileName, "other.db", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Describes a lock file: "none"; "bob:live:a" (held by Bob's current session, heartbeat in age bucket a);
    /// "bob:old:c" (held by an earlier process of Bob's, heartbeat 3 minutes or older); "other:b" (held by a stranger);
    /// "?" (unreadable).
    /// </summary>
    /// <param name="ctx">The run.</param>
    /// <param name="lockPath">The lock file.</param>
    private static string LockHolder(RunContext ctx, string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath)) return "none";

            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var data = JsonSerializer.Deserialize<LockFileData>(reader.ReadToEnd());
            if (data is null) return "?";

            var age    = ctx.World.Clock.GetUtcNow().UtcDateTime - data.Heartbeat;
            var bucket = AgeBucket(age);
            var owner  = ctx.Computers.FirstOrDefault(c => c.Name == data.Username);
            if (owner is null) return $"other:{bucket}";
            return $"{owner.Name}:{(owner.Locks.SessionGuid == data.SessionGuid ? "live" : "old")}:{bucket}";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// Buckets a heartbeat's age by the lock service's own thresholds: fresh (less than a heartbeat interval), quiet
    /// (not yet stale), stale (past the stale threshold, inside the confirmation window) and long dead.
    /// </summary>
    /// <param name="age">How long ago the heartbeat was written, on the fake clock.</param>
    /// <returns>"a", "b", "c" or "d".</returns>
    private static char AgeBucket(TimeSpan age)
    {
        var seconds = age.TotalSeconds;
        if (seconds < WriteLockService.HeartbeatIntervalSeconds) return 'a';
        if (seconds < WriteLockService.StaleLockThresholdSeconds) return 'b';
        if (seconds < WriteLockService.StaleLockThresholdSeconds + WriteLockService.StaleConfirmationSeconds) return 'c';
        return 'd';
    }
}
