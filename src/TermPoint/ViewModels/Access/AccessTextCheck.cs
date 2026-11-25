using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The wording of Check all courses (rules 39 to 41, round 9): a port of the r9 wireframe's <c>runCheck</c> and
/// <c>checkView</c>, with "access checks" now "access watches" in user text. Approved wording from round 9 unless
/// marked DRAFT.
/// </summary>
public static partial class AccessText
{
    /// <summary>The page's title, and the button that starts the check.</summary>
    // approved wording (round 9)
    public const string CheckAllCoursesText = "Check all courses";

    /// <summary>The button that runs the check again.</summary>
    // approved wording (round 9)
    public const string CheckAgainText = "Check again";

    /// <summary>The line every results screen ends with (Q28: lecture and lab as separate courses).</summary>
    // DRAFT wording (round 9)
    public const string SeparateLabsText =
        "Labs set up as separate courses (such as BIOL 111 and BIOL 111LAB) are checked together only by a watch: tag both courses and watch the tag.";

    /// <summary>Why the check cannot run before the access setup when this computer cannot save.</summary>
    // DRAFT wording (round 9, with "access checks" now "access watches")
    public const string CheckNotSetUpReadOnlyText =
        "Access watches aren't set up for this database yet. Someone who can edit it needs to set them up before courses can be checked.";

    /// <summary>What the page says before the access setup when this computer can save (the Set up button follows).</summary>
    // approved wording (round 9, with "access checks" now "access watches")
    public const string CheckNotSetUpText = "Access watches aren't set up yet. Set them up to check courses.";

    /// <summary>The Set up button of <see cref="CheckNotSetUpText"/>.</summary>
    // approved wording (round 9)
    public const string CheckSetUpButtonText = "Set up";

    /// <summary>The note above results that were made before the last change.</summary>
    // approved wording (round 9)
    public const string CheckOutOfDateText = "These results are from before the last change. Click Check again to update them.";

    /// <summary>The line when the setup said no course takes more than one section.</summary>
    // approved wording (round 9, with "access checks" now "access watches")
    public const string CheckNothingNoComponentsText =
        "Nothing to check: when access watches were set up, each course was said to take just one section.";

    /// <summary>The footer line that follows <see cref="CheckNothingNoComponentsText"/>.</summary>
    // approved wording (round 9)
    public const string CheckChangeThatText =
        "To change that, mark a section type as a component under Scheduling Environment → Section Type.";

    /// <summary>The line when no course of the semester has more than one component.</summary>
    // approved wording (round 9)
    public const string CheckNothingNoMultiText = "Nothing to check: no course this term has more than one component.";

    /// <summary>The heading of the quiet findings.</summary>
    // approved wording (round 9)
    public const string CheckWorthALookText = "Worth a look";

    /// <summary>The button of a quiet finding about a course.</summary>
    // approved wording (round 9)
    public const string CheckOpenInCoursesText = "Open in Courses";

    /// <summary>The button of a quiet finding about a section.</summary>
    // approved wording (round 9)
    public const string CheckShowText = "Show";

    /// <summary>The tooltip of a problem line.</summary>
    /// <param name="courseCode">The course's code.</param>
    /// <returns>The tooltip.</returns>
    // approved wording (round 9)
    public static string CheckProblemTip(string courseCode) => $"Show {courseCode} on the grid";

    /// <summary>The headline over the problem lines.</summary>
    /// <param name="problemCourses">How many courses have a problem.</param>
    /// <returns>"1 course needs a look:" or "3 courses need a look:".</returns>
    // approved wording (round 9)
    public static string CheckProblemsHeadline(int problemCourses) =>
        problemCourses == 1 ? "1 course needs a look:" : $"{problemCourses} courses need a look:";

    /// <summary>The headline when nothing is wrong.</summary>
    /// <param name="multiComponentCourses">How many courses with more than one component were checked.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 9)
    public static string CheckNoProblemsHeadline(int multiComponentCourses) =>
        $"No problems in {Plural(multiComponentCourses, "course", "courses")} with more than one component.";

    /// <summary>The footer of a results screen.</summary>
    /// <param name="multiComponentCourses">How many courses with more than one component were checked.</param>
    /// <param name="offeredCourses">How many courses the semester offers.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 9)
    public static string CheckFooter(int multiComponentCourses, int offeredCourses) =>
        $"Checked each of the {Plural(multiComponentCourses, "course", "courses")} with more than one component on its own ({offeredCourses} courses offered this term).";

    /// <summary>
    /// A problem of Check all courses as a line: a course that can't be taken, or a section no student can use.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="problem">The problem.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 9)
    public static string CheckProblemLine(AccessInputs inputs, CheckProblem problem, Func<string?, string> campusName)
    {
        string c = inputs.CourseCode(problem.CourseId);
        if (problem.Kind == CheckProblemKind.StrandedSection && problem.Section is { } section && problem.StrandedReason is { } reason)
            return $"{SectionLabel(inputs, section)} is stranded: {StrandedWhy(inputs, section, reason, campusName, false)} No student taking {c} can use it.";

        string why;
        if (problem.SelfFail is { } selfFail)
        {
            // the self-fail wording reads "BMEG 230's labs ..."; a line already about the course says "its labs ..."
            var analysis = AccessAnalyzer.Analyze(inputs, inputs.SectionsByCourse[problem.CourseId], false, null, null);
            why = SelfFailWhy(inputs, analysis, new SelfFailCourse(problem.CourseId, selfFail), campusName);
        }
        else if (problem.CampusReason is { } campus) why = CampusWhy(inputs, campus, false, campusName);
        else why = "no set of its sections fits together";
        int at = why.IndexOf(c + "'s ", StringComparison.Ordinal);
        if (at >= 0) why = why[..at] + "its " + why[(at + c.Length + 3)..];
        return $"{c} can't be taken: {why}.";
    }

    /// <summary>A quiet finding of Check all courses as a line.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="finding">The finding.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 9)
    public static string CheckQuietLine(AccessInputs inputs, QuietFinding finding)
    {
        string c = inputs.CourseCode(finding.CourseId);
        string TypeName() => inputs.SectionTypes.FirstOrDefault(t => t.Id == finding.ComponentTypeId)?.Name ?? "component";
        switch (finding.Kind)
        {
            case QuietFindingKind.MissingComponent:
                return $"{c} has no {TypeName()} sections this term, but {TypeName()} is one of its components.";
            case QuietFindingKind.SectionNotCounted:
                return NotCountedFlagText(inputs, finding.Sections[0]);
            default:
                return MismatchSentence(c, TypeName(), finding.Sections.Select(s => s.SectionCode).ToList());
        }
    }
}
