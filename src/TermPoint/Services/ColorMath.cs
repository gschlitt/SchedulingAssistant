using System.Globalization;

namespace TermPoint.Services;

/// <summary>
/// Pure colour arithmetic with no Avalonia dependency: hex parsing, WCAG contrast against black, and perceptual
/// distance in the OKLab colour space. Used by the subject-colour feature (palette checks, the too-dark note and
/// <see cref="SubjectColorSuggester"/>).
/// </summary>
public static class ColorMath
{
    /// <summary>
    /// The smallest acceptable contrast ratio with black text (WCAG AAA for normal text; spec subject-colours P4-5).
    /// A colour below this gets the "too dark" note.
    /// </summary>
    public const double MinimumContrastWithBlack = 7.0;

    /// <summary>
    /// Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c> (alpha is read and dropped). Surrounding whitespace is ignored and
    /// hex digits may be either case. Anything else (no <c>#</c>, wrong length, non-hex digits, null) fails.
    /// </summary>
    /// <param name="hex">The text to parse; may be null.</param>
    /// <param name="rgb">The parsed red, green and blue bytes; (0,0,0) when parsing fails.</param>
    /// <returns>True if <paramref name="hex"/> was a valid colour.</returns>
    public static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (hex is null) return false;
        var t = hex.Trim();
        if (t.Length is not (7 or 9) || t[0] != '#') return false;

        // Reject anything that is not a plain hex digit (number parsing alone would tolerate spaces or signs).
        for (var i = 1; i < t.Length; i++)
            if (!Uri.IsHexDigit(t[i])) return false;

        var offset = t.Length == 9 ? 3 : 1; // skip the AA of #AARRGGBB
        rgb = (
            byte.Parse(t.AsSpan(offset, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            byte.Parse(t.AsSpan(offset + 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            byte.Parse(t.AsSpan(offset + 4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>Formats a colour as upper-case <c>#RRGGBB</c>.</summary>
    /// <param name="rgb">The colour.</param>
    /// <returns>The hex string.</returns>
    public static string ToHex((byte R, byte G, byte B) rgb) => $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    /// <summary>
    /// Normalises a colour string to upper-case <c>#RRGGBB</c> (alpha dropped).
    /// </summary>
    /// <param name="hex">Text in <c>#RRGGBB</c> or <c>#AARRGGBB</c> form; may be null.</param>
    /// <returns>The normalised string, or null if <paramref name="hex"/> does not parse.</returns>
    public static string? NormalizeHex(string? hex) =>
        TryParseHex(hex, out var rgb) ? ToHex(rgb) : null;

    /// <summary>Converts one 0-255 sRGB channel to linear light (the inverse sRGB transfer function).</summary>
    private static double ToLinear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// WCAG 2 contrast ratio between a colour and black: (L + 0.05) / 0.05, where L is the sRGB relative luminance.
    /// Black is 1, white is 21, #808080 is about 5.3.
    /// </summary>
    /// <param name="rgb">The colour.</param>
    /// <returns>The ratio, from 1 to 21.</returns>
    public static double ContrastWithBlack((byte R, byte G, byte B) rgb)
    {
        var luminance = 0.2126 * ToLinear(rgb.R) + 0.7152 * ToLinear(rgb.G) + 0.0722 * ToLinear(rgb.B);
        return (luminance + 0.05) / 0.05;
    }

    /// <summary>Hex-string overload of the tuple <c>ContrastWithBlack</c>.</summary>
    /// <param name="hex">A colour in <c>#RRGGBB</c> or <c>#AARRGGBB</c> form.</param>
    /// <returns>The ratio.</returns>
    /// <exception cref="ArgumentException"><paramref name="hex"/> does not parse.</exception>
    public static double ContrastWithBlack(string hex) => ContrastWithBlack(ParseOrThrow(hex));

    /// <summary>
    /// True when the colour's contrast with black is below <see cref="MinimumContrastWithBlack"/>.
    /// </summary>
    /// <param name="hex">A colour in <c>#RRGGBB</c> or <c>#AARRGGBB</c> form.</param>
    /// <returns>True if too dark for black text.</returns>
    /// <exception cref="ArgumentException"><paramref name="hex"/> does not parse.</exception>
    public static bool IsTooDark(string hex) => ContrastWithBlack(hex) < MinimumContrastWithBlack;

    /// <summary>
    /// Converts an sRGB colour to OKLab (Bjorn Ottosson's matrices: linear sRGB, then LMS, cube root, then Lab).
    /// </summary>
    private static (double L, double A, double B) ToOkLab((byte R, byte G, byte B) rgb)
    {
        double r = ToLinear(rgb.R), g = ToLinear(rgb.G), b = ToLinear(rgb.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>
    /// Perceptual distance between two colours: Euclidean distance in OKLab. 0 for identical colours; black to white
    /// is about 1.0.
    /// </summary>
    /// <param name="a">First colour.</param>
    /// <param name="b">Second colour.</param>
    /// <returns>The distance (symmetric, never negative).</returns>
    public static double OkLabDistance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var (l1, a1, b1) = ToOkLab(a);
        var (l2, a2, b2) = ToOkLab(b);
        double dl = l1 - l2, da = a1 - a2, db = b1 - b2;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>Hex-string overload of the tuple <c>OkLabDistance</c>.</summary>
    /// <param name="hexA">First colour, <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</param>
    /// <param name="hexB">Second colour, <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</param>
    /// <returns>The distance.</returns>
    /// <exception cref="ArgumentException">Either argument does not parse.</exception>
    public static double OkLabDistance(string hexA, string hexB) =>
        OkLabDistance(ParseOrThrow(hexA), ParseOrThrow(hexB));

    /// <summary>Parses a hex colour or throws <see cref="ArgumentException"/>.</summary>
    private static (byte R, byte G, byte B) ParseOrThrow(string hex) =>
        TryParseHex(hex, out var rgb)
            ? rgb
            : throw new ArgumentException($"Not a colour in #RRGGBB form: '{hex}'.", nameof(hex));
}
