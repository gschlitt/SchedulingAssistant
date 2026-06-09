using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="SectionCodeGenerator"/> — the pure logic that powers the
/// Copy operation's "next section code" derivation and the pattern editor's preview.
/// No database or UI: the "is this code taken?" check is supplied as a simple predicate.
/// </summary>
public class SectionCodeGeneratorTests
{
    // ── Builders ──────────────────────────────────────────────────────────────

    private static SectionCodePattern Numeric(
        string prefix = "", string suffix = "", int first = 1, int inc = 1, int pad = 0) =>
        new()
        {
            UseLetters  = false,
            Prefix      = prefix,
            Suffix      = suffix,
            FirstNumber = first,
            Increment   = inc,
            PadWidth    = pad
        };

    private static SectionCodePattern Letters(char first = 'A', string prefix = "", string suffix = "") =>
        new() { UseLetters = true, FirstLetter = first, Prefix = prefix, Suffix = suffix };

    /// <summary>Predicate that treats the given codes (case-sensitive) as already taken.</summary>
    private static Func<string, bool> Taken(params string[] codes)
    {
        var set = new HashSet<string>(codes, StringComparer.Ordinal);
        return set.Contains;
    }

    /// <summary>Predicate where nothing is taken.</summary>
    private static readonly Func<string, bool> NoneTaken = _ => false;

    // ══════════════════════════════════════════════════════════════════════════
    // GetNextCode — numeric sequences
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetNextCode_Numeric_NothingTaken_ReturnsFirst()
    {
        Assert.Equal("D1", SectionCodeGenerator.GetNextCode(Numeric(prefix: "D"), NoneTaken));
    }

    [Fact]
    public void GetNextCode_Numeric_SkipsTakenCodes()
    {
        Assert.Equal("D3", SectionCodeGenerator.GetNextCode(Numeric(prefix: "D"), Taken("D1", "D2")));
    }

    [Fact]
    public void GetNextCode_Numeric_HonorsIncrement()
    {
        // First is D1; with D1 taken the next step (increment 10) is D11.
        Assert.Equal("D11", SectionCodeGenerator.GetNextCode(Numeric(prefix: "D", inc: 10), Taken("D1")));
    }

    [Fact]
    public void GetNextCode_Numeric_HonorsPadWidth()
    {
        Assert.Equal("D001", SectionCodeGenerator.GetNextCode(Numeric(prefix: "D", pad: 3), NoneTaken));
    }

    [Fact]
    public void GetNextCode_Numeric_HonorsSuffix()
    {
        Assert.Equal("A1-L", SectionCodeGenerator.GetNextCode(Numeric(prefix: "A", suffix: "-L"), NoneTaken));
    }

