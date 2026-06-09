using Avalonia;
using SchedulingAssistant.Models.Tour;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for <see cref="TourOverlayViewModel"/> — verifies that TourRunner events
/// correctly update all overlay display properties.
/// </summary>
public class TourOverlayViewModelTests : IDisposable
{
    private readonly TourRunner _runner = new();
    private readonly TourOverlayViewModel _vm;

    public TourOverlayViewModelTests()
    {
        _vm = new TourOverlayViewModel(_runner);
        // Provide a default overlay size so positioning doesn't fall back to centered
        _vm.UpdateOverlaySize(new Size(1400, 800));
        // Replace the real dispatcher "let layout settle" hop with a no-op. Touching
        // Dispatcher.UIThread in a headless test host spins up Avalonia UI threading
        // that nothing tears down, which keeps the test process alive after the run
        // (the suite passes but the host never exits). The no-op keeps tests headless.
        _vm.YieldForLayoutAsync = () => Task.CompletedTask;
    }

    public void Dispose() => TourCatalog.Reset();

    private static TourStep MakeStep(string key, string title = "Title", string body = "Body") =>
        new(key, new TourTarget(TourTargetKind.NamedControl, "TestControl"), title, body);

    private void SetupSimpleTour()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1", "First Step", "First body"), MakeStep("s2", "Second Step", "Second body"), MakeStep("s3", "Third Step", "Third body") },
            new[]
            {
                new TourSegment("seg1", "Intro", new[] { "s1", "s2" }),
                new TourSegment("seg2", "Advanced", new[] { "s3" })
            },
            new[] { new TourDefinition("tour1", "Test Tour", "Desc", new[] { "seg1", "seg2" }) });
    }

    /// <summary>Provides a mock target resolver that returns fixed bounds.</summary>
    private void InjectMockResolver(Rect? bounds = null)
    {
        _vm.ResolveTargetAsync = _ => Task.FromResult(bounds ?? (Rect?)new Rect(200, 300, 400, 100));
    }

    // ── StepChanged updates content properties ───────────────────────────────

    [Fact]
    public void StepChanged_UpdatesTitleAndBody()
    {
        SetupSimpleTour();
        InjectMockResolver();

        _runner.Start("tour1");

        Assert.Equal("First Step", _vm.Title);
        Assert.Equal("First body", _vm.Body);
    }

    [Fact]
    public void StepChanged_UpdatesSegmentTitle()
    {
        SetupSimpleTour();
        InjectMockResolver();

        _runner.Start("tour1");

        Assert.Equal("Intro", _vm.SegmentTitle);
    }

    [Fact]
    public void StepChanged_UpdatesStepCounterText()
    {
        SetupSimpleTour();
        InjectMockResolver();

        _runner.Start("tour1");

        Assert.Equal("Step 1 of 3", _vm.StepCounterText);
    }

    [Fact]
    public void StepChanged_SetsIsVisible()
    {
        SetupSimpleTour();
        InjectMockResolver();

        Assert.False(_vm.IsVisible);
        _runner.Start("tour1");

        // ShowStepAsync runs asynchronously; check after a short delay
        Assert.True(_vm.IsVisible);
    }

    // ── IsLastStep ───────────────────────────────────────────────────────────

    [Fact]
    public void IsLastStep_FalseOnFirstStep()
    {
        SetupSimpleTour();
        InjectMockResolver();
        _runner.Start("tour1");

        Assert.False(_vm.IsLastStep);
        Assert.Equal("Next >", _vm.AdvanceButtonLabel);
    }

    [Fact]
    public void IsLastStep_TrueOnFinalStep()
    {
        SetupSimpleTour();
        InjectMockResolver();
        _runner.Start("tour1");

        _runner.Advance(); // s1 → s2
        _runner.Advance(); // s2 → s3 (last step)

        Assert.True(_vm.IsLastStep);
        Assert.Equal("Done", _vm.AdvanceButtonLabel);
    }

    // ── Dismiss ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DismissAsync_HidesOverlay()
    {
        SetupSimpleTour();
        InjectMockResolver();
        _runner.Start("tour1");
        Assert.True(_vm.IsVisible);

        await _vm.DismissAsync();

        Assert.False(_vm.IsVisible);
    }

    [Fact]
    public async Task DismissAsync_RunsPostAction()
    {
        bool postActionRan = false;
        var stepWithPost = new TourStep(
            "sp1",
            new TourTarget(TourTargetKind.NamedControl, "C"),
            "T", "B",
            postAction: () => { postActionRan = true; return Task.CompletedTask; });

        TourCatalog.Initialize(
            new[] { stepWithPost },
            new[] { new TourSegment("seg", "Seg", new[] { "sp1" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });

        InjectMockResolver();
        _runner.Start("t1");

        await _vm.DismissAsync();

        Assert.True(postActionRan);
    }

    // ── Advance ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_UpdatesContentToNextStep()
    {
        SetupSimpleTour();
        InjectMockResolver();
        _runner.Start("tour1");

        await _vm.AdvanceAsync();

        Assert.Equal("Second Step", _vm.Title);
        Assert.Equal("Step 2 of 3", _vm.StepCounterText);
    }

    [Fact]
    public async Task AdvanceAsync_RunsPostActionBeforePreAction()
    {
        var callOrder = new List<string>();

        var step1 = new TourStep("a1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "Step 1", "B",
            postAction: () => { callOrder.Add("post1"); return Task.CompletedTask; });

        var step2 = new TourStep("a2",
            new TourTarget(TourTargetKind.NamedControl, "C"), "Step 2", "B",
            preAction: () => { callOrder.Add("pre2"); return Task.CompletedTask; });

        TourCatalog.Initialize(
            new[] { step1, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "a1", "a2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });

        InjectMockResolver();
        _runner.Start("t1");
        callOrder.Clear();

        await _vm.AdvanceAsync();

        Assert.Equal(new[] { "post1", "pre2" }, callOrder);
    }

    // ── Target unresolvable ──────────────────────────────────────────────────

    [Fact]
    public async Task UnresolvableTarget_HidesHighlight()
    {
        SetupSimpleTour();
        // Resolver returns null — target unresolvable
        _vm.ResolveTargetAsync = _ => Task.FromResult<Rect?>(null);

        _runner.Start("tour1");

        // ShowStepAsync retries 3x with 100ms delays; wait for it to finish
        await Task.Delay(500);

        Assert.False(_vm.IsHighlightVisible);
        Assert.Equal(TourPlacement.Auto, _vm.ActualPlacement);
    }

    // ── Tour completed hides overlay ─────────────────────────────────────────

    [Fact]
    public void TourCompleted_HidesOverlay()
    {
        SetupSimpleTour();
        InjectMockResolver();
        _runner.Start("tour1");
        Assert.True(_vm.IsVisible);

        _runner.Advance(); // s1 → s2
        _runner.Advance(); // s2 → s3
        _runner.Advance(); // s3 → completed

        Assert.False(_vm.IsVisible);
    }

    // ── ArrowClass ───────────────────────────────────────────────────────────

    [Fact]
    public void ArrowClass_ReflectsActualPlacement()
    {
        SetupSimpleTour();
        // Target on far left — card will be placed Right, arrow points Left
        _vm.ResolveTargetAsync = _ => Task.FromResult<Rect?>(new Rect(10, 300, 100, 100));

        _runner.Start("tour1");

        Assert.Equal("arrowLeft", _vm.ArrowClass);
    }

    // ── Multi-MidAction ─────────────────────────────────────────────────────

    [Fact]
    public async Task MultiMidActions_RunsOnePerClick()
    {
        var callLog = new List<string>();
        var step = new TourStep("m1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "T",
            "Body0|Body1|Body2|Body3",
            midActions: new Func<Task>[]
            {
                () => { callLog.Add("mid0"); return Task.CompletedTask; },
                () => { callLog.Add("mid1"); return Task.CompletedTask; },
                () => { callLog.Add("mid2"); return Task.CompletedTask; },
            });
        var step2 = MakeStep("m2", "Next", "Done");

        TourCatalog.Initialize(
            new[] { step, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "m1", "m2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        // First 3 clicks run mid-actions, card stays on step m1
        await _vm.AdvanceAsync();
        Assert.Equal("T", _vm.Title);
        Assert.Equal(new[] { "mid0" }, callLog);

        await _vm.AdvanceAsync();
        Assert.Equal("T", _vm.Title);
        Assert.Equal(new[] { "mid0", "mid1" }, callLog);

        await _vm.AdvanceAsync();
        Assert.Equal("T", _vm.Title);
        Assert.Equal(new[] { "mid0", "mid1", "mid2" }, callLog);

        // 4th click advances to next step
        await _vm.AdvanceAsync();
        Assert.Equal("Next", _vm.Title);
    }

    [Fact]
    public async Task MultiMidActions_AutoAdvancesBody()
    {
        var step = new TourStep("b1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "T",
            "Initial|After mid0|After mid1",
            midActions: new Func<Task>[]
            {
                () => Task.CompletedTask,
                () => Task.CompletedTask,
            });

        TourCatalog.Initialize(
            new[] { step },
            new[] { new TourSegment("seg", "Seg", new[] { "b1" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        Assert.Equal("Initial", _vm.Body);

        await _vm.AdvanceAsync();
        Assert.Equal("After mid0", _vm.Body);

        await _vm.AdvanceAsync();
        Assert.Equal("After mid1", _vm.Body);
    }

    [Fact]
    public async Task DismissMidSequence_ResetsIndexAndRunsPostAction()
    {
        bool postRan = false;
        var step = new TourStep("d1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "T", "B1|B2|B3",
            midActions: new Func<Task>[]
            {
                () => Task.CompletedTask,
                () => Task.CompletedTask,
            },
            postAction: () => { postRan = true; return Task.CompletedTask; });

        TourCatalog.Initialize(
            new[] { step },
            new[] { new TourSegment("seg", "Seg", new[] { "d1" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        // Run first mid-action, then dismiss
        await _vm.AdvanceAsync();
        await _vm.DismissAsync();

        Assert.True(postRan);
        Assert.False(_vm.IsVisible);
    }

    [Fact]
    public async Task NoMidActions_AdvancesOnSingleClick()
    {
        var step1 = new TourStep("n1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "First", "Body");
        var step2 = new TourStep("n2",
            new TourTarget(TourTargetKind.NamedControl, "C"), "Second", "Body");

        TourCatalog.Initialize(
            new[] { step1, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "n1", "n2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        Assert.Equal("First", _vm.Title);
        await _vm.AdvanceAsync();
        Assert.Equal("Second", _vm.Title);
    }

    [Fact]
    public async Task MidActionThrows_TourContinues()
    {
        var step = new TourStep("e1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "T", "B1|B2",
            midActions: new Func<Task>[]
            {
                () => throw new InvalidOperationException("boom"),
            });
        var step2 = MakeStep("e2", "Next", "Done");

        TourCatalog.Initialize(
            new[] { step, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "e1", "e2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        // Mid-action throws — should not crash, index still advances
        await _vm.AdvanceAsync();
        Assert.Equal("T", _vm.Title); // Still on step e1

        // Next click advances past mid-actions to next step
        await _vm.AdvanceAsync();
        Assert.Equal("Next", _vm.Title);
    }

    [Fact]
    public async Task PreActionThrows_TourContinues()
    {
        var step1 = MakeStep("p1", "First", "Body");
        var step2 = new TourStep("p2",
            new TourTarget(TourTargetKind.NamedControl, "C"), "Second", "Body",
            preAction: () => throw new InvalidOperationException("boom"));

        TourCatalog.Initialize(
            new[] { step1, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "p1", "p2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        // Advancing to step2 whose PreAction throws — should not crash
        await _vm.AdvanceAsync();
        Assert.Equal("Second", _vm.Title);
        Assert.True(_vm.IsVisible);
    }

    [Fact]
    public async Task PostActionThrows_TourAdvancesNormally()
    {
        var step1 = new TourStep("q1",
            new TourTarget(TourTargetKind.NamedControl, "C"), "First", "Body",
            postAction: () => throw new InvalidOperationException("boom"));
        var step2 = MakeStep("q2", "Second", "Body");

        TourCatalog.Initialize(
            new[] { step1, step2 },
            new[] { new TourSegment("seg", "Seg", new[] { "q1", "q2" }) },
            new[] { new TourDefinition("t1", "T", "D", new[] { "seg" }) });
        InjectMockResolver();
        _runner.Start("t1");

        // PostAction throws — should not crash, should advance
        await _vm.AdvanceAsync();
        Assert.Equal("Second", _vm.Title);
    }
}
