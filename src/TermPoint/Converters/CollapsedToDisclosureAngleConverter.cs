using Avalonia.Data.Converters;
using System.Globalization;

namespace TermPoint.Converters;

/// <summary>
/// Converts a card's "is collapsed" flag to the rotation angle, in degrees, of its disclosure
/// triangle: <c>true</c> (collapsed) → <see cref="CollapsedAngle"/> (0°, the triangle points
/// right); <c>false</c> (expanded) → <see cref="ExpandedAngle"/> (90°, clockwise, so it points
/// down).
///
/// Bind it to a <c>RotateTransform.Angle</c> on the triangle's <c>RenderTransform</c>. The section
/// list does exactly that instead of using a style with an <c>expanded</c> class: a style with a
/// class condition attaches style machinery to every <c>Path</c> in the view, and the list has one
/// toggle <c>Path</c> per section card (spec item 21, step 4b). A binding costs nothing on the
/// other elements.
///
/// Anything that is not a boolean (null, or an unset binding value) is treated as collapsed, the
/// default look.
/// </summary>
public class CollapsedToDisclosureAngleConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use via <c>x:Static</c> in XAML.</summary>
    public static readonly CollapsedToDisclosureAngleConverter Instance = new();

    /// <summary>Angle, in degrees, of a collapsed triangle: unrotated, pointing right.</summary>
    public const double CollapsedAngle = 0;

    /// <summary>Angle, in degrees, of an expanded triangle: turned clockwise to point down.</summary>
    public const double ExpandedAngle = 90;

    /// <summary>
    /// Maps the collapsed flag to the triangle's rotation angle.
    /// </summary>
    /// <param name="value">The "is collapsed" flag (a <see cref="bool"/>).</param>
    /// <param name="targetType">Ignored; the result is always a <see cref="double"/>.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns><see cref="ExpandedAngle"/> when <paramref name="value"/> is <c>false</c>; otherwise <see cref="CollapsedAngle"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? ExpandedAngle : CollapsedAngle;

    /// <summary>One-way converter; converting back is not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
