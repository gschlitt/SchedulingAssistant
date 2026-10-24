using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="NameMatch"/>: name and code normalisation, the comparers, and the
/// near-miss test used to warn about probable typos.
/// </summary>
public class NameMatchTests
{
    // ── Names ─────────────────────────────────────────────────────────────────

    /// <summary>Normalize trims, collapses whitespace runs to one space, and maps null to empty.</summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Lecture", "Lecture")]
    [InlineData("  Lecture  ", "Lecture")]
    [InlineData("Computer   Lab", "Computer Lab")]
    [InlineData("Computer\t\r\nLab", "Computer Lab")]
    [InlineData(" A  B   C ", "A B C")]
    public void Normalize_TrimsAndCollapses(string? input, string expected) =>
        Assert.Equal(expected, NameMatch.Normalize(input));

    /// <summary>Normalize leaves case alone.</summary>
    [Fact]
    public void Normalize_KeepsCase() => Assert.Equal("Comp Lab", NameMatch.Normalize(" Comp  Lab "));

    /// <summary>Equal ignores case, outer spacing and inner whitespace runs.</summary>
    [Theory]
    [InlineData("Lecture", "lecture", true)]
    [InlineData("Computer Lab", "  computer   LAB ", true)]
    [InlineData(null, "", true)]
    [InlineData(null, null, true)]
    [InlineData("Lecture", "Lectures", false)]
    [InlineData("Comp Lab", "CompLab", false)]
    public void Equal_IgnoresCaseAndSpacing(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, NameMatch.Equal(a, b));
        Assert.Equal(expected, NameMatch.Equal(b, a));
    }

    /// <summary>The comparer works as a dictionary key comparer for names.</summary>
    [Fact]
    public void Comparer_AsDictionaryKey_FindsEquivalentNames()
    {
        var map = new Dictionary<string, int>(NameMatch.Comparer) { ["Computer Lab"] = 7 };

        Assert.Equal(7, map["  computer   LAB "]);
        Assert.False(map.ContainsKey("Computer"));
        Assert.Throws<ArgumentException>(() => map.Add("COMPUTER LAB", 8));
    }

    /// <summary>The comparer's Equals, Compare and GetHashCode agree for equivalent names.</summary>
    [Fact]
    public void Comparer_EqualsCompareAndHash_Agree()
    {
        var c = NameMatch.Comparer;

        Assert.True(c.Equals("Comp  Lab", "comp lab"));
        Assert.Equal(0, c.Compare("Comp  Lab", "comp lab"));
        Assert.Equal(c.GetHashCode("Comp  Lab"), c.GetHashCode(" comp lab"));
        Assert.True(c.Compare("A", "B") < 0);
    }

    // ── Codes ─────────────────────────────────────────────────────────────────

    /// <summary>NormalizeCode removes all whitespace, not just the ends.</summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("CHEM 101", "CHEM101")]
    [InlineData("  chem  101 ", "chem101")]
    [InlineData("CHEM\t101", "CHEM101")]
    [InlineData("CHEM101", "CHEM101")]
    public void NormalizeCode_RemovesAllWhitespace(string? input, string expected) =>
        Assert.Equal(expected, NameMatch.NormalizeCode(input));

    /// <summary>CodeEqual and CodeComparer treat spacing and case as irrelevant.</summary>
    [Fact]
    public void CodeEqual_AndCodeComparer_IgnoreSpacingAndCase()
    {
        Assert.True(NameMatch.CodeEqual("CHEM 101", "chem101"));
        Assert.False(NameMatch.CodeEqual("CHEM 101", "CHEM 102"));

        var map = new Dictionary<string, string>(NameMatch.CodeComparer) { ["CHEM 101"] = "x" };
        Assert.Equal("x", map["chem101"]);
    }

    // ── Near misses ───────────────────────────────────────────────────────────

    /// <summary>Names whose letters and digits match, one containing the other (3+ letters), or a small typo apart are near misses.</summary>
    [Theory]
    [InlineData("Lab.", "Lab")]          // equal once punctuation is dropped
    [InlineData("Comp-Lab", "Comp Lab")] // punctuation vs space
    [InlineData("Lec", "Lecture")]       // containment, shorter has 3 letters
    [InlineData("Lecture", "Lec")]       // order does not matter
    [InlineData("Lab", "Computer Lab")]  // containment
    [InlineData("Writng", "Writing")]    // one letter missing
    [InlineData("Lectrue", "Lecture")]   // adjacent swap
    [InlineData("Seminar", "Semnar")]    // one deletion
    [InlineData("Tutorial", "Tutorals")] // distance 2
    [InlineData("Lab 1", "Lab  1.")]     // equal reduced form, same digits
    [InlineData("Biology Lab 2", "Biolgy Lab 2")] // same digits, typo in the letters
    public void IsNearMiss_TypoLikePairs_AreTrue(string a, string b)
    {
        Assert.True(NameMatch.IsNearMiss(a, b));
        Assert.True(NameMatch.IsNearMiss(b, a));
    }

    /// <summary>Unrelated names, short names, and names too far apart are not near misses.</summary>
    [Theory]
    [InlineData("LAB", "LAW")]           // under 4 letters, no containment
    [InlineData("A", "Lab")]             // one-letter tag is not tested by containment
    [InlineData("Lecture", "Seminar")]
    [InlineData("Lecture", "Tutorial")]
    [InlineData("Lab", "Lecture")]
    [InlineData("Lecture", "Lectern Hall Annex")]
    [InlineData("Ab", "Abcd")]           // shorter than 3: no containment; shorter than 4: no distance
    [InlineData("Theory", "Therapy Session")]
    [InlineData("Lab 1", "Lab 2")]       // different numbers are distinct things, never a typo
    [InlineData("Year 1", "Year 2")]
    [InlineData("Section 1", "Section 2")]
    [InlineData("Lab", "Lab 1")]         // digits "" vs "1"
    public void IsNearMiss_UnrelatedPairs_AreFalse(string a, string b)
    {
        Assert.False(NameMatch.IsNearMiss(a, b));
        Assert.False(NameMatch.IsNearMiss(b, a));
    }
}
