using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="SectionPrefixHelper"/>.
///
/// Tests are organized by method: MatchPrefix, FindNextAvailableCode,
/// and AdvanceSectionCode. Each group covers normal behaviour, boundary
/// conditions, and edge cases specific to the algorithm.
/// </summary>
public class SectionPrefixHelperTests
{
    // ─── Helper ───────────────────────────────────────────────────────────────

    /// <summary>Shorthand factory for a <see cref="SectionPrefix"/>.</summary>
    private static SectionPrefix P(string prefix, string? campusId = null) =>
        new() { Prefix = prefix, CampusId = campusId };

    // ═════════════════════════════════════════════════════════════════════════
    // MatchPrefix
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchPrefix_EmptyCode_ReturnsNull()
    {
        var result = SectionPrefixHelper.MatchPrefix("", new[] { P("AB") });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_EmptyPrefixList_ReturnsNull()
    {
        var result = SectionPrefixHelper.MatchPrefix("AB1", Array.Empty<SectionPrefix>());
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_NoPrefixMatches_ReturnsNull()
    {
        var result = SectionPrefixHelper.MatchPrefix("ZZ1", new[] { P("AB"), P("CH") });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_SingleMatch_ReturnsPrefix()
    {
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("AB1", new[] { ab });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_LongestMatchWins_ShortListedFirst()
    {
        // Both "A" and "AB" are candidates for "AB1", but "AB" is longer.
        // Verify that list order does not affect the outcome.
        var a  = P("A");
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("AB1", new[] { a, ab });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_LongestMatchWins_LongListedFirst()
    {
        var a  = P("A");
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("AB1", new[] { ab, a });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_ShortPrefixMatchesWhenLongPrefixDoesNot()
    {
        // "A1" — "A" matches (followed by digit '1'); "AB" does not match "A1".
        var a  = P("A");
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("A1", new[] { a, ab });
        Assert.Same(a, result);
    }

    [Fact]
    public void MatchPrefix_PrefixNotFollowedByDigit_ReturnsNull()
    {
        // "ABX": after the "AB" prefix the next character is 'X', not a digit.
        var result = SectionPrefixHelper.MatchPrefix("ABX", new[] { P("AB") });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_PrefixNotFollowedByDigit_SpecialChar_ReturnsNull()
    {
        // "AB.1": the character immediately after "AB" is '.', not a digit.
        var result = SectionPrefixHelper.MatchPrefix("AB.1", new[] { P("AB") });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_CodeEqualsPrefix_NoTrailingDigit_ReturnsNull()
    {
        // "AB" with no trailing digit — afterPrefix == sectionCode.Length, so no match.
        var result = SectionPrefixHelper.MatchPrefix("AB", new[] { P("AB") });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_CaseInsensitive_LowercaseCode_ReturnsMatch()
    {
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("ab1", new[] { ab });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_CaseInsensitive_MixedCase_ReturnsMatch()
    {
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("Ab1", new[] { ab });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_SpecialCharInPrefix_ReturnsMatch()
    {
        // Prefixes like "A#" are valid and must be matched correctly.
        var ahash = P("A#");
        var result = SectionPrefixHelper.MatchPrefix("A#1", new[] { ahash });
        Assert.Same(ahash, result);
    }

    [Fact]
    public void MatchPrefix_MultipleTrailingDigits_ReturnsMatch()
    {
        // The rule only requires the character *immediately* after the prefix to be a digit.
        var ab = P("AB");
        var result = SectionPrefixHelper.MatchPrefix("AB123", new[] { ab });
        Assert.Same(ab, result);
    }

    [Fact]
    public void MatchPrefix_EmptyPrefixEntryInList_IsSkipped()
    {
        // A SectionPrefix with an empty Prefix string should be silently skipped.
        var empty = P("");
        var result = SectionPrefixHelper.MatchPrefix("AB1", new[] { empty });
        Assert.Null(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FindNextAvailableCode
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FindNextAvailableCode_NoSlotsTaken_ReturnsPrefix1()
    {
        var result = SectionPrefixHelper.FindNextAvailableCode("AB", _ => false);
        Assert.Equal("AB1", result);
    }

    [Fact]
    public void FindNextAvailableCode_FirstSlotTaken_ReturnsPrefix2()
    {
        var result = SectionPrefixHelper.FindNextAvailableCode("AB", code => code == "AB1");
        Assert.Equal("AB2", result);
    }

    [Fact]
    public void FindNextAvailableCode_FillsGap_ReturnsLowestAvailable()
    {
        // AB1 and AB3 are taken; the gap at AB2 should be filled first.
        var taken = new HashSet<string> { "AB1", "AB3" };
        var result = SectionPrefixHelper.FindNextAvailableCode("AB", code => taken.Contains(code));
        Assert.Equal("AB2", result);
    }

    [Fact]
    public void FindNextAvailableCode_ConsecutiveRangeTaken_ReturnsNextAfterRange()
    {
        var taken = Enumerable.Range(1, 50).Select(n => $"AB{n}").ToHashSet();
        var result = SectionPrefixHelper.FindNextAvailableCode("AB", code => taken.Contains(code));
        Assert.Equal("AB51", result);
    }

    [Fact]
    public void FindNextAvailableCode_AllSlotsTaken_ReturnsNull()
    {
        // All 999 slots occupied — no slot available.
        var result = SectionPrefixHelper.FindNextAvailableCode("AB", _ => true);
        Assert.Null(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // AdvanceSectionCode
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AdvanceSectionCode_EmptySourceCode_ReturnsBothNull()
    {
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Null(code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_KnownPrefix_UsesGapFillNotSimpleIncrement()
    {
        // Source is AB3; AB1 is free. Gap-fill must return AB1, not AB4.
        var prefixes = new[] { P("AB") };
        var taken = new HashSet<string> { "AB2", "AB3" };
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "AB3", prefixes, c => taken.Contains(c));
        Assert.Equal("AB1", code);
    }

    [Fact]
    public void AdvanceSectionCode_KnownPrefix_ReturnsCampusId()
    {
        var prefixes = new[] { P("AB", campusId: "campus-xyz") };
        var (_, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "AB1", prefixes, _ => false);
        Assert.Equal("campus-xyz", campus);
    }

    [Fact]
    public void AdvanceSectionCode_KnownPrefix_NoCampus_ReturnsCampusNull()
    {
        var prefixes = new[] { P("AB", campusId: null) };
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "AB1", prefixes, _ => false);
        Assert.Equal("AB1", code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_KnownPrefix_AllSlotsTaken_ReturnsNullCodeButPreservesCampus()
    {
        // When the prefix sequence is exhausted, Code is null but CampusId is
        // still returned because it was resolved from the matched prefix.
        var prefixes = new[] { P("AB", campusId: "campus-xyz") };
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "AB1", prefixes, _ => true);
        Assert.Null(code);
        Assert.Equal("campus-xyz", campus);
    }

    [Fact]
    public void AdvanceSectionCode_UnknownPrefix_FallbackIncrementsTrailingNumber()
    {
        // "XY1" — no configured prefix, so trailing integer advances by one.
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "XY1", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Equal("XY2", code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_UnknownPrefix_MultiDigitTrailingNumber_Increments()
    {
        // "XY10" → "XY11"
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "XY10", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Equal("XY11", code);
    }

    [Fact]
    public void AdvanceSectionCode_UnknownPrefix_FallbackCandidateTaken_ReturnsNullCode()
    {
        // Fallback would produce "XY2" but it is already taken — no result.
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "XY1", Array.Empty<SectionPrefix>(), c => c == "XY2");
        Assert.Null(code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_NoTrailingDigits_ReturnsNullCode()
    {
        // "ABC" has no trailing digits — the fallback regex fails to match.
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "ABC", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Null(code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_UnknownPrefixAllDigits_FallbackIncrement()
    {
        // "123" — no alpha prefix, fallback strips "" and advances "123" → "124".
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "123", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Equal("124", code);
    }
}
