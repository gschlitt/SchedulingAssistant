using TermPoint.Models.Tour;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class TourCatalogTests : IDisposable
{
    public void Dispose() => TourCatalog.Reset();

    private static TourStep MakeStep(string key, string targetValue = "SomeControl") =>
        new(key, new TourTarget(TourTargetKind.NamedControl, targetValue), "Title", "Body");

    private static TourSegment MakeSegment(string key, params string[] stepKeys) =>
        new(key, "Segment Title", stepKeys);

    private static TourDefinition MakeTour(string key, params string[] segmentKeys) =>
        new(key, "Tour Title", "Description", segmentKeys);

    [Fact]
    public void Validate_ValidCatalog_ReturnsNoErrors()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1"), MakeStep("s2") },
            new[] { MakeSegment("seg1", "s1", "s2") },
            new[] { MakeTour("tour1", "seg1") });

        var errors = TourCatalog.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_SegmentReferencesUnknownStep_ReportsError()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            new[] { MakeSegment("seg1", "s1", "s-missing") },
            new[] { MakeTour("tour1", "seg1") });

        var errors = TourCatalog.Validate();

        Assert.Single(errors);
        Assert.Contains("s-missing", errors[0]);
    }

    [Fact]
    public void Validate_TourReferencesUnknownSegment_ReportsError()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            new[] { MakeSegment("seg1", "s1") },
            new[] { MakeTour("tour1", "seg1", "seg-missing") });

        var errors = TourCatalog.Validate();

        Assert.Single(errors);
        Assert.Contains("seg-missing", errors[0]);
    }

    [Fact]
    public void GetStep_ExistingKey_ReturnsStep()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            Array.Empty<TourSegment>(),
            Array.Empty<TourDefinition>());

        Assert.NotNull(TourCatalog.GetStep("s1"));
    }

    [Fact]
    public void GetStep_UnknownKey_ReturnsNull()
    {
        TourCatalog.Initialize(
            Array.Empty<TourStep>(),
            Array.Empty<TourSegment>(),
            Array.Empty<TourDefinition>());

        Assert.Null(TourCatalog.GetStep("nope"));
    }

    [Fact]
    public void GetSegment_ExistingKey_ReturnsSegment()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            new[] { MakeSegment("seg1", "s1") },
            Array.Empty<TourDefinition>());

        Assert.NotNull(TourCatalog.GetSegment("seg1"));
    }

    [Fact]
    public void GetTour_ExistingKey_ReturnsTour()
    {
        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            new[] { MakeSegment("seg1", "s1") },
            new[] { MakeTour("tour1", "seg1") });

        Assert.NotNull(TourCatalog.GetTour("tour1"));
    }

    [Fact]
    public void GetReplayableTours_ExcludesNonReplayable()
    {
        var replayable = MakeTour("t1", "seg1");
        var nonReplayable = new TourDefinition("t2", "Hidden", "Desc", new[] { "seg1" },
            isReplayable: false);

        TourCatalog.Initialize(
            new[] { MakeStep("s1") },
            new[] { MakeSegment("seg1", "s1") },
            new[] { replayable, nonReplayable });

        var result = TourCatalog.GetReplayableTours();

        Assert.Single(result);
        Assert.Equal("t1", result[0].Key);
    }

    [Fact]
    public void Initialize_DuplicateStepKeys_Throws()
    {
        var step1 = MakeStep("dup", "Control1");
        var step2 = MakeStep("dup", "Control2");

        Assert.Throws<ArgumentException>(() =>
            TourCatalog.Initialize(
                new[] { step1, step2 },
                Array.Empty<TourSegment>(),
                Array.Empty<TourDefinition>()));
    }

    [Fact]
    public void Validate_StepWithEmptyTargetValue_NotPossible()
    {
        // TourTarget constructor throws on empty value, so this invariant
        // is enforced at construction time rather than validation time.
        Assert.Throws<ArgumentException>(() =>
            new TourTarget(TourTargetKind.NamedControl, ""));
    }
}
