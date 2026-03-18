using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="CourseLevelParser.ParseLevel"/>.
///
/// Coverage:
///   - Pure numeric codes (3-digit, fewer, more)
///   - Alphanumeric codes with non-digit prefix and/or suffix
///   - Empty / whitespace / null inputs
///   - Boundary values (000, 099, 100, 199, 900, 999)
///   - Inputs that should not match (mixed digits, multiple digit groups)
/// </summary>
public class CourseLevelParserTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Pure numeric codes
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PureNumeric_3Digits_ReturnsHundredsBand()
    {
        Assert.Equal("100", CourseLevelParser.ParseLevel("101"));
    }

    [Fact]
    public void PureNumeric_348_Returns300()
    {
        // Explicit example from the spec.
        Assert.Equal("300", CourseLevelParser.ParseLevel("348"));
    }

    [Fact]
    public void PureNumeric_000_Returns0()
    {
        Assert.Equal("0", CourseLevelParser.ParseLevel("000"));
    }

    [Fact]
    public void PureNumeric_099_Returns0()
    {
        Assert.Equal("0", CourseLevelParser.ParseLevel("099"));
    }

    [Fact]
    public void PureNumeric_100_Returns100()
    {
        Assert.Equal("100", CourseLevelParser.ParseLevel("100"));
    }

    [Fact]
    public void PureNumeric_199_Returns100()
    {
        Assert.Equal("100", CourseLevelParser.ParseLevel("199"));
    }

    [Fact]
    public void PureNumeric_900_Returns900()
    {
        Assert.Equal("900", CourseLevelParser.ParseLevel("900"));
    }

    [Fact]
    public void PureNumeric_999_Returns900()
    {
        Assert.Equal("900", CourseLevelParser.ParseLevel("999"));
    }

    [Fact]
    public void PureNumeric_500_Returns500()
    {
        Assert.Equal("500", CourseLevelParser.ParseLevel("500"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Alphanumeric codes: digits + letter suffix
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DigitsThenLetters_111LAB_Returns100()
    {
        // Spec example.
        Assert.Equal("100", CourseLevelParser.ParseLevel("111LAB"));
    }

    [Fact]
    public void DigitsThenLetters_348W_Returns300()
    {
        Assert.Equal("300", CourseLevelParser.ParseLevel("348W"));
    }

    [Fact]
    public void DigitsThenLetters_200HONR_Returns200()
    {
        Assert.Equal("200", CourseLevelParser.ParseLevel("200HONR"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Alphanumeric codes: letter prefix + digits
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LettersThenDigits_LAB111_Returns100()
    {
        Assert.Equal("100", CourseLevelParser.ParseLevel("LAB111"));
    }

    [Fact]
    public void LettersThenDigits_A348_Returns300()
    {
        Assert.Equal("300", CourseLevelParser.ParseLevel("A348"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Alphanumeric codes: prefix + digits + suffix
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BothSides_AB111C_Returns100()
    {
        Assert.Equal("100", CourseLevelParser.ParseLevel("AB111C"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inputs that should NOT match → null
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TooFewDigits_2Digit_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("11"));
    }

    [Fact]
    public void TooFewDigits_1Digit_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("1"));
    }

    [Fact]
    public void TooManyDigits_4Consecutive_ReturnsNull()
    {
        // Four consecutive digits — the non-digit suffix fails on the 4th digit.
        Assert.Null(CourseLevelParser.ParseLevel("1111"));
    }

    [Fact]
    public void TooManyDigits_1111LAB_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("1111LAB"));
    }

    [Fact]
    public void InteriorDigitsInAffix_1A1_ReturnsNull()
    {
        // Non-digit prefix/suffix rule: "1A1" has a digit in the prefix position.
        Assert.Null(CourseLevelParser.ParseLevel("1A1"));
    }

    [Fact]
    public void DigitsInSuffix_111A1_ReturnsNull()
    {
        // "A1" suffix contains a digit — [A-Za-z]* will not consume it and the
        // match fails because the full string is not consumed.
        Assert.Null(CourseLevelParser.ParseLevel("111A1"));
    }

    [Fact]
    public void LettersOnly_AB_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("AB"));
    }

    [Fact]
    public void LettersOnly_LAB_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("LAB"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Null / empty / whitespace
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NullInput_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel(null));
    }

    [Fact]
    public void EmptyString_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel(""));
    }

    [Fact]
    public void WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(CourseLevelParser.ParseLevel("   "));
    }

    [Fact]
    public void LeadingTrailingWhitespace_Trimmed_Matches()
    {
        // Surrounding whitespace should be trimmed before matching.
        Assert.Equal("100", CourseLevelParser.ParseLevel(" 101 "));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AllLevels fixture
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllLevels_HasExactlyTenEntries()
    {
        Assert.Equal(10, CourseLevelParser.AllLevels.Count);
    }

    [Fact]
    public void AllLevels_StartsWithZero_EndsWithNineHundred()
    {
        Assert.Equal("0",   CourseLevelParser.AllLevels[0]);
        Assert.Equal("900", CourseLevelParser.AllLevels[9]);
    }
}
