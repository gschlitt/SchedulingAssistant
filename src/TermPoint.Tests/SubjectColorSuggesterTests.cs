using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>Tests for <see cref="SubjectColorSuggester"/>.</summary>
public sealed class SubjectColorSuggesterTests
{
    /// <summary>A small, well-separated test palette (red, green, blue, yellow).</summary>
    private static readonly string[] Palette = { "#FF9999", "#99FF99", "#9999FF", "#FFFF99" };

    /// <summary>Runs the suggester over the test palette and returns abbreviation to colour.</summary>
    private static Dictionary<string, string> Run(IEnumerable<string> names, IEnumerable<string>? inUse = null) =>
        SubjectColorSuggester.Suggest(names, inUse ?? Array.Empty<string>(), Palette)
            .ToDictionary(p => p.Key, p => p.Value);

    /// <summary>Only the abbreviations given get colours; names are normalised and de-duplicated.</summary>
    [Fact]
    public void FillsOnlyTheBlanksGiven()
    {
        var r = Run(new[] { "bio", " BIO ", "chem" });
        Assert.Equal(new[] { "BIO", "CHEM" }, r.Keys.OrderBy(k => k));
    }

    /// <summary>Alphabetical processing: with nothing in use, the palette is taken in order.</summary>
    [Fact]
    public void NothingInUse_TakesPaletteInAlphabeticalOrder()
    {
        var r = Run(new[] { "CHEM", "BIO", "ART" });
        Assert.Equal("#FF9999", r["ART"]);
        Assert.Equal("#99FF99", r["BIO"]);
        Assert.Equal("#9999FF", r["CHEM"]);
    }

    /// <summary>Same input twice, or input shuffled, gives the same output.</summary>
    [Fact]
    public void Deterministic()
    {
        var a = Run(new[] { "ART", "BIO", "CHEM", "DRAM" });
        var b = Run(new[] { "ART", "BIO", "CHEM", "DRAM" });
        var c = Run(new[] { "DRAM", "CHEM", "ART", "BIO" });
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    /// <summary>A colour in use is never reused while a far-enough colour remains.</summary>
    [Fact]
    public void KeepsAwayFromColorsInUse()
    {
        var r = Run(new[] { "ART" }, new[] { "#FF9999" });
        Assert.Equal("#99FF99", r["ART"]);

        // A near-duplicate of palette[0] (within the threshold) also blocks it.
        var r2 = Run(new[] { "ART" }, new[] { "#FF9A9A" });
        Assert.NotEqual("#FF9999", r2["ART"]);
    }

    /// <summary>More subjects than palette colours: all coloured, unused first, then repeats in palette order.</summary>
    [Fact]
    public void MoreSubjectsThanPalette_RepeatsInPaletteOrder()
    {
        var names = new[] { "A", "B", "C", "D", "E", "F", "G" };
        var r = Run(names);
        Assert.Equal(7, r.Count);
        Assert.Equal(new[] { "#FF9999", "#99FF99", "#9999FF", "#FFFF99", "#FF9999", "#99FF99", "#9999FF" },
            names.Select(n => r[n]));
    }

    /// <summary>Fallback prefers palette colours not in use at all over repeating one in use.</summary>
    [Fact]
    public void Fallback_PrefersUnusedColors()
    {
        // Huge threshold: nothing is ever "far enough", so every pick is a fallback.
        var r = SubjectColorSuggester.Suggest(new[] { "A", "B" }, new[] { "#FF9999" }, Palette, 10.0)
            .ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("#99FF99", r["A"]);
        Assert.Equal("#9999FF", r["B"]);
    }
}
