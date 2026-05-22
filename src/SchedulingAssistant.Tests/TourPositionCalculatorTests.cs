using Avalonia;
using SchedulingAssistant.Helpers;
using SchedulingAssistant.Models.Tour;
using Xunit;

namespace SchedulingAssistant.Tests;

public class TourPositionCalculatorTests
{
    // Standard overlay size (1400 x 800) and a centered target (200x100 at 600,350)
    private static readonly Size Overlay = new(1400, 800);
    private static readonly Rect CenteredTarget = new(600, 350, 200, 100);

    // ── Highlight ring ───────────────────────────────────────────────────────

    [Fact]
    public void ComputeHighlightRect_ExpandsTargetByExpansionAmount()
    {
        var target = new Rect(100, 200, 300, 150);
        var ring = TourPositionCalculator.ComputeHighlightRect(target);

        Assert.Equal(96, ring.X);
        Assert.Equal(196, ring.Y);
        Assert.Equal(308, ring.Width);
        Assert.Equal(158, ring.Height);
    }

    [Fact]
    public void ComputeHighlightRect_CustomExpansion()
    {
        var target = new Rect(50, 50, 100, 100);
        var ring = TourPositionCalculator.ComputeHighlightRect(target, 10);

        Assert.Equal(40, ring.X);
        Assert.Equal(40, ring.Y);
        Assert.Equal(120, ring.Width);
        Assert.Equal(120, ring.Height);
    }

    // ── Preferred placement fits ─────────────────────────────────────────────

    [Fact]
    public void ComputeCardPosition_PreferredRight_UsesRight()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Right);

        Assert.Equal(TourPlacement.Right, pos.ActualPlacement);
        // Card left edge = highlight right + gap
        Assert.True(pos.CardMargin.Left > CenteredTarget.Right);
    }

    [Fact]
    public void ComputeCardPosition_PreferredLeft_UsesLeft()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Left);

        Assert.Equal(TourPlacement.Left, pos.ActualPlacement);
        // Card right edge = card left + card width, should be less than highlight left
        Assert.True(pos.CardMargin.Left + TourPositionCalculator.DefaultCardWidth < CenteredTarget.Left);
    }

    [Fact]
    public void ComputeCardPosition_PreferredBelow_UsesBelow()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Below);

        Assert.Equal(TourPlacement.Below, pos.ActualPlacement);
        Assert.True(pos.CardMargin.Top > CenteredTarget.Bottom);
    }

    [Fact]
    public void ComputeCardPosition_PreferredAbove_UsesAbove()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Above);

        Assert.Equal(TourPlacement.Above, pos.ActualPlacement);
        Assert.True(pos.CardMargin.Top + TourPositionCalculator.EstimatedCardHeight < CenteredTarget.Top);
    }

    [Fact]
    public void ComputeCardPosition_Auto_DefaultsToRightPriority()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Auto);

        Assert.Equal(TourPlacement.Right, pos.ActualPlacement);
    }

    // ── Fallback when preferred doesn't fit ──────────────────────────────────

    [Fact]
    public void ComputeCardPosition_RightDoesntFit_FallsBackToBelow()
    {
        // Target at far right — no room for card on the right
        var target = new Rect(1200, 350, 150, 100);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, Overlay, TourPlacement.Right);

        // Right doesn't fit, so it should try Below (next in priority after Right)
        Assert.NotEqual(TourPlacement.Right, pos.ActualPlacement);
    }

    [Fact]
    public void ComputeCardPosition_LeftDoesntFit_FallsBack()
    {
        // Target at far left — no room for card on the left
        var target = new Rect(10, 350, 150, 100);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, Overlay, TourPlacement.Left);

        // Should fall back to Right (first in priority order)
        Assert.Equal(TourPlacement.Right, pos.ActualPlacement);
    }

    // ── Arrow offset points at target center ─────────────────────────────────

    [Fact]
    public void ComputeCardPosition_ArrowOffset_PointsAtTargetCenter_Horizontal()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Right);

        // For Right placement, arrow offset is vertical (target center Y - card top)
        var targetCenterY = CenteredTarget.Y + CenteredTarget.Height / 2;
        var expectedOffset = targetCenterY - pos.CardMargin.Top;

        Assert.Equal(expectedOffset, pos.ArrowOffset, precision: 1);
    }

    [Fact]
    public void ComputeCardPosition_ArrowOffset_PointsAtTargetCenter_Vertical()
    {
        var pos = TourPositionCalculator.ComputeCardPosition(
            CenteredTarget, Overlay, TourPlacement.Below);

        // For Below placement, arrow offset is horizontal (target center X - card left)
        var targetCenterX = CenteredTarget.X + CenteredTarget.Width / 2;
        var expectedOffset = targetCenterX - pos.CardMargin.Left;

        Assert.Equal(expectedOffset, pos.ArrowOffset, precision: 1);
    }

    // ── Secondary axis clamping ──────────────────────────────────────────────

    [Fact]
    public void ComputeCardPosition_ClampsSecondaryAxis_NearTopEdge()
    {
        // Target near the top — card on right would want negative Y
        var target = new Rect(200, 10, 150, 30);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, Overlay, TourPlacement.Right);

        Assert.Equal(TourPlacement.Right, pos.ActualPlacement);
        Assert.True(pos.CardMargin.Top >= TourPositionCalculator.EdgeMargin);
    }

    [Fact]
    public void ComputeCardPosition_ClampsSecondaryAxis_NearBottomEdge()
    {
        // Target near the bottom — card on right would overflow
        var target = new Rect(200, 750, 150, 30);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, Overlay, TourPlacement.Right);

        Assert.True(pos.CardMargin.Top + TourPositionCalculator.EstimatedCardHeight
            <= Overlay.Height - TourPositionCalculator.EdgeMargin + 1);
    }

    // ── All sides overflow (small overlay) ───────────────────────────────────

    [Fact]
    public void ComputeCardPosition_AllSidesOverflow_ReturnsClampedFallback()
    {
        // Very small overlay — nothing fits cleanly
        var tinyOverlay = new Size(350, 220);
        var target = new Rect(10, 10, 330, 200);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, tinyOverlay, TourPlacement.Right);

        // Should still return a valid position (clamped)
        Assert.NotNull(pos);
        Assert.True(pos.CardMargin.Left >= TourPositionCalculator.EdgeMargin);
    }

    // ── Centered fallback ────────────────────────────────────────────────────

    [Fact]
    public void ComputeCenteredPosition_CentersCardInOverlay()
    {
        var pos = TourPositionCalculator.ComputeCenteredPosition(Overlay);

        var expectedX = (Overlay.Width - TourPositionCalculator.DefaultCardWidth) / 2;
        var expectedY = (Overlay.Height - TourPositionCalculator.EstimatedCardHeight) / 2;

        Assert.Equal(expectedX, pos.CardMargin.Left);
        Assert.Equal(expectedY, pos.CardMargin.Top);
        Assert.Equal(TourPlacement.Auto, pos.ActualPlacement);
    }

    // ── Arrow offset stays within card bounds ────────────────────────────────

    [Fact]
    public void ComputeCardPosition_ArrowOffset_ClampedToCardBounds()
    {
        // Target near very top — arrow offset would be small
        var target = new Rect(200, 2, 150, 5);
        var pos = TourPositionCalculator.ComputeCardPosition(
            target, Overlay, TourPlacement.Right);

        // Arrow offset must be at least 16px from the card edge
        Assert.True(pos.ArrowOffset >= 16);
    }
}
