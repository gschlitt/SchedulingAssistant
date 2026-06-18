using TermPoint.Models.Tour;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="TourRunner"/> state machine.
/// Each test initializes TourCatalog with test data and creates a fresh TourRunner.
/// </summary>
public class TourRunnerTests : IDisposable
{
    private readonly TourRunner _runner = new();

    public void Dispose() => TourCatalog.Reset();

    private static TourStep MakeStep(string key) =>
        new(key, new TourTarget(TourTargetKind.NamedControl, "Control"), "Title", "Body");

    private void SetupCatalog(
        TourStep[]? steps = null,
        TourSegment[]? segments = null,
        TourDefinition[]? tours = null)
    {
        TourCatalog.Initialize(
            steps ?? Array.Empty<TourStep>(),
            segments ?? Array.Empty<TourSegment>(),
            tours ?? Array.Empty<TourDefinition>());
    }

    private void SetupSimpleTour()
    {
        SetupCatalog(
            steps: new[] { MakeStep("s1"), MakeStep("s2"), MakeStep("s3") },
            segments: new[]
            {
                new TourSegment("seg1", "First Segment", new[] { "s1", "s2" }),
                new TourSegment("seg2", "Second Segment", new[] { "s3" })
            },
            tours: new[] { new TourDefinition("tour1", "Test Tour", "Desc", new[] { "seg1", "seg2" }) });
    }

    [Fact]
    public void Start_SetsInProgressAtIndex0()
    {
        SetupSimpleTour();

        var result = _runner.Start("tour1");

        Assert.True(result);
        Assert.True(_runner.IsActive);
        Assert.Equal(TourStatus.InProgress, _runner.Progress!.Status);
        Assert.Equal(0, _runner.Progress.CurrentSegmentIndex);
        Assert.Equal(0, _runner.Progress.CurrentStepIndex);
    }

    [Fact]
    public void Start_UnknownKey_ReturnsFalse()
    {
        SetupSimpleTour();

        Assert.False(_runner.Start("nonexistent"));
        Assert.False(_runner.IsActive);
    }

    [Fact]
    public void Start_FiresStepChanged()
    {
        SetupSimpleTour();
        TourStep? received = null;
        _runner.StepChanged += step => received = step;

        _runner.Start("tour1");

        Assert.NotNull(received);
        Assert.Equal("s1", received!.Key);
    }

    [Fact]
    public void Advance_WithinSegment_IncrementsStepIndex()
    {
        SetupSimpleTour();
        _runner.Start("tour1");

        _runner.Advance();

        Assert.Equal(0, _runner.Progress!.CurrentSegmentIndex);
        Assert.Equal(1, _runner.Progress.CurrentStepIndex);
    }

    [Fact]
    public void Advance_CrossesSegmentBoundary()
    {
        SetupSimpleTour();
        _runner.Start("tour1");
        _runner.Advance(); // s1 -> s2

        _runner.Advance(); // s2 -> s3 (crosses to seg2)

        Assert.Equal(1, _runner.Progress!.CurrentSegmentIndex);
        Assert.Equal(0, _runner.Progress.CurrentStepIndex);
    }

    [Fact]
    public void Advance_OnLastStep_Completes()
    {
        SetupSimpleTour();
        _runner.Start("tour1");
        bool completed = false;
        _runner.TourCompleted += () => completed = true;

        _runner.Advance(); // s1 -> s2
        _runner.Advance(); // s2 -> s3 (seg boundary)
        _runner.Advance(); // s3 -> completed

        Assert.True(completed);
        Assert.False(_runner.IsActive);
    }

    [Fact]
    public void Dismiss_SetsDismissedAndFiresEvent()
    {
        SetupSimpleTour();
        _runner.Start("tour1");
        bool dismissed = false;
        _runner.TourDismissed += () => dismissed = true;

        _runner.Dismiss();

        Assert.True(dismissed);
        Assert.False(_runner.IsActive);
    }

    [Fact]
    public void StartNewTour_DismissesActiveTour()
    {
        SetupCatalog(
            steps: new[] { MakeStep("a1"), MakeStep("b1") },
            segments: new[]
            {
                new TourSegment("segA", "A", new[] { "a1" }),
                new TourSegment("segB", "B", new[] { "b1" })
            },
            tours: new[]
            {
                new TourDefinition("tourA", "A", "Desc", new[] { "segA" }),
                new TourDefinition("tourB", "B", "Desc", new[] { "segB" })
            });

        _runner.Start("tourA");
        bool dismissed = false;
        _runner.TourDismissed += () => dismissed = true;

        _runner.Start("tourB");

        Assert.True(dismissed);
        Assert.Equal("tourB", _runner.ActiveTour!.Key);
    }

    [Fact]
    public void ResolveCurrentStep_ReturnsCorrectStep()
    {
        SetupSimpleTour();
        _runner.Start("tour1");
        _runner.Advance(); // now at s2

        var step = _runner.ResolveCurrentStep();

        Assert.NotNull(step);
        Assert.Equal("s2", step!.Key);
    }

    [Fact]
    public void GetGlobalStepIndex_ReflectsPositionAcrossSegments()
    {
        SetupSimpleTour();
        _runner.Start("tour1");

        Assert.Equal(0, _runner.GetGlobalStepIndex()); // s1
        _runner.Advance();
        Assert.Equal(1, _runner.GetGlobalStepIndex()); // s2
        _runner.Advance();
        Assert.Equal(2, _runner.GetGlobalStepIndex()); // s3 (seg2)
    }

    [Fact]
    public void GetGlobalStepCount_ReturnsTotalSteps()
    {
        SetupSimpleTour();
        _runner.Start("tour1");

        Assert.Equal(3, _runner.GetGlobalStepCount());
    }

    [Fact]
    public void GetCurrentSegmentTitle_ReturnsSegmentTitle()
    {
        SetupSimpleTour();
        _runner.Start("tour1");

        Assert.Equal("First Segment", _runner.GetCurrentSegmentTitle());

        _runner.Advance(); // s2
        _runner.Advance(); // s3 (seg2)

        Assert.Equal("Second Segment", _runner.GetCurrentSegmentTitle());
    }
}
