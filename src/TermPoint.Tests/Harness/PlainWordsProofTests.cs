using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests that invariants I7 ("plain words") and I8 (a) ("the same message twice") are wired into the world: a message, a question or a banner shown
/// to a computer in a funnel step is checked as it appears, and the violation is raised when the step ends. The messages are shown by hand
/// (<see cref="World.ObservePrompt"/> and <see cref="World.ObserveBanner"/>), because production shows none of these today; the wording rules themselves
/// are unit-tested in <see cref="MessageWordingTests"/>. They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class PlainWordsProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public PlainWordsProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>Makes a world with one computer, expecting the "new" finding (the violations these tests provoke are not known findings).</summary>
    /// <param name="name">The test's name.</param>
    /// <param name="bob">The computer.</param>
    private World NewWorld(string name, out Computer bob)
    {
        var world = new World(_output, name);
        world.ExpectFindings(Findings.NewFinding);
        world.CreateDatabase();
        bob = world.AddComputer("bob");
        return world;
    }

    /// <summary>A message that says "working copy", a question button that says "safe", and a banner that shows a path are each reported by I7.</summary>
    [HarnessFact]
    public void MessagesQuestionsAndBanners_AreCheckedForPlainWords()
    {
        using var world = NewWorld(nameof(MessagesQuestionsAndBanners_AreCheckedForPlainWords), out var bob);

        world.Do(bob, "Bob is shown three badly worded things", () =>
        {
            world.ObservePrompt(bob, new PromptCall(PromptKind.Message, "Trouble", "We kept your working copy.", "OK"));
            world.ObservePrompt(bob, new PromptCall(PromptKind.Choice, "Question", "Keep going?", "Stay safe|Leave", ChoiceAnswer.Affirmative));
            world.ObserveBanner(bob, $"Could not reach {world.ShareDir}\\dept.db. Try later.");
            return 0;
        });

        var reasons = world.Violations.Where(v => v.Kind == ViolationKind.MessageNotPlainWords).Select(v => v.Reason).ToList();
        _output.WriteLine(string.Join(Environment.NewLine, reasons));

        Assert.Contains(reasons, r => r.Contains("working copy") && r.Contains("text"));
        Assert.Contains(reasons, r => r.Contains("\"safe\"") && r.Contains("button"));
        Assert.Contains(reasons, r => r.Contains("program's folders") && r.Contains("text"));
        Assert.All(world.Violations.Where(v => v.Kind == ViolationKind.MessageNotPlainWords), v => Assert.Equal("I7", v.Invariant));
    }

    /// <summary>A plain message, question and banner raise nothing.</summary>
    [HarnessFact]
    public void PlainMessages_RaiseNothing()
    {
        using var world = NewWorld(nameof(PlainMessages_RaiseNothing), out var bob);

        world.Do(bob, "Bob is shown a plain message", () =>
        {
            world.ObservePrompt(bob, new PromptCall(PromptKind.Message, "Trouble", "Something went wrong. Try again later.", "OK"));
            return 0;
        });

        Assert.Empty(world.Violations.Where(v => v.Kind == ViolationKind.MessageNotPlainWords));
    }

    /// <summary>I8 (a): the same message twice on one computer in one step is a violation; the same message in two steps is not.</summary>
    [HarnessFact]
    public void TheSameMessageTwiceInOneStep_IsReported_ButNotInTwoSteps()
    {
        using var world = NewWorld(nameof(TheSameMessageTwiceInOneStep_IsReported_ButNotInTwoSteps), out var bob);
        var message = new PromptCall(PromptKind.Message, "Trouble", "Something went wrong. Try again later.", "OK");

        world.Do(bob, "step one", () => { world.ObservePrompt(bob, message); return 0; });
        world.Do(bob, "step two", () => { world.ObservePrompt(bob, message); return 0; });
        Assert.Empty(world.Violations.Where(v => v.Kind == ViolationKind.MessageShownTwice));

        world.Do(bob, "step three", () => { world.ObservePrompt(bob, message); world.ObservePrompt(bob, message); return 0; });
        var twice = Assert.Single(world.Violations, v => v.Kind == ViolationKind.MessageShownTwice);
        Assert.Equal("I8", twice.Invariant);
    }
}
