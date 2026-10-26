#if DEBUG
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SlowExitSave"/>, the DEBUG-ONLY <c>TERMPOINT_DEBUG_SLOW_EXIT_SAVE</c> switch that makes the
/// close teardown wait before the exit save (spec item 15). The whole file is compiled only into DEBUG test builds,
/// because the class under test does not exist in Release. Every test that touches the switch's shared state puts
/// the real environment and clock back in <c>finally</c> (test parallelisation is off for this assembly).
/// </summary>
public sealed class SlowExitSaveTests
{
    /// <summary>Whole seconds from 0 to 600, with optional surrounding whitespace, parse to that many seconds.</summary>
    [Theory]
    [InlineData("0",     0)]
    [InlineData("1",     1)]
    [InlineData("20",    20)]
    [InlineData("150",   150)]
    [InlineData("600",   600)]
    [InlineData(" 30 ",  30)]
    [InlineData("007",   7)]
    public void Parse_WholeSecondsInRange_ReturnsThatWait(string text, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), SlowExitSave.Parse(text));

    /// <summary>Out of range, signed, fractional, textual, empty or oversized values are ignored (null).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("601")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("20s")]
    [InlineData("abc")]
    [InlineData("99999999999999999999")]
    [InlineData("٣")] // an Arabic-Indic digit is a digit to char.IsDigit but not to this switch
    public void Parse_AnythingElse_IsIgnored(string? text)
        => Assert.Null(SlowExitSave.Parse(text));

    /// <summary>With the variable unset the wait returns at once and logs nothing.</summary>
    [Fact]
    public async Task Wait_Unset_ReturnsImmediately_AndLogsNothing()
    {
        var log = new RecordingAppLogger();
        SlowExitSave.SetSourceForTests(() => null, new FakeTimeProvider());
        try
        {
            await SlowExitSave.WaitIfRequestedAsync(log);

            Assert.Empty(log.Infos);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }

    /// <summary>0 is valid and means "no wait", with no log line.</summary>
    [Fact]
    public async Task Wait_Zero_ReturnsImmediately_AndLogsNothing()
    {
        var log = new RecordingAppLogger();
        SlowExitSave.SetSourceForTests(() => "0", new FakeTimeProvider());
        try
        {
            await SlowExitSave.WaitIfRequestedAsync(log);

            Assert.Empty(log.Infos);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }

    /// <summary>A set value waits exactly that long on the clock, and says so in one log line first.</summary>
    [Fact]
    public async Task Wait_Set_WaitsThatLong_AndLogsOneLine()
    {
        var log   = new RecordingAppLogger();
        var clock = new FakeTimeProvider();
        SlowExitSave.SetSourceForTests(() => "20", clock);
        try
        {
            var waiting = SlowExitSave.WaitIfRequestedAsync(log);

            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(19));
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            await waiting;

            var line = Assert.Single(log.Infos);
            Assert.Contains("TERMPOINT_DEBUG_SLOW_EXIT_SAVE", line);
            Assert.Contains("waiting 20 s before the exit save", line);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }

    /// <summary>An ignored value is logged exactly once (not on every exit attempt) and causes no wait.</summary>
    [Fact]
    public async Task Wait_Junk_IsIgnoredWithOneLogLine_EvenAcrossTwoCalls()
    {
        var log = new RecordingAppLogger();
        SlowExitSave.SetSourceForTests(() => "soon", new FakeTimeProvider());
        try
        {
            await SlowExitSave.WaitIfRequestedAsync(log);
            await SlowExitSave.WaitIfRequestedAsync(log);

            var line = Assert.Single(log.Infos);
            Assert.Contains("\"soon\" ignored", line);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }

    /// <summary>The variable is read once: a later change to the source is not seen.</summary>
    [Fact]
    public async Task Wait_ReadsTheValueOnce()
    {
        var log   = new RecordingAppLogger();
        var clock = new FakeTimeProvider();
        var value = "0";
        SlowExitSave.SetSourceForTests(() => value, clock);
        try
        {
            await SlowExitSave.WaitIfRequestedAsync(log); // reads "0" and remembers it
            value = "30";
            await SlowExitSave.WaitIfRequestedAsync(log); // would wait 30 s if it read again

            Assert.Empty(log.Infos);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }
}

/// <summary>
/// The switch seen from the teardown: with a wait configured, <see cref="SessionLifecycle.TeardownForCloseAsync"/>
/// pauses JUST BEFORE the exit save (after the periodic backup is stopped, before D' is saved to D), and then
/// carries on exactly as it always did. Runs on the shared lifecycle fixture with a controllable clock, so the
/// "slow" save takes no real time.
/// </summary>
public sealed class SlowExitSaveLifecycleTests : SessionLifecycleTestBase
{
    /// <summary>
    /// The teardown is held before the save while the wait runs (no save has happened), and once the wait is over
    /// the save, the release and the cleanup follow in their usual order.
    /// </summary>
    [Fact]
    public async Task Teardown_WaitsJustBeforeTheExitSave_ThenSavesAsUsual()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        _host.Edit("edit-at-close");
        ResetRecords();

        var clock = new FakeTimeProvider();
        SlowExitSave.SetSourceForTests(() => "30", clock);
        try
        {
            var teardown = _lifecycle.TeardownForCloseAsync();

            // Held: the backup was stopped, but the exit save has not started.
            Assert.False(teardown.IsCompleted);
            Assert.Contains("host:StopBackupSession", _trace);
            Assert.DoesNotContain("checkout:SaveCompleted", _trace);
            Assert.DoesNotContain("host:CloseSecondaryWindows", _trace);

            clock.Advance(TimeSpan.FromSeconds(30));
            await teardown;
            SettleLocks();

            AssertInOrder(_trace,
                "host:StopBackupSession",
                "checkout:SaveCompleted",
                "host:CloseSecondaryWindows",
                "host:DisposeServices(built=True, connection=True)");
            Assert.Contains(_logger.Infos, m => m.Contains("waiting 30 s before the exit save"));
            Assert.Empty(_logger.Errors);
        }
        finally { SlowExitSave.SetSourceForTests(); }
    }
}
#endif
