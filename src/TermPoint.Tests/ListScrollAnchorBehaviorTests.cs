using Avalonia.Input;
using TermPoint.Behaviors;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the pure, UI-free parts of <see cref="ListScrollAnchorBehavior"/> (spec item 21,
/// "Virtualization re-test"): the geometry that decides where a card must be scrolled to, and the
/// oscillation guard that stops scroll anchoring if the layout fights its corrections. The
/// layout-driven part of the behavior needs a real visual tree and is checked by hand in the app.
/// </summary>
public class ListScrollAnchorBehaviorTests
{
    // ---- ComputeRevealOffset -------------------------------------------------------------

    /// <summary>A card that is entirely inside the viewport needs no scrolling.</summary>
    [Fact]
    public void Reveal_FullyVisibleCard_NeedsNoScroll()
    {
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 100, itemHeight: 80, viewportHeight: 500, currentOffsetY: 300));
    }

    /// <summary>A card clipped by the top of the viewport is scrolled so its top is flush with it.</summary>
    [Fact]
    public void Reveal_CardClippedAbove_AlignsItsTop()
    {
        var result = ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: -30, itemHeight: 100, viewportHeight: 500, currentOffsetY: 200);

        Assert.Equal(170d, result);
    }

    /// <summary>A card clipped by the bottom of the viewport is scrolled so its bottom is flush with it.</summary>
    [Fact]
    public void Reveal_CardClippedBelow_AlignsItsBottom()
    {
        // Bottom edge is at 450 + 100 = 550, 50 below a 500-high viewport.
        var result = ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 450, itemHeight: 100, viewportHeight: 500, currentOffsetY: 200);

        Assert.Equal(250d, result);
    }

    /// <summary>A card exactly as tall as the viewport counts as fitting: only its top matters then.</summary>
    [Fact]
    public void Reveal_CardExactlyViewportHeight_IsTreatedAsFitting()
    {
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 0, itemHeight: 500, viewportHeight: 500, currentOffsetY: 40));
    }

    /// <summary>
    /// A card taller than the viewport (the editor open) is positioned by its top, whether it
    /// currently starts below or above the viewport top.
    /// </summary>
    [Fact]
    public void Reveal_CardTallerThanViewport_AlignsItsTop()
    {
        Assert.Equal(320d, ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 120, itemHeight: 900, viewportHeight: 500, currentOffsetY: 200));

        Assert.Equal(120d, ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: -80, itemHeight: 900, viewportHeight: 500, currentOffsetY: 200));
    }

    /// <summary>A tall card whose top is already flush with the viewport (within tolerance) is left alone.</summary>
    [Fact]
    public void Reveal_TallCardAlreadyTopAligned_NeedsNoScroll()
    {
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 0, itemHeight: 900, viewportHeight: 500, currentOffsetY: 200));
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: 0.3, itemHeight: 900, viewportHeight: 500, currentOffsetY: 200));
    }

    /// <summary>The computed offset is never negative, however far above the card appears to be.</summary>
    [Fact]
    public void Reveal_NeverReturnsNegativeOffset()
    {
        var result = ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: -300, itemHeight: 100, viewportHeight: 500, currentOffsetY: 100);

        Assert.Equal(0d, result);
    }

    /// <summary>With no viewport or no card height there is nothing meaningful to compute.</summary>
    [Fact]
    public void Reveal_DegenerateGeometry_NeedsNoScroll()
    {
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: -50, itemHeight: 100, viewportHeight: 0, currentOffsetY: 200));
        Assert.Null(ListScrollAnchorBehavior.ComputeRevealOffset(
            itemTop: -50, itemHeight: 0, viewportHeight: 500, currentOffsetY: 200));
    }

    // ---- CorrectionRateLimiter -----------------------------------------------------------

    /// <summary>Creates a limiter with the behavior's real limits (20 corrections per 500 ms).</summary>
    private static ListScrollAnchorBehavior.CorrectionRateLimiter NewLimiter() =>
        new(ListScrollAnchorBehavior.MaxCorrectionsPerWindow, ListScrollAnchorBehavior.OscillationWindowMs);

    /// <summary>Twenty corrections inside one window are allowed; the twenty-first trips the guard.</summary>
    [Fact]
    public void Limiter_TripsOnTheCorrectionAfterTheLimit()
    {
        var limiter = NewLimiter();

        for (int i = 0; i < ListScrollAnchorBehavior.MaxCorrectionsPerWindow; i++)
            Assert.False(limiter.RegisterAndCheck(1000 + i), $"correction {i + 1} should be allowed");

        Assert.True(limiter.RegisterAndCheck(1100));
    }

    /// <summary>Corrections that are spread out in time, so the window never holds too many, never trip it.</summary>
    [Fact]
    public void Limiter_SpreadOutCorrections_NeverTrip()
    {
        var limiter = NewLimiter();

        // One correction every 100 ms for 10 seconds: at most 6 in any 500 ms window.
        for (long t = 0; t < 10_000; t += 100)
            Assert.False(limiter.RegisterAndCheck(t));
    }

    /// <summary>Old corrections age out of the window, so a burst after a quiet spell starts afresh.</summary>
    [Fact]
    public void Limiter_OldCorrectionsExpire()
    {
        var limiter = NewLimiter();

        for (int i = 0; i < ListScrollAnchorBehavior.MaxCorrectionsPerWindow; i++)
            limiter.RegisterAndCheck(0);

        // 600 ms later the earlier burst is outside the 500 ms window.
        Assert.False(limiter.RegisterAndCheck(600));
    }

    /// <summary>Reset forgets the history, as happens when the user presses or wheels and anchoring resumes.</summary>
    [Fact]
    public void Limiter_Reset_ClearsHistory()
    {
        var limiter = NewLimiter();

        for (int i = 0; i <= ListScrollAnchorBehavior.MaxCorrectionsPerWindow; i++)
            limiter.RegisterAndCheck(0);
        Assert.True(limiter.RegisterAndCheck(1));

        limiter.Reset();

        Assert.False(limiter.RegisterAndCheck(2));
    }

    // ---- IsScrollKey ---------------------------------------------------------------------

    /// <summary>
    /// Only PageUp and PageDown are user-scroll hints: they are the keys the ScrollViewer itself turns
    /// into scrolling. Home / End / Up / Down are selection navigation, and other keys are typing.
    /// </summary>
    [Theory]
    [InlineData(Key.PageUp, true)]
    [InlineData(Key.PageDown, true)]
    [InlineData(Key.Home, false)]
    [InlineData(Key.End, false)]
    [InlineData(Key.Up, false)]
    [InlineData(Key.Down, false)]
    [InlineData(Key.Left, false)]
    [InlineData(Key.Enter, false)]
    [InlineData(Key.Space, false)]
    [InlineData(Key.A, false)]
    public void IsScrollKey_ClassifiesKeys(Key key, bool expected)
    {
        Assert.Equal(expected, ListScrollAnchorBehavior.IsScrollKey(key));
    }
}
