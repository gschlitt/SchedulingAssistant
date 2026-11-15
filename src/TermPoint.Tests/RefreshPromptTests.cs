using System.Globalization;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="RefreshPrompt"/>, the wording of the Refresh button's tooltip and of the one line a failed
/// refresh shows in the yellow save-error row (spec item 28, from item 11), and for the pure function that picks that
/// line (<see cref="RefreshPrompt.FailureLine"/>). Every text is pinned VERBATIM (the wording was approved by Greg on
/// 2026-10-26). The view model that calls the picker is not unit-constructible (it needs the whole window's services),
/// so "a failed refresh leaves the last-refreshed time alone" is covered here through the picker's contract: it returns
/// null exactly when the refresh worked, and <c>RefreshData</c> sets the time and reloads the list only then.
/// </summary>
public sealed class RefreshPromptTests
{
    /// <summary>2:14 PM, the time every test names.</summary>
    private static readonly DateTime TwoFourteenPm = new(2026, 10, 26, 14, 14, 0);

    /// <summary>
    /// Runs <paramref name="check"/> under the en-US culture (the "tt" part of the time format is culture-dependent)
    /// and puts the culture back afterwards.
    /// </summary>
    /// <param name="check">The assertions to run.</param>
    private static void UnderEnUs(Action check)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try { check(); }
        finally { CultureInfo.CurrentCulture = before; }
    }

    /// <summary>The tooltip, before any refresh has worked and after one has.</summary>
    [Fact]
    public void Tooltip_IsTheApprovedWording()
    {
        Assert.Equal("Refresh to see changes made by the person editing this database.", RefreshPrompt.TooltipBeforeFirstRefresh);
        Assert.Equal(RefreshPrompt.TooltipBeforeFirstRefresh, RefreshPrompt.Tooltip(null));
        UnderEnUs(() => Assert.Equal(
            "Last refreshed at 2:14 PM. Click to refresh again.",
            RefreshPrompt.Tooltip(TwoFourteenPm)));
    }

    /// <summary>The three failure lines, with a time and without one (no refresh has worked yet), pinned verbatim.</summary>
    [Fact]
    public void FailureLines_AreTheApprovedWording()
    {
        Assert.Equal(
            "TermPoint couldn't refresh and lost its connection to the database. Close TermPoint and open it again.",
            RefreshPrompt.ConnectionLost);

        UnderEnUs(() =>
        {
            Assert.Equal(
                "Couldn't refresh: the database location isn't responding. Showing data from 2:14 PM.",
                RefreshPrompt.NotResponding(TwoFourteenPm));
            Assert.Equal(
                "Couldn't refresh just now. Showing data from 2:14 PM. Try again in a moment.",
                RefreshPrompt.OtherFailure(TwoFourteenPm));
        });
        Assert.Equal(
            "Couldn't refresh: the database location isn't responding. Showing data from when you opened the database.",
            RefreshPrompt.NotResponding(null));
        Assert.Equal(
            "Couldn't refresh just now. Showing data from when you opened the database. Try again in a moment.",
            RefreshPrompt.OtherFailure(null));
    }

    /// <summary>The picker returns null when the refresh worked: nothing to show, so the view model reloads the list and sets the time.</summary>
    [Fact]
    public void FailureLine_WhenTheRefreshWorked_IsNull()
    {
        Assert.Null(RefreshPrompt.FailureLine(RefreshOutcome.Updated, threw: false, null));
        Assert.Null(RefreshPrompt.FailureLine(RefreshOutcome.Updated, threw: false, TwoFourteenPm));
    }

    /// <summary>Outcome SourceUnavailable: "the database location isn't responding", with the last good time or "when you opened the database".</summary>
    [Fact]
    public void FailureLine_SourceUnavailable_SaysTheLocationIsNotResponding()
    {
        Assert.Equal(RefreshPrompt.NotResponding(null),
            RefreshPrompt.FailureLine(RefreshOutcome.SourceUnavailable, false, null));
        Assert.Equal(RefreshPrompt.NotResponding(TwoFourteenPm),
            RefreshPrompt.FailureLine(RefreshOutcome.SourceUnavailable, false, TwoFourteenPm));
    }

    /// <summary>
    /// Outcome ConnectionLost (three reopen attempts failed, or the session ended): the restart line, whatever time is known.
    /// </summary>
    [Fact]
    public void FailureLine_ConnectionLost_SaysCloseAndReopen()
    {
        Assert.Equal(RefreshPrompt.ConnectionLost, RefreshPrompt.FailureLine(RefreshOutcome.ConnectionLost, false, null));
        Assert.Equal(RefreshPrompt.ConnectionLost, RefreshPrompt.FailureLine(RefreshOutcome.ConnectionLost, false, TwoFourteenPm));
    }

    /// <summary>
    /// Outcome ReplaceFailed (the swap failed, the connection is open on the previous data): "Couldn't refresh just
    /// now… Try again in a moment." naming the last good time.
    /// </summary>
    [Fact]
    public void FailureLine_ReplaceFailed_SaysTryAgainInAMoment()
    {
        Assert.Equal(RefreshPrompt.OtherFailure(null), RefreshPrompt.FailureLine(RefreshOutcome.ReplaceFailed, false, null));
        Assert.Equal(RefreshPrompt.OtherFailure(TwoFourteenPm), RefreshPrompt.FailureLine(RefreshOutcome.ReplaceFailed, false, TwoFourteenPm));
    }

    /// <summary>An exception: "Couldn't refresh just now… Try again in a moment."</summary>
    [Fact]
    public void FailureLine_AnException_SaysTryAgainInAMoment()
    {
        Assert.Equal(RefreshPrompt.OtherFailure(null), RefreshPrompt.FailureLine(null, threw: true, null));
        Assert.Equal(RefreshPrompt.OtherFailure(TwoFourteenPm), RefreshPrompt.FailureLine(null, threw: true, TwoFourteenPm));
        // An exception wins over an outcome that was somehow also recorded.
        Assert.Equal(RefreshPrompt.OtherFailure(null),
            RefreshPrompt.FailureLine(RefreshOutcome.SourceUnavailable, threw: true, null));
    }

    /// <summary>No outcome and no exception (the refresh produced nothing): the "just now" line, never a success.</summary>
    [Fact]
    public void FailureLine_NoOutcomeAndNoException_IsTheGenericFailure()
        => Assert.Equal(RefreshPrompt.OtherFailure(TwoFourteenPm), RefreshPrompt.FailureLine(null, false, TwoFourteenPm));

    /// <summary>
    /// A failure's line names the time of the last refresh that WORKED, so the time the view model passes must be the
    /// one it kept: two different times give two different lines (the time is read from the argument, never "now").
    /// </summary>
    [Fact]
    public void FailureLine_NamesTheLastGoodTime_NotTheCurrentOne()
        => UnderEnUs(() =>
        {
            Assert.Contains("2:14 PM", RefreshPrompt.FailureLine(RefreshOutcome.SourceUnavailable, false, TwoFourteenPm));
            Assert.Contains("9:05 AM", RefreshPrompt.FailureLine(RefreshOutcome.SourceUnavailable, false, new DateTime(2026, 10, 26, 9, 5, 0)));
        });

    /// <summary>The DEBUG fault plan text: <c>replace</c> and <c>reopen[:count[:skip]]</c> parse, anything else is ignored.</summary>
    [Fact]
    public void RefreshFaultPlan_ParsesReplaceAndReopen_IgnoresGarbage()
    {
        Assert.Equal(InjectedRefreshFault.Replace, RefreshFaultPlan.Parse("replace")!.Fault);
        Assert.Equal(InjectedRefreshFault.Reopen, RefreshFaultPlan.Parse("REOPEN:2")!.Fault);
        Assert.Null(RefreshFaultPlan.Parse("unreachable"));
        Assert.Null(RefreshFaultPlan.Parse(null));

        var plan = RefreshFaultPlan.Parse("reopen:2:1")!;
        Assert.False(plan.TryConsume(InjectedRefreshFault.Replace));   // another stage: neither fires nor spends
        Assert.False(plan.TryConsume(InjectedRefreshFault.Reopen));    // skipped
        Assert.True(plan.TryConsume(InjectedRefreshFault.Reopen));
        Assert.True(plan.TryConsume(InjectedRefreshFault.Reopen));
        Assert.False(plan.TryConsume(InjectedRefreshFault.Reopen));    // budget spent
    }

    /// <summary>
    /// The rules for every text: no "SQLite", "snapshot", "write access", "D'", path, raw exception or log text, no
    /// doubled space.
    /// </summary>
    [Fact]
    public void NoText_BreaksTheRulesOfRule4()
    {
        var texts = new[]
        {
            RefreshPrompt.TooltipBeforeFirstRefresh, RefreshPrompt.Tooltip(TwoFourteenPm), RefreshPrompt.ConnectionLost,
            RefreshPrompt.NotResponding(null), RefreshPrompt.NotResponding(TwoFourteenPm),
            RefreshPrompt.OtherFailure(null), RefreshPrompt.OtherFailure(TwoFourteenPm)
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain("SQLite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("snapshot", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("write access", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("D'", text);
            Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
            Assert.DoesNotContain("  ", text);
        }
    }
}
