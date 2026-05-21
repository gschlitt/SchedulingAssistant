using Avalonia;
using SchedulingAssistant.Models.Tour;

namespace SchedulingAssistant.Helpers;

/// <summary>
/// Pure static positioning logic for the tour overlay card and highlight ring.
/// No Avalonia visual tree dependencies — takes geometry in, returns geometry out.
/// </summary>
public static class TourPositionCalculator
{
    /// <summary>Default card width in pixels.</summary>
    public const double CardWidth = 320;

    /// <summary>Estimated card height. Actual height varies with content; used for fit-checking.</summary>
    public const double EstimatedCardHeight = 200;

    /// <summary>Gap between the highlight ring and the card edge (matches arrow length).</summary>
    public const double Gap = 32;

    /// <summary>Minimum margin between the card and the overlay edge.</summary>
    public const double EdgeMargin = 8;

    /// <summary>How far the highlight ring extends beyond the target bounds on each side.</summary>
    public const double HighlightExpansion = 4;

    /// <summary>
    /// Placement priority order when the preferred side doesn't fit.
    /// </summary>
    private static readonly TourPlacement[] PriorityOrder =
        { TourPlacement.Right, TourPlacement.Below, TourPlacement.Left, TourPlacement.Above };

    /// <summary>
    /// Computes the highlight ring rect by expanding the target bounds outward.
    /// </summary>
    /// <param name="targetBounds">Bounds of the target control in overlay coordinates.</param>
    /// <param name="expansion">Pixels to expand on each side (default 4).</param>
    /// <returns>Expanded rect for the highlight ring.</returns>
    public static Rect ComputeHighlightRect(Rect targetBounds, double expansion = HighlightExpansion)
    {
        return new Rect(
            targetBounds.X - expansion,
            targetBounds.Y - expansion,
            targetBounds.Width + expansion * 2,
            targetBounds.Height + expansion * 2);
    }

    /// <summary>
    /// Computes the card position relative to the target, trying the preferred placement
    /// first then falling back through the priority order. Clamps the secondary axis to
    /// keep the card within the overlay bounds.
    /// </summary>
    /// <param name="targetBounds">Target control bounds in overlay coordinates.</param>
    /// <param name="overlaySize">Size of the overlay container.</param>
    /// <param name="preferred">The step's preferred placement hint.</param>
    /// <param name="cardWidth">Card width (default 320).</param>
    /// <param name="cardHeight">Estimated card height (default 200).</param>
    /// <returns>Computed card position with margin, actual placement, and arrow offset.</returns>
    public static CardPosition ComputeCardPosition(
        Rect targetBounds,
        Size overlaySize,
        TourPlacement preferred,
        double cardWidth = CardWidth,
        double cardHeight = EstimatedCardHeight)
    {
        var highlight = ComputeHighlightRect(targetBounds);
        var targetCenterX = targetBounds.X + targetBounds.Width / 2;
        var targetCenterY = targetBounds.Y + targetBounds.Height / 2;

        // Build candidate list: preferred first (if specific), then the standard priority order
        var candidates = new List<TourPlacement>();
        if (preferred != TourPlacement.Auto)
            candidates.Add(preferred);
        foreach (var p in PriorityOrder)
        {
            if (!candidates.Contains(p))
                candidates.Add(p);
        }

        foreach (var placement in candidates)
        {
            var pos = TryPlace(placement, highlight, overlaySize, cardWidth, cardHeight,
                targetCenterX, targetCenterY);
            if (pos is not null)
                return pos;
        }

        // All sides overflow — clamp to the first candidate (preferred or Right)
        return ClampedFallback(candidates[0], highlight, overlaySize, cardWidth, cardHeight,
            targetCenterX, targetCenterY);
    }

    /// <summary>
    /// Computes a centered card position for when the target is unresolvable.
    /// Card is centered in the overlay with no arrow.
    /// </summary>
    /// <param name="overlaySize">Size of the overlay container.</param>
    /// <param name="cardWidth">Card width (default 320).</param>
    /// <param name="cardHeight">Estimated card height (default 200).</param>
    /// <returns>Centered card position.</returns>
    public static CardPosition ComputeCenteredPosition(
        Size overlaySize,
        double cardWidth = CardWidth,
        double cardHeight = EstimatedCardHeight)
    {
        var x = (overlaySize.Width - cardWidth) / 2;
        var y = (overlaySize.Height - cardHeight) / 2;
        return new CardPosition(
            new Thickness(Math.Max(x, EdgeMargin), Math.Max(y, EdgeMargin), 0, 0),
            TourPlacement.Auto,
            0);
    }

