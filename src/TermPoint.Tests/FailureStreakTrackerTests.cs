using System.Diagnostics;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec 1.2.4 item 7.2: <see cref="FailureStreakTracker"/> turns a run of failures followed by a success into one
/// <see cref="FailureSpell"/>. Timestamps are passed in, so no test waits.
/// </summary>
public sealed class FailureStreakTrackerTests
{
    /// <summary>Converts seconds to a <see cref="Stopwatch"/> timestamp distance.</summary>
    /// <param name="seconds">The seconds.</param>
    /// <returns>The same span in <see cref="Stopwatch.Frequency"/> ticks.</returns>
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    /// <summary>A success with no failure before it is nothing to report.</summary>
    [Fact]
    public void Success_WithNoSpell_ReturnsNull()
    {
        var tracker = new FailureStreakTracker();
        Assert.Null(tracker.RecordSuccess(Ticks(5)));
        Assert.False(tracker.IsSpellOpen);
    }

    /// <summary>N failures then a success: the spell carries the count, whole seconds and first and last kinds.</summary>
    [Fact]
    public void FailuresThenSuccess_ReturnsTheSpell()
    {
        var tracker = new FailureStreakTracker();
        tracker.RecordFailure("NotResponding", Ticks(10));
        tracker.RecordFailure("FileError", Ticks(20));
        tracker.RecordFailure("TornTransfer", Ticks(30));

        var spell = tracker.RecordSuccess(Ticks(52.9));

        Assert.NotNull(spell);
        Assert.Equal(3, spell!.FailedAttempts);
        Assert.Equal(42, spell.FailedSeconds);
        Assert.Equal("NotResponding", spell.FirstKind);
        Assert.Equal("TornTransfer", spell.LastKind);
        Assert.Equal(1, spell.SpellInRun);
        Assert.False(tracker.IsSpellOpen);
    }

    /// <summary>Each closed spell gets the next number; a success after a closed spell is again nothing.</summary>
    [Fact]
    public void SpellsAreNumbered_1_2_3()
    {
        var tracker = new FailureStreakTracker();
        for (var i = 1; i <= 3; i++)
        {
            tracker.RecordFailure("X", Ticks(i * 100));
            Assert.Equal(i, tracker.RecordSuccess(Ticks(i * 100 + 1))!.SpellInRun);
            Assert.Null(tracker.RecordSuccess(Ticks(i * 100 + 2)));
        }
    }

    /// <summary>Abandon closes an open spell without counting it, and says whether there was one.</summary>
    [Fact]
    public void Abandon_ClosesAnOpenSpell_WithoutRecovery()
    {
        var tracker = new FailureStreakTracker();
        Assert.False(tracker.Abandon("nothing open"));

        tracker.RecordFailure("X", Ticks(1));
        Assert.True(tracker.Abandon("LockLost"));
        Assert.False(tracker.IsSpellOpen);
        Assert.Null(tracker.RecordSuccess(Ticks(2)));

        // An abandoned spell is not a closed one: the next real spell is number 1.
        tracker.RecordFailure("Y", Ticks(3));
        Assert.Equal(1, tracker.RecordSuccess(Ticks(4))!.SpellInRun);
    }

    /// <summary>A new spell after a closed one starts from its own first failure.</summary>
    [Fact]
    public void ANewSpell_RemembersItsOwnFirstKindAndTime()
    {
        var tracker = new FailureStreakTracker();
        tracker.RecordFailure("A", Ticks(0));
        tracker.RecordSuccess(Ticks(1));

        tracker.RecordFailure("B", Ticks(100));
        var spell = tracker.RecordSuccess(Ticks(105))!;

        Assert.Equal(1, spell.FailedAttempts);
        Assert.Equal(5, spell.FailedSeconds);
        Assert.Equal("B", spell.FirstKind);
        Assert.Equal("B", spell.LastKind);
    }

    /// <summary>Smoke test: many threads recording failures lose none and close exactly one spell.</summary>
    [Fact]
    public void ManyThreads_LoseNoFailure()
    {
        var tracker = new FailureStreakTracker();
        Parallel.For(0, 1000, i => tracker.RecordFailure(i % 2 == 0 ? "A" : "B", Ticks(1)));

        var spell = tracker.RecordSuccess(Ticks(2));

        Assert.Equal(1000, spell!.FailedAttempts);
        Assert.Null(tracker.RecordSuccess(Ticks(3)));
    }
}
