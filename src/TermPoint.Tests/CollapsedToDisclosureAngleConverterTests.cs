using System.Globalization;
using Avalonia;
using TermPoint.Converters;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="CollapsedToDisclosureAngleConverter"/>, which maps a section card's
/// "is collapsed" flag to the rotation angle of its disclosure triangle (spec item 21, step 4b):
/// collapsed points right (0 degrees), expanded points down (90 degrees clockwise). Pure
/// conversion logic, so no Avalonia runtime is needed.
/// </summary>
public class CollapsedToDisclosureAngleConverterTests
{
    /// <summary>Runs the converter's forward conversion with the culture and parameters it ignores.</summary>
    private static object Convert(object? value) =>
        CollapsedToDisclosureAngleConverter.Instance.Convert(value, typeof(double), null, CultureInfo.InvariantCulture);

    /// <summary>A collapsed card's triangle is unrotated, so it points right.</summary>
    [Fact]
    public void Collapsed_ReturnsZeroDegrees()
    {
        var result = Convert(true);

        Assert.IsType<double>(result);
        Assert.Equal(0d, (double)result);
    }

    /// <summary>An expanded card's triangle is turned 90 degrees clockwise, so it points down.</summary>
    [Fact]
    public void Expanded_ReturnsNinetyDegrees()
    {
        var result = Convert(false);

        Assert.IsType<double>(result);
        Assert.Equal(90d, (double)result);
    }

    /// <summary>
    /// A missing or unset binding value (null, or Avalonia's unset sentinel) shows the default
    /// collapsed look rather than failing.
    /// </summary>
    [Fact]
    public void NonBooleanInput_IsTreatedAsCollapsed()
    {
        Assert.Equal(0d, (double)Convert(null));
        Assert.Equal(0d, (double)Convert(AvaloniaProperty.UnsetValue));
        Assert.Equal(0d, (double)Convert("false"));
    }

    /// <summary>The exposed constants are the angles the converter returns.</summary>
    [Fact]
    public void Constants_MatchReturnedAngles()
    {
        Assert.Equal(CollapsedToDisclosureAngleConverter.CollapsedAngle, (double)Convert(true));
        Assert.Equal(CollapsedToDisclosureAngleConverter.ExpandedAngle, (double)Convert(false));
    }

    /// <summary>The conversion is one-way: nothing binds the angle back to the collapsed flag.</summary>
    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            CollapsedToDisclosureAngleConverter.Instance.ConvertBack(90d, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
