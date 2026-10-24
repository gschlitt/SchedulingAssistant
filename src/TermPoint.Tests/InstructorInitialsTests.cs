using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="InstructorInitials"/>: base initials from the name, collision
/// suffixes, case-insensitive matching against the taken set, and that the set is left alone.
/// </summary>
public class InstructorInitialsTests
{
    /// <summary>An empty set of taken initials.</summary>
    private static HashSet<string> None() => new();

    /// <summary>First and last names give the upper-cased first letters of each.</summary>
    [Fact]
    public void Generate_BothNames_UsesFirstLetters() =>
        Assert.Equal("JS", InstructorInitials.Generate("jane", "smith", None()));

    /// <summary>Names are trimmed before the first letter is taken.</summary>
    [Fact]
    public void Generate_PaddedNames_AreTrimmed() =>
        Assert.Equal("JS", InstructorInitials.Generate("  Jane ", " Smith  ", None()));

    /// <summary>With no first name, the first two letters of the last name are used.</summary>
    [Theory]
    [InlineData("", "smith", "SM")]
    [InlineData(null, "Smith", "SM")]
    [InlineData("   ", "Li", "LI")]
    public void Generate_LastNameOnly_UsesFirstTwoLetters(string? first, string last, string expected) =>
        Assert.Equal(expected, InstructorInitials.Generate(first, last, None()));

    /// <summary>A one-letter last name with no first name gives that single letter.</summary>
    [Fact]
    public void Generate_OneLetterLastName_UsesThatLetter() =>
        Assert.Equal("X", InstructorInitials.Generate("", "x", None()));

    /// <summary>With neither name the fallback is XX.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("  ", "  ")]
    public void Generate_NoName_IsXx(string? first, string? last) =>
        Assert.Equal("XX", InstructorInitials.Generate(first, last, None()));

    /// <summary>A first name with no last name gives XX, matching the Dev Tools import it was ported from.</summary>
    [Fact]
    public void Generate_FirstNameOnly_IsXx() =>
        Assert.Equal("XX", InstructorInitials.Generate("Jane", "", None()));

    /// <summary>Collisions append 2, 3, ... until the initials are free.</summary>
    [Fact]
    public void Generate_Collisions_AppendNumbers()
    {
        var taken = new HashSet<string> { "JS" };
        Assert.Equal("JS2", InstructorInitials.Generate("Jane", "Smith", taken));

        taken.Add("JS2");
        Assert.Equal("JS3", InstructorInitials.Generate("Jane", "Smith", taken));
    }

    /// <summary>Taken initials match ignoring case, even when the caller's set is case-sensitive.</summary>
    [Fact]
    public void Generate_TakenIsComparedIgnoringCase()
    {
        var caseSensitive = new HashSet<string>(StringComparer.Ordinal) { "js", "Js2" };

        Assert.Equal("JS3", InstructorInitials.Generate("Jane", "Smith", caseSensitive));
    }

    /// <summary>Generate never adds to the caller's set; the caller does that between calls.</summary>
    [Fact]
    public void Generate_DoesNotModifyTaken()
    {
        var taken = new HashSet<string> { "JS" };

        InstructorInitials.Generate("Jane", "Smith", taken);
        InstructorInitials.Generate("Jane", "Smith", taken);

        Assert.Equal(new[] { "JS" }, taken);
    }

    /// <summary>When 2 to 99 are all used, a random four-character suffix is appended.</summary>
    [Fact]
    public void Generate_AllNumbersTaken_FallsBackToGuidSuffix()
    {
        var taken = new HashSet<string> { "JS" };
        for (var i = 2; i < 100; i++)
            taken.Add($"JS{i}");

        var result = InstructorInitials.Generate("Jane", "Smith", taken);

        Assert.StartsWith("JS", result);
        Assert.Equal(6, result.Length);
        Assert.DoesNotContain(result, taken, StringComparer.OrdinalIgnoreCase);
    }
}
