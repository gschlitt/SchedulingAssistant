using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="MoveSuggestions"/>: the fewest sections to move to bring a full slate back (Greg, 2026-11-25). One move,
/// two separate one-move problems, tangled problems, sections kept in place, and the cases where it does not apply.
/// </summary>
public sealed class AccessMoveSuggestionsTests
{
    private static MoveSuggestions For(IReadOnlyList<Section> sections, IReadOnlySet<string>? kept = null, params Course[] courses) =>
        MoveSuggestions.For(AnalyzeAll(Simple(sections, courses)), kept);

    private static string[] Movers(MoveProblem problem) => Ids(problem.Options.Select(o => o.Section));

    [Fact]
    public void FullSlate_DoesNotApply()
    {
        var moves = For([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10))]);
        Assert.Same(MoveSuggestions.None, moves);
    }

    [Fact]
    public void CampusRuleToBlame_DoesNotApply()
    {
        var moves = For([Sec("A", "1", Lecture, At(0, 9, 10), "abb"), Sec("B", "1", Lecture, At(1, 9, 10), "chil")]);
        Assert.Same(MoveSuggestions.None, moves);
    }

    [Fact]
    public void OneConflict_EitherEndCanMove()
    {
        var moves = For([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        Assert.Equal(1, moves.MovesNeeded);
        var problem = Assert.Single(moves.Problems);
        Assert.Equal(["A 1", "B 1"], Movers(problem));
        Assert.Equal(["B 1"], Ids(problem.Options[0].ConflictsWith));
    }

    [Fact]
    public void OneSectionClashingWithTwo_IsOneMove_AndTheOthersAreNotSuggested()
    {
        // moving A or B alone leaves C's other conflict, so only C is suggested
        var moves = For([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10)),
            Sec("C", "1", Lecture, At([0, 1], 9, 10))]);
        Assert.Equal(1, moves.MovesNeeded);
        var option = Assert.Single(Assert.Single(moves.Problems).Options);
        Assert.Equal("C 1", option.Section.Id);
        Assert.Equal(["A 1", "B 1"], Ids(option.ConflictsWith));
    }

    [Fact]
    public void EveryLeftOutSection_IsAnOption_WithOnlyTheConflictsItsMoveWouldClear()
    {
        // MANU-Y4's shape: M's lecture sits inside lab L2; lab L1 clashes with X. Moving the lecture, either lab or X
        // brings a full slate back.
        var moves = For([
            Sec("M", "1", Lecture, At(4, 11, 12)),
            Sec("M", "L1", Lab, At(2, 14, 16)), Sec("M", "L2", Lab, At(4, 11, 13)),
            Sec("X", "1", Lecture, At(2, 15, 17))], null, Crs("M", Lecture, Lab));
        var problem = Assert.Single(moves.Problems);
        Assert.Equal(["M 1", "M L1", "M L2", "X 1"], Movers(problem));
        Assert.Equal(["M L2"], Ids(problem.Options[0].ConflictsWith));   // not L1: students would take L1 then
        Assert.Equal(["X 1"], Ids(problem.Options[1].ConflictsWith));
        Assert.Equal(["M 1"], Ids(problem.Options[2].ConflictsWith));
        Assert.Equal(["M L1"], Ids(problem.Options[3].ConflictsWith));
    }

    [Fact]
    public void TwoSeparateConflicts_AreTwoOneMoveProblems()
    {
        var moves = For([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)),
            Sec("C", "1", Lecture, At(1, 9, 10)), Sec("D", "1", Lecture, At(1, 9, 10))]);
        Assert.Equal(2, moves.MovesNeeded);
        Assert.Equal(2, moves.Problems.Count);
        Assert.Equal(["A 1", "B 1"], Movers(moves.Problems[0]));
        Assert.Equal(["C 1", "D 1"], Movers(moves.Problems[1]));
        Assert.Equal(["B 1"], Ids(moves.Problems[0].Options[0].ConflictsWith));
        Assert.Equal(["D 1"], Ids(moves.Problems[1].Options[0].ConflictsWith));
    }

    [Fact]
    public void TangledTwoMoves_SuggestNothing()
    {
        // three single sections at one time: any two of them must move, so no section is singled out
        var moves = For([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)), Sec("C", "1", Lecture, At(0, 9, 10))]);
        Assert.Equal(2, moves.MovesNeeded);
        Assert.Empty(moves.Problems);
    }

    [Fact]
    public void KeptInPlace_IsNeverSuggested_AndKeepingEveryOptionLeavesNothing()
    {
        var sections = new[] { Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10)) };
        Assert.Equal(["B 1"], Movers(Assert.Single(For(sections, new HashSet<string> { "A 1" }).Problems)));

        var both = For(sections, new HashSet<string> { "A 1", "B 1" });
        Assert.Empty(both.Problems);
        Assert.Null(both.MovesNeeded);
    }

    [Fact]
    public void KeepingTheOneMove_FallsThroughToTwoMoves()
    {
        // moving C is the one move; with C kept in place, A and B must both move (one separate problem each)
        var moves = For([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10)),
            Sec("C", "1", Lecture, At([0, 1], 9, 10))], new HashSet<string> { "C 1" });
        Assert.Equal(2, moves.MovesNeeded);
        Assert.Equal(2, moves.Problems.Count);
        Assert.Equal(["A 1"], Movers(moves.Problems[0]));
        Assert.Equal(["B 1"], Movers(moves.Problems[1]));
    }
}
