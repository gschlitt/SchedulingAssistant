using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ExitCloseGuard"/>, the pure decision behind the main window's close (spec item 15, the
/// 1.2.3 part): the first close request starts the teardown, further requests while it runs are ignored, and the
/// window closes once the teardown has finished or the two-minute backstop has passed. The clock is a
/// <see cref="FakeTimeProvider"/>, so minutes pass in no time. The window work (hiding windows, the curtain, the
/// final <c>Close()</c>) cannot be unit-tested here; it is covered by Greg's hand checks.
/// </summary>
public sealed class ExitCloseGuardTests
{
    /// <summary>The fake clock the guard under test reads.</summary>
    private readonly FakeTimeProvider _clock = new();

    /// <summary>Builds a guard on <see cref="_clock"/>.</summary>
    private ExitCloseGuard NewGuard() => new(_clock);

    /// <summary>The backstop is two minutes, as decided.</summary>
    [Fact]
    public void Backstop_IsTwoMinutes() => Assert.Equal(TimeSpan.FromMinutes(2), ExitCloseGuard.Backstop);

    /// <summary>A new guard is Open and has not fired the backstop.</summary>
    [Fact]
    public void NewGuard_IsOpen()
    {
        var guard = NewGuard();

        Assert.Equal(ExitCloseState.Open, guard.State);
        Assert.False(guard.BackstopFired);
    }

    /// <summary>The first request starts the teardown and moves to ClosingInProgress.</summary>
    [Fact]
    public void FirstRequest_StartsTheTeardown()
    {
        var guard = NewGuard();

        Assert.Equal(ExitCloseDecision.StartTeardown, guard.OnCloseRequested());
        Assert.Equal(ExitCloseState.ClosingInProgress, guard.State);
    }

    /// <summary>
    /// The bug this guard fixes: a second and a third click on X while the teardown runs are ignored, and the
    /// guard stays in ClosingInProgress.
    /// </summary>
    [Fact]
    public void SecondAndThirdRequests_WhileTheTeardownRuns_AreIgnored()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());

        Assert.Equal(ExitCloseState.ClosingInProgress, guard.State);
        Assert.False(guard.BackstopFired);
    }

    /// <summary>A request after the teardown finished (the window's own final Close()) is let through, with no backstop involved.</summary>
    [Fact]
    public async Task RequestAfterTheTeardownFinished_LetsTheWindowClose()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        var failure = await guard.RunTeardownAsync(() => Task.CompletedTask);

        Assert.Null(failure);
        Assert.Equal(ExitCloseState.ReadyToClose, guard.State);
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
        Assert.False(guard.BackstopFired);
    }

    /// <summary>
    /// While the teardown is genuinely still awaiting, requests are ignored; the moment it completes, the next
    /// request is let through. Uses a teardown that only finishes when the test says so.
    /// </summary>
    [Fact]
    public async Task RequestsAreIgnoredUntilTheRunningTeardownCompletes()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();
        var release = new TaskCompletionSource();

        var running = guard.RunTeardownAsync(() => release.Task);

        Assert.False(running.IsCompleted);
        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());
        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());

        release.SetResult();
        Assert.Null(await running);
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
    }

    /// <summary>
    /// After two minutes a request is let through even though the teardown has not finished: the backstop, so a
    /// hung save can never trap the user. It is recorded in <see cref="ExitCloseGuard.BackstopFired"/>.
    /// </summary>
    [Fact]
    public void RequestAfterTheBackstop_LetsTheWindowClose_EvenThoughTheTeardownHasNotFinished()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        _clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
        Assert.True(guard.BackstopFired);
        Assert.Equal(ExitCloseState.ReadyToClose, guard.State);
    }

    /// <summary>One tick short of two minutes the request is still ignored: the backstop has not passed.</summary>
    [Fact]
    public void RequestJustBeforeTheBoundary_IsStillIgnored()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        _clock.Advance(ExitCloseGuard.Backstop - TimeSpan.FromTicks(1));

        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());
        Assert.False(guard.BackstopFired);
    }

    /// <summary>
    /// Exactly at the boundary the backstop HAS passed ("two minutes or more"): the request is let through.
    /// </summary>
    [Fact]
    public void RequestExactlyAtTheBoundary_IsLetThrough()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        _clock.Advance(ExitCloseGuard.Backstop);

        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
        Assert.True(guard.BackstopFired);
    }

    /// <summary>
    /// The backstop counts from the START of the teardown, not from the latest ignored request: an ignored click
    /// at one minute does not push the deadline back.
    /// </summary>
    [Fact]
    public void TheBackstopCountsFromTheStartOfTheTeardown_NotFromTheLastIgnoredRequest()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ExitCloseDecision.Ignore, guard.OnCloseRequested());

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
    }

    /// <summary>
    /// A teardown that throws still ends in "close": <see cref="ExitCloseGuard.RunTeardownAsync"/> hands the
    /// exception back instead of throwing it and the guard becomes ReadyToClose, so the window's final Close() is
    /// never refused and the window cannot be left open and covered.
    /// </summary>
    [Fact]
    public async Task ATeardownThatThrows_StillEndsInClose()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();
        var boom = new InvalidOperationException("the save blew up");

        var failure = await guard.RunTeardownAsync(() => throw boom);

        Assert.Same(boom, failure);
        Assert.Equal(ExitCloseState.ReadyToClose, guard.State);
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
    }

    /// <summary>The same when the teardown fails after an await (an exception inside the returned task, not thrown synchronously).</summary>
    [Fact]
    public async Task ATeardownTaskThatFaults_StillEndsInClose()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();

        var failure = await guard.RunTeardownAsync(async () =>
        {
            await Task.Yield();
            throw new IOException("share went away");
        });

        Assert.IsType<IOException>(failure);
        Assert.Equal(ExitCloseState.ReadyToClose, guard.State);
    }

    /// <summary>
    /// If the backstop already let a close through and the teardown finishes later, the guard stays ReadyToClose
    /// and <see cref="ExitCloseGuard.BackstopFired"/> stays true, which tells the window not to call Close() again.
    /// </summary>
    [Fact]
    public async Task AteardownThatFinishesAfterTheBackstop_KeepsTheBackstopRecorded()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();
        var release = new TaskCompletionSource();
        var running = guard.RunTeardownAsync(() => release.Task);

        _clock.Advance(ExitCloseGuard.Backstop);
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());

        release.SetResult();
        await running;

        Assert.True(guard.BackstopFired);
        Assert.Equal(ExitCloseState.ReadyToClose, guard.State);
        Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
    }

    /// <summary>Once ready to close, every later request is let through.</summary>
    [Fact]
    public async Task OnceReadyToClose_EveryRequestIsLetThrough()
    {
        var guard = NewGuard();
        guard.OnCloseRequested();
        await guard.RunTeardownAsync(() => Task.CompletedTask);

        for (var i = 0; i < 3; i++)
            Assert.Equal(ExitCloseDecision.LetClose, guard.OnCloseRequested());
    }

    /// <summary>The curtain says "Saving and closing…" in write mode and "Closing…" otherwise.</summary>
    [Fact]
    public void StatusText_NamesTheSaveOnlyInWriteMode()
    {
        Assert.Equal("Saving and closing…", ExitCloseGuard.StatusText(writeAccess: true));
        Assert.Equal("Closing…", ExitCloseGuard.StatusText(writeAccess: false));
    }
}
