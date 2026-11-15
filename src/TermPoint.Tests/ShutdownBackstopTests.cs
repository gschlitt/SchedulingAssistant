using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec 1.2.4 item 4.3 (design <c>designs/restore-relaunch-1.2.4.md</c>): the shutdown backstop ends the process only
/// when the main loop has not ended after the exit teardown, waits for the lock file's background delete first, marks
/// the run as ended normally, reports once, and never runs on a normal exit. And the restart argument: the relaunched
/// copy reopens the restored database only when the argument names the saved most-recent database.
/// Every wait is instant and the exit is recorded, never real.
/// </summary>
public sealed class ShutdownBackstopTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>What a backstop did, in order.</summary>
    private readonly List<string> _steps = [];

    /// <summary>The waits the backstop asked for, in order.</summary>
    private readonly List<TimeSpan> _waits = [];

    /// <summary>Builds a backstop whose every effect is recorded in <see cref="_steps"/>.</summary>
    /// <param name="mainReturned">What the "has Main returned" check answers (read each time).</param>
    /// <param name="pendingRelease">The pending lock delete to hand out, or null.</param>
    private ShutdownBackstop Make(Func<bool> mainReturned, Task? pendingRelease = null)
        => new(mainReturned,
               pendingRelease: () => pendingRelease,
               endRunMarker:   () => _steps.Add("runMarker:End"),
               log:            _ => _steps.Add("log"),
               report:         () => _steps.Add("report"),
               exit:           code => _steps.Add($"exit:{code}"),
               wait:           t => { _waits.Add(t); return Task.CompletedTask; });

    // ── The backstop ──────────────────────────────────────────────────────────

    /// <summary>A normal exit (Main has returned) never fires the backstop.</summary>
    [Fact]
    public async Task MainReturned_DoesNothing()
    {
        var fired = await Make(() => true).FireIfStillRunningAsync();

        Assert.False(fired);
        Assert.Empty(_steps);
    }

    /// <summary>
    /// Main has not returned: the run marker is ended FIRST (no false "ended unexpectedly" at the next start), then one
    /// log line and one report, then the exit with code 0.
    /// </summary>
    [Fact]
    public async Task StillRunning_EndsTheRunMarker_LogsReports_ThenExitsWithZero()
    {
        var fired = await Make(() => false).FireIfStillRunningAsync();

        Assert.True(fired);
        Assert.Equal(["runMarker:End", "log", "report", "exit:0"], _steps);
        Assert.Contains(ShutdownBackstop.ReportGrace, _waits);
    }

    /// <summary>
    /// A lock file's background delete still running is waited for (bounded by <see cref="ShutdownBackstop.ReleaseWait"/>)
    /// before the exit, so a stale lock file is never left for colleagues.
    /// </summary>
    [Fact]
    public async Task PendingLockDelete_IsWaitedForBeforeTheExit()
    {
        var delete = new TaskCompletionSource();
        var backstop = new ShutdownBackstop(
            mainReturned:   () => false,
            pendingRelease: () => delete.Task,
            endRunMarker:   () => _steps.Add("runMarker:End"),
            log:            _ => { },
            report:         () => { },
            exit:           code => _steps.Add($"exit:{code}"),
            wait:           t => t == ShutdownBackstop.ReleaseWait ? Task.Delay(Timeout.Infinite) : Task.CompletedTask);

        var fire = backstop.FireIfStillRunningAsync();
        await Task.Delay(100);
        Assert.False(fire.IsCompleted, "the backstop must wait for the lock file's delete");
        Assert.Empty(_steps);

        delete.SetResult();
        Assert.True(await fire.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(["runMarker:End", "exit:0"], _steps);
    }

    /// <summary>A lock delete that never finishes does not hold the exit forever: the bounded wait ends it.</summary>
    [Fact]
    public async Task PendingLockDelete_ThatNeverFinishes_StillExitsAfterTheBoundedWait()
    {
        var fired = await Make(() => false, pendingRelease: new TaskCompletionSource().Task).FireIfStillRunningAsync();

        Assert.True(fired);
        Assert.Contains(ShutdownBackstop.ReleaseWait, _waits);
        Assert.Equal("exit:0", _steps[^1]);
    }

    /// <summary>A slow but normal exit that finishes while the backstop waits for the lock delete is left alone.</summary>
    [Fact]
    public async Task MainReturnsWhileWaitingForTheLockDelete_DoesNotExit()
    {
        var returned = false;
        var delete   = new TaskCompletionSource();
        var backstop = new ShutdownBackstop(
            mainReturned:   () => returned,
            pendingRelease: () => delete.Task,
            endRunMarker:   () => _steps.Add("runMarker:End"),
            log:            _ => _steps.Add("log"),
            report:         () => _steps.Add("report"),
            exit:           code => _steps.Add($"exit:{code}"),
            wait:           _ => Task.Delay(Timeout.Infinite));   // only the delete's completion ends the wait
        var fire = backstop.FireIfStillRunningAsync();

        returned = true;
        delete.SetResult();

        Assert.False(await fire.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(_steps);
    }

    /// <summary>A failing log, report or run-marker step never stops the exit.</summary>
    [Fact]
    public async Task FailingSteps_StillExit()
    {
        var exits = new List<int>();
        var backstop = new ShutdownBackstop(
            mainReturned:   () => false,
            pendingRelease: () => Task.FromException(new IOException("delete failed")),
            endRunMarker:   () => throw new InvalidOperationException(),
            log:            _ => throw new InvalidOperationException(),
            report:         () => throw new InvalidOperationException(),
            exit:           exits.Add,
            wait:           _ => Task.CompletedTask);

        Assert.True(await backstop.FireIfStillRunningAsync());
        Assert.Equal([0], exits);
    }

    /// <summary>Arm waits <see cref="ShutdownBackstop.Delay"/> first and runs once, however often it is called.</summary>
    [Fact]
    public async Task Arm_WaitsTheDelay_AndRunsOnce()
    {
        var backstop = Make(() => false);

        var first  = backstop.Arm();
        var second = backstop.Arm();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ShutdownBackstop.Delay, _waits[0]);
        Assert.Single(_steps, s => s == "exit:0");
    }

    // ── The restart argument ──────────────────────────────────────────────────

    /// <summary>The relaunch's own arguments, naming the saved database, reopen it.</summary>
    [Fact]
    public void ShouldReopen_ArgumentNamesTheSavedDatabase_True()
    {
        var path = Path.Combine(Path.GetTempPath(), "share", "BIOL-TT.db");

        Assert.True(RestartArguments.ShouldReopen(RestartArguments.ForReopen(path), path));
    }

    /// <summary>Paths are compared as full paths and ignoring case.</summary>
    [Fact]
    public void ShouldReopen_SamePathDifferentCaseOrForm_True()
    {
        var path = Path.Combine(Path.GetTempPath(), "share", "BIOL-TT.db");
        var other = Path.Combine(Path.GetTempPath(), "share", ".", "biol-tt.DB");

        Assert.True(RestartArguments.ShouldReopen(RestartArguments.ForReopen(other), path));
    }

    /// <summary>Anything else starts as it always has: another database, no argument, no path, no saved database.</summary>
    [Theory]
    [InlineData("other")]
    [InlineData("none")]
    [InlineData("flagOnly")]
    [InlineData("blankPath")]
    [InlineData("noSaved")]
    public void ShouldReopen_OtherwiseFalse(string variant)
    {
        var saved = Path.Combine(Path.GetTempPath(), "share", "BIOL-TT.db");
        string[] args = variant switch
        {
            "other"     => RestartArguments.ForReopen(Path.Combine(Path.GetTempPath(), "share", "CHEM.db")),
            "none"      => [],
            "flagOnly"  => [RestartArguments.ReopenAfterRestoreFlag],
            "blankPath" => [RestartArguments.ReopenAfterRestoreFlag, " "],
            _           => RestartArguments.ForReopen(saved),
        };

        Assert.False(RestartArguments.ShouldReopen(args, variant == "noSaved" ? null : saved));
    }
}
