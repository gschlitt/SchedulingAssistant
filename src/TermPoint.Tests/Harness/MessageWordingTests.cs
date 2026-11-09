using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Unit tests for the plain-words rules of invariant I7 (<see cref="MessageWording"/>) and for the step-level checks of invariant I8
/// (<see cref="MessageChecker.CheckStep"/>), with synthetic messages. They need no world and run in the normal suite.
/// </summary>
public sealed class MessageWordingTests
{
    /// <summary>The folder the harness keeps its files in, as the tests pretend it is.</summary>
    private const string HarnessFolder = @"C:\Users\x\AppData\Local\Temp\harness_0123456789abcdef";

    /// <summary>Texts that are plain words: no rule is broken.</summary>
    [Theory]
    [InlineData("Your changes were kept. Open the database for editing and TermPoint will offer them back.")]
    [InlineData("Your version was saved as Bob-copy.db")]
    [InlineData("Try again|Close")]
    [InlineData("Restart TermPoint. Then open the file again.")]
    [InlineData("The file system. Everything is as it was.")]
    [InlineData("It is unsafe to continue, so we stopped; nothing was changed.")]
    [InlineData("See http://example.com for help at 9:00.")]
    [InlineData("")]
    public void PlainText_BreaksNoRule(string text)
        => Assert.Empty(MessageWording.Reasons(text, new[] { HarnessFolder }));

    /// <summary>Null breaks nothing.</summary>
    [Fact]
    public void NullText_BreaksNoRule() => Assert.Empty(MessageWording.Reasons(null));

