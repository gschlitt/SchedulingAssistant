using System.Diagnostics;

namespace TermPoint.Services;

/// <summary>
/// One closed run of failures that ended in a success (spec 1.2.4 item 7.2): what <see cref="FailureStreakTracker.RecordSuccess"/>
/// hands back so the caller can report "recovered after failing". Every value is a number or a fixed kind name, so it
/// is safe to send to the developers.
/// </summary>
/// <param name="FailedAttempts">How many attempts failed in a row before the success (at least 1).</param>
/// <param name="FailedSeconds">Whole seconds from the first failure to the success.</param>
/// <param name="FirstKind">The kind of the first failure of the spell.</param>
/// <param name="LastKind">The kind of the last failure of the spell.</param>
/// <param name="SpellInRun">1 for the first spell this tracker closed, 2 for the second, and so on.</param>
public sealed record FailureSpell(int FailedAttempts, long FailedSeconds, string FirstKind, string LastKind, int SpellInRun);

/// <summary>
/// Remembers a run of failures of one repeated action (a save, a reader's Refresh) so that the first success after it
/// can be reported as a recovery (spec 1.2.4 item 7.2). Pure and reusable: it knows nothing about saving or reporting.
///
/// <para><b>Thread-safe</b> (one <c>lock</c>). <b>Timestamps</b> are <see cref="Stopwatch.GetTimestamp"/> values
/// passed in by the caller (real time on purpose: it measures the share, not a test clock; tests pass their own).
/// <b>Never throws.</b></para>
/// </summary>
public sealed class FailureStreakTracker
{
    private readonly object _gate = new();

    /// <summary>How many failures the open spell has (0 when no spell is open).</summary>
    private int _attempts;

    /// <summary>Timestamp of the open spell's first failure.</summary>
    private long _firstTimestamp;

    /// <summary>Kind of the open spell's first failure.</summary>
    private string _firstKind = "";

    /// <summary>Kind of the open spell's latest failure.</summary>
    private string _lastKind = "";

    /// <summary>How many spells have been closed by <see cref="RecordSuccess"/> so far.</summary>
    private int _spellsClosed;

    /// <summary>True while a spell is open (at least one failure and no success since).</summary>
    public bool IsSpellOpen { get { lock (_gate) return _attempts > 0; } }

    /// <summary>Failures in the open spell (0 when none is open).</summary>
    public int OpenAttempts { get { lock (_gate) return _attempts; } }

    /// <summary>
    /// Records one failed attempt. The first failure of a spell remembers its kind and time; every failure bumps the
    /// count and sets the last kind.
    /// </summary>
    /// <param name="kind">A fixed name for what failed (never message text or a path).</param>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value for now.</param>
    public void RecordFailure(string kind, long timestamp)
    {
        lock (_gate)
        {
            if (_attempts == 0)
            {
                _firstTimestamp = timestamp;
                _firstKind      = kind;
            }
            _attempts++;
            _lastKind = kind;
        }
    }

    /// <summary>
    /// Records one successful attempt. When a spell was open it is closed and returned, and the tracker resets.
    /// </summary>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value for now.</param>
    /// <returns>The closed spell, or null when no failures preceded this success.</returns>
    public FailureSpell? RecordSuccess(long timestamp)
    {
        lock (_gate)
        {
            if (_attempts == 0)
                return null;

            var seconds = Math.Max(0L, (long)Stopwatch.GetElapsedTime(_firstTimestamp, timestamp).TotalSeconds);
            var spell = new FailureSpell(_attempts, seconds, _firstKind, _lastKind, ++_spellsClosed);
            ResetOpenSpell();
            return spell;
        }
    }

    /// <summary>
    /// Closes an open spell without a recovery (the lock was lost, the database changed elsewhere, the reader became a
    /// writer). The spell does not count toward <see cref="FailureSpell.SpellInRun"/>.
    /// </summary>
    /// <param name="why">A fixed name for the reason; kept only for the caller's own use and readability.</param>
    /// <returns>True when a spell was open; false when there was nothing to close.</returns>
    public bool Abandon(string why)
    {
        lock (_gate)
        {
            if (_attempts == 0)
                return false;
            ResetOpenSpell();
            return true;
        }
    }

    /// <summary>Clears the open spell. Call only while holding <see cref="_gate"/>.</summary>
    private void ResetOpenSpell()
    {
        _attempts       = 0;
        _firstTimestamp = 0;
        _firstKind      = "";
        _lastKind       = "";
    }
}
