using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>What a Check all courses problem is about.</summary>
public enum CheckProblemKind
{
    /// <summary>The course has no slate on its own.</summary>
    NoSlate,
    /// <summary>A section of the course is stranded: no student taking the course can use it.</summary>
    StrandedSection,
}

/// <summary>
/// One problem found by Check all courses. For <see cref="CheckProblemKind.NoSlate"/>, the reason is the
/// self-fail reason, else the campus reason, else neither (no set of the course's sections fits together). For
/// <see cref="CheckProblemKind.StrandedSection"/>, the section and why it is stranded.
/// </summary>
/// <param name="CourseId">The course.</param>
/// <param name="Kind">What kind of problem.</param>
/// <param name="Section">The stranded section; null for <see cref="CheckProblemKind.NoSlate"/>.</param>
/// <param name="StrandedReason">Why the section is stranded; null for <see cref="CheckProblemKind.NoSlate"/>.</param>
/// <param name="SelfFail">For no slate: why the course cannot be taken alone, when a section is to blame or no section fits.</param>
/// <param name="CampusReason">For no slate and no self-fail: the one-campus rule alone is to blame.</param>
public sealed record CheckProblem(
    string CourseId,
    CheckProblemKind Kind,
    Section? Section,
    StrandedReason? StrandedReason,
    SelfFailReason? SelfFail,
    CampusReason? CampusReason);

/// <summary>What a quiet finding of Check all courses is about.</summary>
public enum QuietFindingKind
{
    /// <summary>A component of the course has no sections this term (rule 10); the course is not judged.</summary>
    MissingComponent,
    /// <summary>A section of a multi-component course fills no component because it has no type or a non-component type (rule 8).</summary>
    SectionNotCounted,
    /// <summary>Sections of a component type their course does not list (rule 8a).</summary>
    ComponentMismatch,
}

/// <summary>A "worth a look" finding that is not a problem.</summary>
/// <param name="CourseId">The course.</param>
/// <param name="Kind">What kind of finding.</param>
/// <param name="ComponentTypeId">The missing component or mismatched type; null for not-counted sections.</param>
/// <param name="Sections">The sections involved; empty for a missing component.</param>
public sealed record QuietFinding(
    string CourseId,
    QuietFindingKind Kind,
    string? ComponentTypeId,
    IReadOnlyList<Section> Sections);

/// <summary>The results of Check all courses. Never stored.</summary>
/// <param name="OfferedCourseCount">Courses with at least one section in the semester view.</param>
/// <param name="MultiComponentCourseCount">Of those, courses with two or more components (the only ones that can have problems).</param>
/// <param name="Problems">Courses with no slate, or stranded sections, in course order.</param>
/// <param name="Quiet">Missing components, sections not counted and mismatches, in course order.</param>
public sealed record CheckResult(
    int OfferedCourseCount,
    int MultiComponentCourseCount,
    IReadOnlyList<CheckProblem> Problems,
    IReadOnlyList<QuietFinding> Quiet);

/// <summary>
/// Check all courses (rules 39-41, round 9; a port of the wireframe's <c>runCheck</c>): each offered course of the
/// semester analysed on its own, with no pins and the one-campus rule. Callers decide whether to run it (before
/// the access setup it is not offered); the engine just reads the inputs it is given. Pure and static.
/// </summary>
public static class CheckAllCourses
{
    /// <summary>
    /// Runs the check. Mismatches (rule 8a) are reported quietly for every course. For a course with two or more
    /// components: missing components and sections not counted are reported quietly, and a course with a missing
    /// component is not judged further (its components may be out of date); otherwise a course with no slate is a
    /// problem, or each of its stranded sections is.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <returns>The problems and quiet findings.</returns>
    public static CheckResult Run(AccessInputs inputs)
    {
        var offered = inputs.SectionsByCourse.Keys.OrderBy(c => c, inputs.CourseOrder).ToList();
        var problems = new List<CheckProblem>();
        var quiet = new List<QuietFinding>();
        int multiCount = 0;

        foreach (var courseId in offered)
        {
            foreach (var mismatch in ComponentGrouping.Mismatches(inputs, [courseId]))
                quiet.Add(new QuietFinding(courseId, QuietFindingKind.ComponentMismatch, mismatch.SectionTypeId, mismatch.Sections));
            if (inputs.ComponentsOf(courseId).Count < 2) continue;
            multiCount++;

            var analysis = AccessAnalyzer.Analyze(inputs, inputs.SectionsByCourse[courseId], false, null, null);
            foreach (var missing in analysis.MissingComponents)
                quiet.Add(new QuietFinding(courseId, QuietFindingKind.MissingComponent, missing.ComponentTypeId, []));
            // A section with a component type its course doesn't list is a mismatch (reported above), not "not counted"
            var noType = analysis.NotCountedSections
                .Where(s => s.SectionTypeId is null || !inputs.IsComponentType(s.SectionTypeId)).ToList();
            foreach (var section in noType)
                quiet.Add(new QuietFinding(courseId, QuietFindingKind.SectionNotCounted, null, [section]));
            if (analysis.MissingComponents.Count > 0) continue;

            if (!analysis.HasSlate)
            {
                problems.Add(new CheckProblem(courseId, CheckProblemKind.NoSlate, null, null,
                    analysis.SelfFailCourses.FirstOrDefault()?.Reason, analysis.CampusReason));
            }
            else
            {
                foreach (var section in analysis.StrandedSections)
                    problems.Add(new CheckProblem(courseId, CheckProblemKind.StrandedSection, section,
                        analysis.StrandedReasonFor(section), null, null));
            }
        }
        return new CheckResult(offered.Count, multiCount, problems, quiet);
    }
}
