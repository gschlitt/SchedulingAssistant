using SchedulingAssistant.Models.Tour;
using Xunit;

namespace SchedulingAssistant.Tests;

public class TourProgressTests
{
    [Fact]
    public void Start_SetsCorrectInitialState()
    {
        var progress = TourProgress.Start("my-tour");

        Assert.Equal("my-tour", progress.TourKey);
        Assert.Equal(TourStatus.InProgress, progress.Status);
        Assert.Equal(0, progress.CurrentSegmentIndex);
        Assert.Equal(0, progress.CurrentStepIndex);
        Assert.Null(progress.CompletedAt);
    }

    [Fact]
    public void Status_CanTransitionToCompleted()
    {
        var progress = TourProgress.Start("t");

        progress.Status = TourStatus.Completed;
        progress.CompletedAt = DateTime.UtcNow;

        Assert.Equal(TourStatus.Completed, progress.Status);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public void Status_CanTransitionToDismissed()
    {
        var progress = TourProgress.Start("t");

        progress.Status = TourStatus.Dismissed;
        progress.CompletedAt = DateTime.UtcNow;

        Assert.Equal(TourStatus.Dismissed, progress.Status);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public void TourKey_IsImmutable()
    {
        var progress = TourProgress.Start("fixed-key");

        // TourKey has no setter — this is a compile-time guarantee.
        // Verify it stays as set by the factory.
        Assert.Equal("fixed-key", progress.TourKey);
    }
}