    /// <summary>Each forbidden shape is reported, whatever its case.</summary>
    [Theory]
    [InlineData("Your WORKING COPY was kept.", "working copy")]
    [InlineData("We made a Local Copy first.", "local copy")]
    [InlineData("Your work is safe.", "safe")]
    [InlineData("Saved to C:\\Users\\bob\\file.db", "drive path")]
    [InlineData("Saved to d:/data/file.db", "drive path")]
    [InlineData("Could not reach \\\\server\\share\\dept.db", "network path")]
    [InlineData("Failed: System.IO.IOException: the file is in use", "program name")]
    [InlineData("Failed: TermPoint.Services.CheckoutService threw", "program name")]
    [InlineData("The operation threw an Exception", "exception")]
    [InlineData("Error 0x80070020 while copying", "error code")]
    [InlineData("HRESULT 80004005", "HRESULT")]
    [InlineData("It failed\n   at Foo.Bar()", "stack trace")]
    [InlineData("It failed at Foo.Bar.Baz(int x)", "stack trace")]
    [InlineData("Session 3f2504e0-4f89-41d3-9a0c-0305e82c3301 ended", "GUID")]
    public void ForbiddenShapes_AreReported(string text, string expectedFragment)
    {
        var reasons = MessageWording.Reasons(text, new[] { HarnessFolder });
        Assert.Contains(reasons, r => r.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The harness's own folders are reported, with either slash and any case; a bare file name is not.</summary>
    [Fact]
    public void HarnessFolders_AreReported_ButABareFileNameIsNot()
    {
        Assert.Contains(MessageWording.Reasons($"Saved as {HarnessFolder}\\copy.db", new[] { HarnessFolder }), r => r.Contains("folders"));
        Assert.Contains(MessageWording.Reasons($"Saved as {HarnessFolder.Replace('\\', '/').ToUpperInvariant()}/copy.db", new[] { HarnessFolder }), r => r.Contains("folders"));
        Assert.Empty(MessageWording.Reasons("Saved as copy.db", new[] { HarnessFolder }));
    }

    /// <summary>A message and a banner: the identity is the title and the first sentence.</summary>
    [Fact]
    public void Identity_IsTitlePlusFirstSentence()
    {
        Assert.Equal("Checkout Failed | Could not open the database for editing",
            MessageChecker.IdentityOf("Checkout Failed", "Could not open the database for editing. The application will open in read-only mode."));
        Assert.StartsWith("(banner) | ", MessageChecker.IdentityOf("", "Someone else now holds the lock. Your changes are kept."));
    }

    /// <summary>Builds a message as the checker sees it.</summary>
    private static SeenMessage Seen(string computer, string title, string text, string phase = "")
        => new(computer, char.ToUpperInvariant(computer[0]) + computer[1..], title, text, IsQuestion: false, phase);

    /// <summary>I8 (a): the same title and text twice on one computer in one step is a violation; on two computers it is not.</summary>
    [Fact]
    public void SameMessageTwiceOnOneComputer_IsAViolation()
    {
        var sightings = new List<MessagePairSighting>();
        var violations = MessageChecker.CheckStep(
            new[] { Seen("bob", "T", "Text one. More."), Seen("bob", "T", "Text one. More."), Seen("alice", "T", "Text one. More.") },
            3, "run", "Bob does something", sightings);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.MessageShownTwice, violation.Kind);
        Assert.Equal("bob", violation.Computer);
        Assert.Empty(sightings);
    }

    /// <summary>I8 (b): two different messages on one computer in one step are a violation (the allow-list is empty today), and are sighted.</summary>
    [Fact]
    public void TwoDifferentMessages_AreAViolation_AndAreSighted()
    {
        var pair = new[] { Seen("bob", "A", "First. More.", "before the event"), Seen("bob", "B", "Second. More.", "after release") };

        var sightings = new List<MessagePairSighting>();
        var violation = Assert.Single(MessageChecker.CheckStep(pair, 1, "run", "Bob opens", sightings));
        Assert.Equal(ViolationKind.TwoMessagesForOneProblem, violation.Kind);
        Assert.Equal(new[] { "A | First", "B | Second" }, violation.MessageIdentities);
        Assert.Equal(Findings.NewFinding, Findings.Classify(violation));

        var sighting = Assert.Single(sightings);
        Assert.Equal("A | First", sighting.First);
        Assert.Equal("B | Second", sighting.Second);
        Assert.Equal("before the event; after release", sighting.Phases);
    }

    /// <summary>A banner with "Changes Not Saved" is classified F17 (fixed in 1.2.4, so it is no longer a known finding and a regression fails as new); two banners are F18 (also fixed in 1.2.4; the rule stays so a regression is reported).</summary>
    [Fact]
    public void BannerWithChangesNotSaved_IsF17_AndTwoBanners_AreF18()
    {
        var takeover = Seen("bob", "", "Alice has taken over editing this database, so it's read-only for you now. More.");
        var removed  = Seen("bob", "", "Another program removed the file TermPoint uses to mark this database as in use. More.");
        var notSaved = Seen("bob", KeptChangesPrompt.ChangesNotSavedTitle, "Your latest changes couldn't be saved to the database. More.");

        var f17 = Assert.Single(MessageChecker.CheckStep(new[] { takeover, notSaved }, 1, "run", "Bob closes", new List<MessagePairSighting>()));
        Assert.Equal("F17", Findings.Classify(f17));

        var f18 = Assert.Single(MessageChecker.CheckStep(new[] { removed, takeover }, 1, "run", "Bob switches", new List<MessagePairSighting>()));
        Assert.Equal("F18", Findings.Classify(f18));
    }

    /// <summary>A "Try again / Close" problem report pairs with a message (A13's shape), and asking it again after Try again is not "the same message twice".</summary>
    [Fact]
    public void ProblemQuestion_PairsWithAMessage_AndMayBeAskedAgain()
    {
        var failed   = Seen("bob", "Checkout Failed", "Could not open the database for editing. The application will open in read-only mode.");
        var question = new SeenMessage("bob", "Bob", HarnessTitles.CouldntOpen, "TermPoint could not open the database. Try again.", IsQuestion: true, "");

        var sightings = new List<MessagePairSighting>();
        var violations = MessageChecker.CheckStep(new[] { failed, question, question, question }, 2, "run", "Bob opens", sightings);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.TwoMessagesForOneProblem, violation.Kind);
        Assert.Equal("A13", Findings.Classify(violation));
        Assert.Single(sightings);
        Assert.True(MessageChecker.IsProblemQuestion(HarnessTitles.CouldntOpen));
        Assert.False(MessageChecker.IsProblemQuestion(HarnessTitles.RestoreUnsavedChanges));
    }

    /// <summary>Messages on different computers are not a pair.</summary>
    [Fact]
    public void DifferentMessagesOnDifferentComputers_AreNotAPair()
    {
        var sightings = new List<MessagePairSighting>();
        var violations = MessageChecker.CheckStep(
            new[] { Seen("bob", "A", "First."), Seen("alice", "B", "Second.") }, 1, "run", "", sightings);
        Assert.Empty(violations);
        Assert.Empty(sightings);
    }
}
