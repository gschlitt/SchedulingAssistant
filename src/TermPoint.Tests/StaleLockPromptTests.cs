using System;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="StaleLockPrompt"/>, the wording of the stale-lock takeover prompt (spec item 9a)
/// and of the "holder is active again" message: the holder's name, machine and quiet time, the warning
/// about unsaved changes, and the fallbacks when fields are missing.
/// </summary>
public sealed class StaleLockPromptTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A fixed "now" so the minutes in a message are exact.</summary>
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Builds a lock holder whose last heartbeat was <paramref name="minutesAgo"/> minutes before <see cref="Now"/>.</summary>
    private static LockFileData Holder(string username, string machine, double minutesAgo = 7) => new()
    {
        Username  = username,
        Machine   = machine,
        Acquired  = Now.AddMinutes(-minutesAgo - 30),
        Heartbeat = Now.AddMinutes(-minutesAgo),
    };

    // ── Takeover prompt ───────────────────────────────────────────────────────

    /// <summary>The text of the dialog's title and buttons is the agreed wording.</summary>
    [Fact]
    public void Constants_AreTheAgreedWording()
    {
        Assert.Equal("Database Lock Is Inactive", StaleLockPrompt.TakeOverTitle);
        Assert.Equal("Take over", StaleLockPrompt.TakeOverButton);
        Assert.Equal("Open read-only", StaleLockPrompt.OpenReadOnlyButton);
        Assert.Equal("Database Is In Use Again", StaleLockPrompt.ActiveAgainTitle);
    }

    /// <summary>The full message for a known holder: name, machine, minutes, the cause, and the warning.</summary>
    [Fact]
    public void TakeOverMessage_NamesTheHolderTheMachineAndTheMinutes_AndWarnsAboutUnsavedChanges()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("jsmith", "LAPTOP-42", minutesAgo: 7), Now);

        Assert.Equal(
            "jsmith on LAPTOP-42 hasn't been active in this database for 7 minutes. " +
            "Their computer may be asleep or disconnected.\n\n" +
            "If you take over, changes they haven't saved yet can't go into the database " +
            "— they'll only be able to save them as a separate copy.",
            message);
    }

    /// <summary>One minute reads "1 minute", not "1 minute(s)" or "1 minutes".</summary>
    [Fact]
    public void TakeOverMessage_OneMinute_IsSingular()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("jsmith", "LAPTOP-42", minutesAgo: 1.5), Now);

        Assert.Contains("for 1 minute. ", message);
        Assert.DoesNotContain("minutes", message);
    }

    /// <summary>Every other count is plural, including zero (whole minutes, rounded down).</summary>
    [Theory]
    [InlineData(0.5, "for 0 minutes. ")]
    [InlineData(2.9, "for 2 minutes. ")]
    [InlineData(61,  "for 61 minutes. ")]
    public void TakeOverMessage_OtherCounts_ArePlural(double minutesAgo, string expected)
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("jsmith", "LAPTOP-42", minutesAgo), Now);

        Assert.Contains(expected, message);
    }

    /// <summary>A heartbeat in the future (clock skew) never produces a negative number of minutes.</summary>
    [Fact]
    public void TakeOverMessage_HeartbeatInTheFuture_ShowsZeroMinutes()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("jsmith", "LAPTOP-42", minutesAgo: -5), Now);

        Assert.Contains("for 0 minutes. ", message);
    }

    /// <summary>Without a machine name only the user is named.</summary>
    [Fact]
    public void TakeOverMessage_MissingMachine_NamesOnlyTheUser()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("jsmith", ""), Now);

        Assert.StartsWith("jsmith hasn't been active in this database for 7 minutes.", message);
    }

    /// <summary>Without a user name the machine is still named.</summary>
    [Fact]
    public void TakeOverMessage_MissingUser_NamesTheMachine()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("  ", "LAPTOP-42"), Now);

        Assert.StartsWith("Someone on LAPTOP-42 hasn't been active in this database for 7 minutes.", message);
    }

    /// <summary>With neither, the holder is "An unknown user"; the warning is still there.</summary>
    [Fact]
    public void TakeOverMessage_NoNameNoMachine_SaysAnUnknownUser_AndStillWarns()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(Holder("", ""), Now);

        Assert.StartsWith("An unknown user hasn't been active in this database for 7 minutes.", message);
        Assert.Contains("changes they haven't saved yet can't go into the database", message);
    }

    /// <summary>No holder at all (the lock could not be read): an unknown user, and no minute count to invent.</summary>
    [Fact]
    public void TakeOverMessage_NullHolder_SaysAnUnknownUserForAWhile()
    {
        var message = StaleLockPrompt.BuildTakeOverMessage(null, Now);

        Assert.StartsWith("An unknown user hasn't been active in this database for a while.", message);
        Assert.Contains("separate copy", message);
    }

    /// <summary>
    /// The stand-in holder for a garbled lock file ("(corrupted)", heartbeat at DateTime.MinValue) must
    /// never leak into the prompt, nor produce a ridiculous number of minutes.
    /// </summary>
    [Fact]
    public void TakeOverMessage_CorruptedLockPlaceholder_ReadsAsAnUnknownUser()
    {
        var corrupted = new LockFileData
        {
            Username  = "(corrupted)",
            Machine   = "(corrupted)",
            Acquired  = DateTime.MinValue,
            Heartbeat = DateTime.MinValue,
        };

        var message = StaleLockPrompt.BuildTakeOverMessage(corrupted, Now);

        Assert.StartsWith("An unknown user hasn't been active in this database for a while.", message);
        Assert.DoesNotContain("corrupted", message);
    }

    // ── "Active again" message ────────────────────────────────────────────────

    /// <summary>The follow-up names the holder and says the session will open read-only.</summary>
    [Fact]
    public void ActiveAgainMessage_NamesTheHolder()
    {
        var message = StaleLockPrompt.BuildActiveAgainMessage(Holder("jsmith", "LAPTOP-42"));

        Assert.Equal("jsmith is active in this database again, so it will open read-only.", message);
    }

    /// <summary>With no usable name (unreadable lock, or none) it says "Someone".</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(corrupted)")]
    public void ActiveAgainMessage_NoUsableName_SaysSomeone(string? username)
    {
        var holder = username is null ? null : Holder(username, "LAPTOP-42");

        var message = StaleLockPrompt.BuildActiveAgainMessage(holder);

        Assert.Equal("Someone is active in this database again, so it will open read-only.", message);
    }
}
