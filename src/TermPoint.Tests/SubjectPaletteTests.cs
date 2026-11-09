using System.Runtime.CompilerServices;
using System.Xml.Linq;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Checks the REAL subject palette in <c>src/TermPoint/AppColors.axaml</c>, so a palette swap (task 5) is
/// re-verified automatically: eleven consecutive colours, all distinct, and
/// far enough apart for <see cref="SubjectColorSuggester.DefaultMinimumDistance"/> to take them in order.
/// </summary>
public sealed class SubjectPaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Finds AppColors.axaml by walking up from THIS SOURCE FILE's folder (not from the test binaries, which may be
    /// built outside the repository, e.g. <c>dotnet test -o E:/...</c>) and reads SubjectPalette01.. in order.
    /// </summary>
    /// <param name="sourceFile">Filled in by the compiler with this file's path; callers leave it out.</param>
    /// <returns>The palette's hex strings, in order.</returns>
    private static List<string> ReadPalette([CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "TermPoint", "AppColors.axaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var doc = XDocument.Load(Path.Combine(dir!.FullName, "src", "TermPoint", "AppColors.axaml"));
        var byKey = doc.Descendants()
            .Where(e => e.Attribute(Xaml + "Key") is not null)
            .ToDictionary(e => e.Attribute(Xaml + "Key")!.Value, e => e.Value.Trim());

        var palette = new List<string>();
        for (var n = 1; byKey.TryGetValue($"SubjectPalette{n:00}", out var hex); n++) palette.Add(hex);

        // Consecutive: no stray SubjectPaletteNN beyond the first gap.
        Assert.Equal(palette.Count, byKey.Keys.Count(k => k.StartsWith("SubjectPalette", StringComparison.Ordinal)));
        return palette;
    }

    /// <summary>There are 11 palette colours, and each parses.</summary>
    [Fact]
    public void Palette_HasElevenParsableColors()
    {
        var palette = ReadPalette();
        Assert.Equal(11, palette.Count);
        Assert.All(palette, h => Assert.True(ColorMath.TryParseHex(h, out _), h));
    }

    /// <summary>All colours are different.</summary>
    [Fact]
    public void Palette_AllDistinct()
    {
        var palette = ReadPalette().Select(h => ColorMath.NormalizeHex(h)!).ToList();
        Assert.Equal(palette.Count, palette.Distinct().Count());
    }

    /// <summary>The suggester threshold does not exceed the closest pair, so an empty database gets the palette in order.</summary>
    [Fact]
    public void DefaultMinimumDistance_NotAboveSmallestPairwiseDistance()
    {
        var palette = ReadPalette();
        var smallest = double.MaxValue;
        for (var i = 0; i < palette.Count; i++)
            for (var j = i + 1; j < palette.Count; j++)
                smallest = Math.Min(smallest, ColorMath.OkLabDistance(palette[i], palette[j]));

        Assert.True(SubjectColorSuggester.DefaultMinimumDistance <= smallest,
            $"DefaultMinimumDistance {SubjectColorSuggester.DefaultMinimumDistance} > smallest palette distance {smallest}");
    }
}
