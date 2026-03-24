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
    private static SectionPrefix P(
        string prefix,
        string? campusId = null,
        DesignatorType designatorType = DesignatorType.Number) =>
        new() { Prefix = prefix, CampusId = campusId, DesignatorType = designatorType };

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

    // ── Letter designator ─────────────────────────────────────────────────

    [Fact]
    public void MatchPrefix_LetterDesignator_MatchesWhenSuffixIsLetter()
    {
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("TUTA", new[] { tut });
        Assert.Same(tut, result);
    }

    [Fact]
    public void MatchPrefix_LetterDesignator_DoesNotMatchWhenSuffixIsDigit()
    {
        // A Letter-type prefix should NOT match a code whose suffix is a number.
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("TUT1", new[] { tut });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_NumberDesignator_DoesNotMatchWhenSuffixIsLetter()
    {
        // A Number-type prefix should NOT match a code whose suffix is a letter.
        var ab = P("AB", designatorType: DesignatorType.Number);
        var result = SectionPrefixHelper.MatchPrefix("ABA", new[] { ab });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_SpecialCharPrefix_LetterDesignator_Matches()
    {
        // "A#" with Letter designator should match "A#C".
        var ahash = P("A#", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("A#C", new[] { ahash });
        Assert.Same(ahash, result);
    }

    [Fact]
    public void MatchPrefix_LetterDesignator_LowercaseSuffix_StillMatches()
    {
        // char.IsLetter is case-insensitive, so "tuta" must still resolve to the "TUT" prefix.
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("tuta", new[] { tut });
        Assert.Same(tut, result);
    }

    [Fact]
    public void MatchPrefix_LetterDesignator_CodeEqualsPrefix_NoSuffix_ReturnsNull()
    {
        // "TUT" with no designator character — afterPrefix == sectionCode.Length, so no match.
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("TUT", new[] { tut });
        Assert.Null(result);
    }

    [Fact]
    public void MatchPrefix_MixedList_LetterAndNumberPrefixes_EachMatchesOwnSuffixType()
    {
        // "AB" is a Number prefix; "TUT" is a Letter prefix.
        // Neither should match the other's code style.
        var ab  = P("AB",  designatorType: DesignatorType.Number);
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var prefixes = new[] { ab, tut };

        Assert.Same(ab,  SectionPrefixHelper.MatchPrefix("AB1",   prefixes));
        Assert.Same(tut, SectionPrefixHelper.MatchPrefix("TUTA",  prefixes));
        Assert.Null(     SectionPrefixHelper.MatchPrefix("ABA",   prefixes)); // AB is Number, won't match letter suffix
        Assert.Null(     SectionPrefixHelper.MatchPrefix("TUT1",  prefixes)); // TUT is Letter, won't match digit suffix
    }

    [Fact]
    public void MatchPrefix_LetterDesignator_LongestMatchWins()
    {
        // Both "T" (Number) and "TUT" (Letter) could share a leading substring.
        // "TUTA" — "T" is Number so 'U' (not a digit) is no match;
        //          "TUT" is Letter so 'A' (a letter) is a match.
        var t   = P("T",   designatorType: DesignatorType.Number);
        var tut = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.MatchPrefix("TUTA", new[] { t, tut });
        Assert.Same(tut, result);
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

    // ── Letter designator ─────────────────────────────────────────────────

    [Fact]
    public void FindNextAvailableCode_Letter_NoSlotsTaken_ReturnsA()
    {
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", _ => false, DesignatorType.Letter);
        Assert.Equal("TUTA", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_FirstSlotTaken_ReturnsB()
    {
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", code => code == "TUTA", DesignatorType.Letter);
        Assert.Equal("TUTB", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_FillsGap_ReturnsLowestAvailableLetter()
    {
        // A and C are taken; the gap at B should be filled first.
        var taken = new HashSet<string> { "TUTA", "TUTC" };
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", code => taken.Contains(code), DesignatorType.Letter);
        Assert.Equal("TUTB", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_AllSlotsTaken_ReturnsNull()
    {
        // All 18,278 (A–Z + AA–ZZ + AAA–ZZZ) slots occupied — no slot available.
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", _ => true, DesignatorType.Letter);
        Assert.Null(result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_AllSingleLettersTaken_ReturnsDoubleLetterCode()
    {
        // Once A–Z are exhausted the sequence must continue with AA, AB, …
        var taken = new HashSet<string>("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => $"TUT{c}"));
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", code => taken.Contains(code), DesignatorType.Letter);
        Assert.Equal("TUTAA", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_DoubleLetterGap_FillsGapBeforeAdvancing()
    {
        // AA and AC are taken; AB is the gap — must be returned before AD.
        var taken = new HashSet<string> { "TUTAA", "TUTAC" };
        // Assume all single-letter slots are also taken so we enter double-letter territory.
        var allSingle = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => $"TUT{c}");
        taken.UnionWith(allSingle);

        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", code => taken.Contains(code), DesignatorType.Letter);
        Assert.Equal("TUTAB", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_SpecialCharPrefix_ReturnsLetterSuffix()
    {
        // "A#" is a valid prefix; should generate "A#A", not "A#1".
        var result = SectionPrefixHelper.FindNextAvailableCode("A#", _ => false, DesignatorType.Letter);
        Assert.Equal("A#A", result);
    }

    [Fact]
    public void FindNextAvailableCode_Letter_GeneratesUppercase()
    {
        // The designator portion of generated codes must be uppercase.
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", _ => false, DesignatorType.Letter);
        Assert.Equal("TUTA", result);
        // Every character in the result that is a letter must be uppercase.
        Assert.True(result!.All(c => !char.IsLetter(c) || char.IsUpper(c)));
    }

    [Fact]
    public void FindNextAvailableCode_Letter_DoubleLetterResult_IsUppercase()
    {
        // Multi-character designators must also be uppercase.
        var allSingle = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => $"TUT{c}").ToHashSet();
        var result = SectionPrefixHelper.FindNextAvailableCode("TUT", code => allSingle.Contains(code), DesignatorType.Letter);
        Assert.Equal("TUTAA", result);
        Assert.True(result!.All(c => !char.IsLetter(c) || char.IsUpper(c)));
    }

    // ── Bug regression: FindNextAvailableCode defaults to Number ──────────
    // SectionEditViewModel.OnSelectedPrefixOptionChanged calls FindNextAvailableCode
    // without passing DesignatorType.  For a Letter-designated prefix this generates
    // a numeric code ("TUT1") instead of a letter code ("TUTA").
    // The following test documents the CORRECT call pattern (with DesignatorType passed)
    // and the WRONG call pattern (without it), so the discrepancy is visible.

    [Fact]
    public void FindNextAvailableCode_WithDesignatorType_LetterPrefixGivesLetterCode()
    {
        // CORRECT: caller supplies the prefix's DesignatorType.
        var prefix = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.FindNextAvailableCode(prefix.Prefix, _ => false, prefix.DesignatorType);
        Assert.Equal("TUTA", result);
    }

    [Fact]
    public void FindNextAvailableCode_WithoutDesignatorType_LetterPrefixGivesNumericCode()
    {
        // WRONG call pattern (no DesignatorType): replicates the bug in
        // SectionEditViewModel.OnSelectedPrefixOptionChanged where the prefix's
        // DesignatorType is not forwarded.  The default is Number, so the result
        // is "TUT1" rather than "TUTA".
        var prefix = P("TUT", designatorType: DesignatorType.Letter);
        var result = SectionPrefixHelper.FindNextAvailableCode(prefix.Prefix, _ => false);
        Assert.Equal("TUT1", result); // numeric — the bug in action
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

    // ── Letter designator ─────────────────────────────────────────────────

    [Fact]
    public void AdvanceSectionCode_LetterPrefix_GapFill_ReturnsFirstAvailableLetter()
    {
        // Source is "TUTB"; TUTA is free — gap-fill returns TUTA not TUTC.
        var prefixes = new[] { P("TUT", designatorType: DesignatorType.Letter) };
        var taken = new HashSet<string> { "TUTB" };
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "TUTB", prefixes, c => taken.Contains(c));
        Assert.Equal("TUTA", code);
    }

    [Fact]
    public void AdvanceSectionCode_LetterPrefix_ReturnsCampusId()
    {
        var prefixes = new[] { P("TUT", campusId: "campus-xyz", designatorType: DesignatorType.Letter) };
        var (_, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "TUTA", prefixes, _ => false);
        Assert.Equal("campus-xyz", campus);
    }

    [Fact]
    public void AdvanceSectionCode_LetterPrefix_AllSlotsTaken_ReturnsNullCode()
    {
        var prefixes = new[] { P("TUT", campusId: "campus-xyz", designatorType: DesignatorType.Letter) };
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "TUTA", prefixes, _ => true);
        Assert.Null(code);
        Assert.Equal("campus-xyz", campus);
    }

    [Fact]
    public void AdvanceSectionCode_LetterPrefix_SpecialChar_GapFill()
    {
        // "A#" Letter prefix: "A#C" source, "A#A" and "A#B" free — returns "A#A".
        var prefixes = new[] { P("A#", designatorType: DesignatorType.Letter) };
        var taken = new HashSet<string> { "A#C", "A#D" };
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "A#C", prefixes, c => taken.Contains(c));
        Assert.Equal("A#A", code);
    }

    [Fact]
    public void AdvanceSectionCode_LetterSuffixCode_NoPrefixConfigured_FallbackReturnsNull()
    {
        // "TUTA" has no trailing digit and no configured prefix — fallback cannot advance it.
        var (code, campus) = SectionPrefixHelper.AdvanceSectionCode(
            "TUTA", Array.Empty<SectionPrefix>(), _ => false);
        Assert.Null(code);
        Assert.Null(campus);
    }

    [Fact]
    public void AdvanceSectionCode_LetterSuffixCode_PrefixListHasOnlyNumberPrefixes_FallbackReturnsNull()
    {
        // "TUTA" — the list has "TUT" but as a Number prefix, so MatchPrefix won't match
        // ("A" is not a digit).  Fallback looks for trailing digits and finds none → null.
        var prefixes = new[] { P("TUT", designatorType: DesignatorType.Number) };
        var (code, _) = SectionPrefixHelper.AdvanceSectionCode(
            "TUTA", prefixes, _ => false);
        Assert.Null(code);
    }
}
