using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class CsvImportMatcherTests
{
    private readonly CsvImportMatcher _matcher = new();

    private static Instructor MakeInstructor(string lastName, string firstName) =>
        new() { LastName = lastName, FirstName = firstName };

    [Fact]
    public void MatchInstructor_ExactLastAndFirstName_ReturnsExact()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor> { MakeInstructor("Smith", "John") });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Smith", FirstName = "John" }, index);

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Equal("John", result.Resolved!.FirstName);
    }

    [Fact]
    public void MatchInstructor_LastOnly_SingleCandidate_ReturnsExact()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor> { MakeInstructor("MacDonald", "Alice") });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "MacDonald", FirstName = "" }, index);

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Equal("Alice", result.Resolved!.FirstName);
    }

    [Theory]
    [InlineData("J.")]
    [InlineData("J")]
    public void MatchInstructor_InitialCompatibleFirstName_ReturnsExact(string csvFirstName)
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor> { MakeInstructor("Chen", "John") });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Chen", FirstName = csvFirstName }, index);

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Equal("John", result.Resolved!.FirstName);
    }

    [Fact]
    public void MatchInstructor_TwoCandidatesSameLastName_AmbiguousWhenFirstNameBlank()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor>
        {
            MakeInstructor("Smith", "John"),
            MakeInstructor("Smith", "Jane"),
        });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Smith", FirstName = "" }, index);

        Assert.Equal(MatchStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void MatchInstructor_TwoCandidatesSameLastName_AmbiguousWhenInitialMatchesBoth()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor>
        {
            MakeInstructor("Smith", "John"),
            MakeInstructor("Smith", "Jane"),
        });

        // "J." is initial-compatible with both John and Jane — can't disambiguate.
        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Smith", FirstName = "J." }, index);

        Assert.Equal(MatchStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void MatchInstructor_HonorificStripped_StillMatchesExact()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor> { MakeInstructor("Smith", "John") });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Smith", FirstName = "Dr. John" }, index);

        Assert.Equal(MatchStatus.Exact, result.Status);
    }

    [Fact]
    public void MatchInstructor_NoLastNameMatch_ReturnsUnmatched()
    {
        var index = _matcher.BuildInstructorIndex(new List<Instructor> { MakeInstructor("Smith", "John") });

        var result = _matcher.MatchInstructor(new InstructorRow { LastName = "Nguyen", FirstName = "Anh" }, index);

        Assert.Equal(MatchStatus.Unmatched, result.Status);
    }

    [Fact]
    public void MatchCourse_ExactCaseInsensitiveWhitespaceNormalized_ReturnsExact()
    {
        var course = new Course { CalendarCode = "CHEM 101" };
        var index = new Dictionary<string, Course>(StringComparer.OrdinalIgnoreCase) { ["CHEM 101"] = course };

        var result = _matcher.MatchCourse("chem  101", index);

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Same(course, result.Resolved);
    }

    [Fact]
    public void MatchCourse_NoMatch_ReturnsUnmatched()
    {
        var index = new Dictionary<string, Course>(StringComparer.OrdinalIgnoreCase);

        var result = _matcher.MatchCourse("BIOL 201", index);

        Assert.Equal(MatchStatus.Unmatched, result.Status);
    }

    [Fact]
    public void MatchCourse_BlankCode_ReturnsSkipped()
    {
        var index = new Dictionary<string, Course>(StringComparer.OrdinalIgnoreCase);

        var result = _matcher.MatchCourse("", index);

        Assert.Equal(MatchStatus.Skipped, result.Status);
    }

    [Fact]
    public void MatchSubject_MatchedByCalendarAbbreviation_ReturnsExact()
    {
        var subject = new Subject { CalendarAbbreviation = "CHEM" };
        var index = new Dictionary<string, Subject>(StringComparer.OrdinalIgnoreCase) { ["CHEM"] = subject };

        var result = _matcher.MatchSubject("chem", index);

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Same(subject, result.Resolved);
    }

    [Fact]
    public void MatchEnvironmentValue_ExactCaseInsensitive_ReturnsExact()
    {
        var target = new EnvironmentTarget { Id = "1", DisplayName = "Lecture" };

        var result = _matcher.MatchEnvironmentValue("lecture", new List<EnvironmentTarget> { target });

        Assert.Equal(MatchStatus.Exact, result.Status);
        Assert.Same(target, result.Resolved);
    }

    [Fact]
    public void MatchEnvironmentValue_Unmatched_ReturnsUnmatched()
    {
        var target = new EnvironmentTarget { Id = "1", DisplayName = "Lecture" };

        var result = _matcher.MatchEnvironmentValue("Laboratory", new List<EnvironmentTarget> { target });

        Assert.Equal(MatchStatus.Unmatched, result.Status);
    }

    [Fact]
    public void MatchEnvironmentValue_RoomCompositeDisplayName_MatchesLikeAnyOtherValue()
    {
        var target = new EnvironmentTarget { Id = "1", DisplayName = "Science 101" };

        var result = _matcher.MatchEnvironmentValue("science 101", new List<EnvironmentTarget> { target });

        Assert.Equal(MatchStatus.Exact, result.Status);
    }

    [Fact]
    public void MatchEnvironmentValue_BlankValue_ReturnsSkipped()
    {
        var result = _matcher.MatchEnvironmentValue("", new List<EnvironmentTarget>());

        Assert.Equal(MatchStatus.Skipped, result.Status);
    }
}