    /// <summary>
    /// Attempts to place the card on the given side. Returns null if the card
    /// doesn't fit within the overlay bounds with the required edge margin.
    /// </summary>
    private static CardPosition? TryPlace(
        TourPlacement placement,
        Rect highlight,
        Size overlaySize,
        double cardWidth,
        double cardHeight,
        double targetCenterX,
        double targetCenterY)
    {
        double x, y, arrowOffset;

        switch (placement)
        {
            case TourPlacement.Right:
                x = highlight.Right + Gap;
                y = targetCenterY - cardHeight / 2;
                arrowOffset = targetCenterY - y;
                if (x + cardWidth > overlaySize.Width - EdgeMargin) return null;
                break;

            case TourPlacement.Left:
                x = highlight.Left - Gap - cardWidth;
                y = targetCenterY - cardHeight / 2;
                arrowOffset = targetCenterY - y;
                if (x < EdgeMargin) return null;
                break;

            case TourPlacement.Below:
                x = targetCenterX - cardWidth / 2;
                y = highlight.Bottom + Gap;
                arrowOffset = targetCenterX - x;
                if (y + cardHeight > overlaySize.Height - EdgeMargin) return null;
                break;

            case TourPlacement.Above:
                x = targetCenterX - cardWidth / 2;
                y = highlight.Top - Gap - cardHeight;
                arrowOffset = targetCenterX - x;
                if (y < EdgeMargin) return null;
                break;

            default:
                return null;
        }

        // Clamp secondary axis
        if (placement is TourPlacement.Right or TourPlacement.Left)
        {
            y = Clamp(y, EdgeMargin, overlaySize.Height - cardHeight - EdgeMargin);
            arrowOffset = targetCenterY - y;
        }
        else
        {
            x = Clamp(x, EdgeMargin, overlaySize.Width - cardWidth - EdgeMargin);
            arrowOffset = targetCenterX - x;
        }

        // Clamp arrow offset to stay within the card
        arrowOffset = Clamp(arrowOffset, 16, placement is TourPlacement.Right or TourPlacement.Left
            ? cardHeight - 16
            : cardWidth - 16);

        return new CardPosition(new Thickness(x, y, 0, 0), placement, arrowOffset);
    }

    /// <summary>
    /// When no side fits cleanly, force the card onto the given side and clamp everything.
    /// </summary>
    private static CardPosition ClampedFallback(
        TourPlacement placement,
        Rect highlight,
        Size overlaySize,
        double cardWidth,
        double cardHeight,
        double targetCenterX,
        double targetCenterY)
    {
        double x, y, arrowOffset;

        switch (placement)
        {
            case TourPlacement.Right:
                x = highlight.Right + Gap;
                y = targetCenterY - cardHeight / 2;
                arrowOffset = targetCenterY - y;
                break;
            case TourPlacement.Left:
                x = highlight.Left - Gap - cardWidth;
                y = targetCenterY - cardHeight / 2;
                arrowOffset = targetCenterY - y;
                break;
            case TourPlacement.Below:
                x = targetCenterX - cardWidth / 2;
                y = highlight.Bottom + Gap;
                arrowOffset = targetCenterX - x;
                break;
            default: // Above
                x = targetCenterX - cardWidth / 2;
                y = highlight.Top - Gap - cardHeight;
                arrowOffset = targetCenterX - x;
                break;
        }

        x = Clamp(x, EdgeMargin, overlaySize.Width - cardWidth - EdgeMargin);
        y = Clamp(y, EdgeMargin, overlaySize.Height - cardHeight - EdgeMargin);

        // Recompute arrow offset after clamping
        if (placement is TourPlacement.Right or TourPlacement.Left)
            arrowOffset = targetCenterY - y;
        else
            arrowOffset = targetCenterX - x;

        arrowOffset = Clamp(arrowOffset, 16, placement is TourPlacement.Right or TourPlacement.Left
            ? cardHeight - 16
            : cardWidth - 16);

        return new CardPosition(new Thickness(x, y, 0, 0), placement, arrowOffset);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min) max = min;
        return Math.Max(min, Math.Min(max, value));
    }
}

/// <summary>
/// Result of card position computation.
/// </summary>
/// <param name="CardMargin">Margin (Left, Top) for absolute positioning within the overlay.</param>
/// <param name="ActualPlacement">The side the card was placed on (or Auto if centered).</param>
/// <param name="ArrowOffset">
/// Pixel offset along the card edge where the arrow should point.
/// For Right/Left placement: vertical offset from the card's top.
/// For Above/Below placement: horizontal offset from the card's left.
/// </param>
public record CardPosition(Thickness CardMargin, TourPlacement ActualPlacement, double ArrowOffset);
