using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>Tests for <see cref="ColorMath"/>: hex parsing, contrast with black, OKLab distance.</summary>
public sealed class ColorMathTests
{
    /// <summary>#RRGGBB parses to its bytes, in either case and with surrounding spaces.</summary>
    [Theory]
    [InlineData("#B7E79D", 0xB7, 0xE7, 0x9D)]
    [InlineData("#b7e79d", 0xB7, 0xE7, 0x9D)]
    [InlineData("  #B7E79D ", 0xB7, 0xE7, 0x9D)]
    [InlineData("#000000", 0, 0, 0)]
    public void TryParseHex_RrGgBb_Parses(string hex, int r, int g, int b)
    {
        Assert.True(ColorMath.TryParseHex(hex, out var rgb));
        Assert.Equal(((byte)r, (byte)g, (byte)b), rgb);
    }

    /// <summary>#AARRGGBB parses with the alpha dropped.</summary>
    [Fact]
    public void TryParseHex_AaRrGgBb_DropsAlpha()
    {
        Assert.True(ColorMath.TryParseHex("#80B7E79D", out var rgb));
        Assert.Equal(((byte)0xB7, (byte)0xE7, (byte)0x9D), rgb);
    }

    /// <summary>Anything that is not #RRGGBB / #AARRGGBB fails.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("B7E79D")]
    [InlineData("#B7E79")]
    [InlineData("#B7E79DE")]
    [InlineData("#GGGGGG")]
    [InlineData("#B7 E79D")]
    [InlineData("#+7E79D")]
    [InlineData("not-a-colour")]
    public void TryParseHex_BadInput_Fails(string? hex)
    {
        Assert.False(ColorMath.TryParseHex(hex, out _));
    }

    /// <summary>NormalizeHex gives upper-case #RRGGBB, or null.</summary>
    [Fact]
    public void NormalizeHex_NormalisesOrNull()
    {
        Assert.Equal("#B7E79D", ColorMath.NormalizeHex(" #80b7e79d "));
        Assert.Null(ColorMath.NormalizeHex("nope"));
    }

    /// <summary>Known WCAG contrast values: mid grey 5.3, white 21, black 1.</summary>
    [Fact]
    public void ContrastWithBlack_KnownValues()
    {
        Assert.InRange(ColorMath.ContrastWithBlack("#808080"), 5.25, 5.35);
        Assert.Equal(21.0, ColorMath.ContrastWithBlack("#FFFFFF"), 6);
        Assert.Equal(1.0, ColorMath.ContrastWithBlack("#000000"), 6);
    }

    /// <summary>IsTooDark uses the 7:1 threshold.</summary>
    [Fact]
    public void IsTooDark_UsesSevenToOne()
    {
        Assert.True(ColorMath.IsTooDark("#808080"));
        Assert.False(ColorMath.IsTooDark("#B7E79D"));
        Assert.Equal(7.0, ColorMath.MinimumContrastWithBlack);
    }

    /// <summary>OKLab distance: 0 for identical, symmetric, black to white about 1.</summary>
    [Fact]
    public void OkLabDistance_BasicProperties()
    {
        Assert.Equal(0.0, ColorMath.OkLabDistance("#B7E79D", "#B7E79D"), 9);
        Assert.Equal(ColorMath.OkLabDistance("#B7E79D", "#EBB4E6"), ColorMath.OkLabDistance("#EBB4E6", "#B7E79D"), 12);
        Assert.Equal(1.0, ColorMath.OkLabDistance("#000000", "#FFFFFF"), 2);
    }
}