    [Fact]
    public void GetNextCode_Numeric_HonorsFirstNumber()
    {
        Assert.Equal("D100", SectionCodeGenerator.GetNextCode(Numeric(prefix: "D", first: 100), NoneTaken));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GetNextCode — letter sequences (incl. rollover past Z)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetNextCode_Letters_NothingTaken_ReturnsFirstLetter()
    {
        Assert.Equal("A", SectionCodeGenerator.GetNextCode(Letters(), NoneTaken));
    }

    [Fact]
    public void GetNextCode_Letters_SkipsTakenLetters()
    {
        Assert.Equal("C", SectionCodeGenerator.GetNextCode(Letters(), Taken("A", "B")));
    }

    [Fact]
    public void GetNextCode_Letters_FirstLetterOffset()
    {
        Assert.Equal("D", SectionCodeGenerator.GetNextCode(Letters(first: 'D'), NoneTaken));
    }

    [Fact]
    public void GetNextCode_Letters_RollsOverPastZ_ToDoubleLetters()
    {
        // Every single letter A–Z is taken → the sequence must continue to "AA".
        var allSingleLettersTaken = (Func<string, bool>)(c => c.Length == 1);
        Assert.Equal("AA", SectionCodeGenerator.GetNextCode(Letters(), allSingleLettersTaken));
    }

    [Fact]
    public void GetNextCode_Letters_RollsOver_AAtoAB()
    {
        var takenThroughAA = (Func<string, bool>)(c => c.Length == 1 || c == "AA");
        Assert.Equal("AB", SectionCodeGenerator.GetNextCode(Letters(), takenThroughAA));
    }

    [Fact]
    public void GetNextCode_Letters_RollsOver_AZtoBA()
    {
        // A–Z and AA–AZ all taken → next is BA.
        var takenThroughAZ = (Func<string, bool>)(c =>
            c.Length == 1 || (c.Length == 2 && c[0] == 'A'));
        Assert.Equal("BA", SectionCodeGenerator.GetNextCode(Letters(), takenThroughAZ));
    }

    [Fact]
    public void GetNextCode_Letters_NotBoundedAt26_ReturnsMultiLetterCode()
    {
        // Before the fix, letters were capped at 26 and this returned null.
        var takenThroughAY = (Func<string, bool>)(c =>
            c.Length == 1 || (c.Length == 2 && c[0] == 'A' && c[1] < 'Z'));
        Assert.Equal("AZ", SectionCodeGenerator.GetNextCode(Letters(), takenThroughAY));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GetPreviewCodes
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetPreviewCodes_DefaultsToThree()
    {
        Assert.Equal(new[] { "D1", "D2", "D3" }, SectionCodeGenerator.GetPreviewCodes(Numeric(prefix: "D")));
    }

    [Fact]
    public void GetPreviewCodes_RespectsCount()
    {
        Assert.Equal(new[] { "D1", "D2" }, SectionCodeGenerator.GetPreviewCodes(Numeric(prefix: "D"), 2));
    }

    [Fact]
    public void GetPreviewCodes_Letters_ExtendsPastZ()
    {
        var preview = SectionCodeGenerator.GetPreviewCodes(Letters(), 28);

        Assert.Equal(28, preview.Count);
        Assert.Equal("Z",  preview[25]);   // 26th
        Assert.Equal("AA", preview[26]);   // 27th
        Assert.Equal("AB", preview[27]);   // 28th
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MatchesPattern — numeric
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchesPattern_Numeric_AcceptsProducibleCode()
    {
        Assert.True(SectionCodeGenerator.MatchesPattern("D3", Numeric(prefix: "D")));
    }

    [Fact]
    public void MatchesPattern_Numeric_RejectsWrongPrefix()
    {
        Assert.False(SectionCodeGenerator.MatchesPattern("X3", Numeric(prefix: "D")));
    }

    [Fact]
    public void MatchesPattern_Numeric_RejectsEmptyMiddle()
    {
        Assert.False(SectionCodeGenerator.MatchesPattern("D", Numeric(prefix: "D")));
    }

    [Fact]
    public void MatchesPattern_Numeric_RejectsValueBelowFirstNumber()
    {
        Assert.False(SectionCodeGenerator.MatchesPattern("D3", Numeric(prefix: "D", first: 5)));
    }

    [Theory]
    [InlineData("D11", true)]   // 1 + 10
    [InlineData("D2",  false)]  // not on the increment-10 grid
    public void MatchesPattern_Numeric_HonorsIncrement(string code, bool expected)
    {
        Assert.Equal(expected, SectionCodeGenerator.MatchesPattern(code, Numeric(prefix: "D", inc: 10)));
    }

    [Theory]
    [InlineData("D003", true)]
    [InlineData("D3",   false)]   // unpadded does not match a padded pattern
    public void MatchesPattern_Numeric_DistinguishesPadding(string code, bool expected)
    {
        Assert.Equal(expected, SectionCodeGenerator.MatchesPattern(code, Numeric(prefix: "D", pad: 3)));
    }

    [Theory]
    [InlineData("A1-L", true)]
    [InlineData("A1",   false)]   // missing suffix
    public void MatchesPattern_Numeric_HonorsSuffix(string code, bool expected)
    {
        Assert.Equal(expected, SectionCodeGenerator.MatchesPattern(code, Numeric(prefix: "A", suffix: "-L")));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MatchesPattern — letters (incl. multi-letter, the new behavior)
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("C",  true)]
    [InlineData("AA", true)]   // multi-letter now recognized
    [InlineData("AB", true)]
    [InlineData("a",  false)]  // lowercase (Ordinal/case-sensitive)
    [InlineData("A1", false)]  // non-letter in the middle
    public void MatchesPattern_Letters_FromA(string code, bool expected)
    {
        Assert.Equal(expected, SectionCodeGenerator.MatchesPattern(code, Letters()));
    }

    [Theory]
    [InlineData("D",  true)]
    [InlineData("B",  false)]  // below FirstLetter 'D'
    [InlineData("AA", true)]   // AA (27) is past D (4)
    public void MatchesPattern_Letters_HonorsFirstLetter(string code, bool expected)
    {
        Assert.Equal(expected, SectionCodeGenerator.MatchesPattern(code, Letters(first: 'D')));
    }
}
