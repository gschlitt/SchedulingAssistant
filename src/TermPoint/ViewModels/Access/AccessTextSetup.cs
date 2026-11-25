using System.Text.RegularExpressions;
using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The wording of the access setup (piece E): the wizard, the Access tab's invitation, the Component box in
/// Scheduling Environment, the Components column and Edit Course's group, and the Copy Semester and import lines.
/// A port of the r9 wireframe (designs/wireframes/access-panel-r9.html: <c>wizardHtml</c>, <c>setupInvite</c>,
/// <c>coursesFlyout</c>, <c>mismatches</c>, <c>copyTypesLine</c>, <c>seFlyout</c>). Wording marked "approved wording"
/// was approved by Greg for coding (he expects to fine-tune it once the feature runs).
/// </summary>
public static partial class AccessText
{
    // ── The wizard ────────────────────────────────────────────────────────────

    /// <summary>The flyout's title while the wizard is open (either mode).</summary>
    public const string WizardFlyoutTitle = "Set up access watches";

    /// <summary>Step 1's title.</summary>
    // approved wording (8f)
    public const string WizardStep1Title = "Setting up access watches: question 1 of 2";

    /// <summary>Step 1's question.</summary>
    // approved wording (8f)
    public const string WizardQuestion =
        "Do any of your courses require students to register in more than one section?";

    /// <summary>Step 1's example paragraph.</summary>
    // approved wording (8f)
    public const string WizardExample =
        "For example, students taking CHEM 101 might register in a lecture section, CHEM 101 A, and a lab section, CHEM 101 L1. " +
        "Students taking MUSIC 390 might register in a theory section and a recital section. These sections are the course's components.";

    /// <summary>Step 1's note about parallel courses (InlineFormatter markup: <c>*word*</c> is italic).</summary>
    // approved wording (Greg, 2026-11-25)
    public const string WizardParallelCourses =
        "Note that some institutions instead have students register in sections of parallel *courses*, such as a section " +
        "each of the *courses* CHEM 101 and CHEM 101Lab. That means a 'No' here, this question concerns sections of the " +
        "*same* course.";

    /// <summary>The Yes radio's label.</summary>
    // approved wording (8f)
    public const string WizardYesLabel = "Yes, some of my courses require registration in more than one section";

    /// <summary>The No radio's label.</summary>
    // approved wording (8f)
    public const string WizardNoLabel = "No, each of my courses requires registration in just one section";

    /// <summary>Step 2's title.</summary>
    // approved wording (8f)
    public const string WizardStep2Title = "Setting up access watches: question 2 of 2";

    /// <summary>Step 2's question.</summary>
    // approved wording (8f)
    public const string WizardTypesQuestion = "Which of your section types mark a component?";

    /// <summary>Step 2's explanation.</summary>
    // approved wording (8f)
    public const string WizardTypesText =
        "Tick the section types that can indicate components of a course, such as Lecture and Laboratory. Types that only " +
        "indicate how a section is taught (e.g. F2F, Online) aren't components. TermPoint has ticked the ones that look like components.";

    /// <summary>The link under step 2's boxes.</summary>
    // approved wording (8f)
    public const string WizardTypesFirstLink = "My section types need revising first…";

    /// <summary>Step 2's Next button tooltip while nothing is ticked.</summary>
    // approved wording (8f)
    public const string WizardNextDisabledTip = "Tick at least one, or go Back and answer No.";

    /// <summary>The "types first" step's title.</summary>
    // approved wording (8f)
    public const string WizardTypesFirstTitle = "Set up your section types first";

    /// <summary>The "types first" step's text (two paragraphs, joined by a blank line).</summary>
    // approved wording (8f)
    public const string WizardTypesFirstText =
        "TermPoint tells a course's components apart by section type. Under Scheduling Environment → Section Type, " +
        "add the types you need (such as Laboratory or Recital), then give them to the right sections in the Section List." +
        "\n\n" +
        "Then make your watch again; these questions come back then. Nothing has been saved.";

    /// <summary>The course check's title.</summary>
    // approved wording (8f)
    public const string WizardCoursesTitle = "Check your courses' components";

    /// <summary>The course check's text.</summary>
    // approved wording (8f)
    public const string WizardCoursesText =
        "Every course is listed, including ones not offered this term. The ticks indicate TermPoint's best guess, based on " +
        "sections already scheduled. Fix any that are incorrect.";

    /// <summary>The note under the course table (both course steps).</summary>
    // approved wording (8f)
    public const string WizardCoursesFootnote = "You can change any course later under Courses.";

    /// <summary>The "new type" step's title.</summary>
    /// <param name="typeName">The section type that was just marked.</param>
    /// <returns>The title.</returns>
    // approved wording (8f)
    public static string WizardNewTypeTitle(string typeName) => $"{typeName} is now a component: which courses have it?";

    /// <summary>The "new type" step's text.</summary>
    /// <param name="typeName">The section type that was just marked.</param>
    /// <returns>The text.</returns>
    // approved wording (8f)
    public static string WizardNewTypeText(string typeName) =>
        $"Courses that have {typeName} sections are ticked (highlighted). Every course is listed: tick any other that needs " +
        $"{typeName} sections.";

    /// <summary>The "new type" step's second button.</summary>
    // approved wording (8f)
    public const string WizardSkipForNow = "Skip for now";

    /// <summary>The wizard's message when the save fails (nothing was saved).</summary>
    public const string WizardSaveFailed = "The setup could not be saved. No changes were made. Please try again.";

    // ── The Access tab ────────────────────────────────────────────────────────

    /// <summary>The invitation's title (its only text: Greg, 2026-11-25, dropped the sentence below it).</summary>
    // approved wording (Greg, 2026-11-25)
    public const string SetupInviteTitle = "Two quick questions before using access watches";

    /// <summary>The invitation's button.</summary>
    // approved wording (8f)
    public const string SetupInviteButton = "Set up";

    /// <summary>The link at the invitation's bottom right, to the help page or video on access watches.</summary>
    // approved wording (Greg, 2026-11-25)
    public const string WhatIsAWatchLink = "What's an access watch?";

    /// <summary>
    /// Where <see cref="WhatIsAWatchLink"/> goes. The help page or video is still to be made (Greg, 2026-11-25): set
    /// this to it before the release.
    /// </summary>
    public const string WhatIsAWatchUri = "https://termpoint.ca/help/access-watches";

    /// <summary>The quiet line for a computer that cannot save.</summary>
    /// <param name="typeNames">The section types that look like components.</param>
    /// <returns>The sentence.</returns>
    // approved wording (8f)
    public static string SetupReadOnlyText(IReadOnlyList<string> typeNames) =>
        "Access watches aren't set up for this database yet. Until someone editing it sets them up, Access counts sections of " +
        $"{JoinAnd(typeNames)} as separate components.";

    // ── Scheduling Environment, Section Type ──────────────────────────────────

    /// <summary>The line above the Section Type list until a Component box is saved: the ticks are a guess.</summary>
    // approved wording (Greg, 2026-11-24)
    public const string ComponentGuessNote =
        "The Component ticks are TermPoint's guess, based on the type names. Change any that are wrong.";

    /// <summary>The Component column heading's tooltip.</summary>
    // approved wording (8f)
    public const string ComponentHeadingTip =
        "Ticked: a course can have this type as one of its components, which students take one section of each of " +
        "(such as one lecture and one lab). Which courses have it is set in Courses.";

    /// <summary>The Section Type page's description: what a section type is, then the Component column (two
    /// paragraphs, joined by a blank line).</summary>
    // approved wording (Greg, 2026-11-24)
    public const string SectionTypeDescription =
        "Section types describe the different kinds of sections: e.g. Lecture, Lab, Tutorial. Use Delivery Type to indicate " +
        "how the section is taught or delivered (e.g. Online)." +
        "\n\n" +
        "Select 'Component' to indicate those section types which describe essential components of at least one of your " +
        "courses. For example, if every offering of the course PSYCH 200 needs both a Lecture section and a Laboratory " +
        "section, both Lecture and Laboratory should be marked as components (even if another course PSYCH 202 needs only " +
        "a lecture section). In most circumstances, all Section Types will be components.";

    /// <summary>The ask shown in the row when unticking a Component box would change courses.</summary>
    /// <param name="typeName">The section type.</param>
    /// <param name="courseCodes">The calendar codes of the courses that list it.</param>
    /// <returns>The question.</returns>
    // approved wording (8f)
    public static string StopCountingAsk(string typeName, IReadOnlyList<string> courseCodes) =>
        $"Stop counting {typeName} as a component? It comes off " +
        $"{(courseCodes.Count > 4 ? $"{courseCodes.Count} courses" : JoinAnd(courseCodes))}.";

    /// <summary>The confirm button of that ask.</summary>
    // approved wording (8f)
    public const string StopCountingButton = "Stop counting it";

    // ── Courses ───────────────────────────────────────────────────────────────

    /// <summary>The Components column heading's tooltip.</summary>
    // approved wording (8c)
    public const string ComponentsColumnTip =
        "The kinds of section a student takes one of each of, such as a lecture and a lab. Set in Edit Course, from the " +
        "section types marked Component (Scheduling Environment → Section Type).";

    /// <summary>Edit Course's Components hint before the setup.</summary>
    // approved wording (8f)
    public const string ComponentsHintNotSetUp = "Components: set up access watches first (Access → Set up).";

    /// <summary>Edit Course's text when no section type is marked Component.</summary>
    // approved wording (8f)
    public const string NoComponentTypes = "No section type is marked Component.";

    /// <summary>The count beside a component box: sections of this course with this type in the current semester.</summary>
    /// <param name="count">How many.</param>
    /// <returns>"3 this term" or "none this term".</returns>
    // approved wording (8f)
    public static string ThisTermCount(int count) => count > 0 ? $"{count} this term" : "none this term";

    /// <summary>Edit Course's warning about sections that fill no component (rule 8).</summary>
    /// <param name="labels">The sections, each as "X01 (no section type)" or "X01 (Online)".</param>
    /// <returns>The warning.</returns>
    // approved wording (8f)
    public static string NotCountedWarning(IReadOnlyList<string> labels) =>
        $"Not counted with these components: {string.Join(", ", labels)}.";

    /// <summary>
    /// Rule 8a's sentence for one course and component type: sections of a component type that the course does not
    /// list. "BIOL 305 L1 is a Laboratory section, but Laboratory isn't one of BIOL 305's components."
    /// </summary>
    /// <param name="courseCode">The course's calendar code.</param>
    /// <param name="typeName">The section type's name.</param>
    /// <param name="sectionCodes">The offending sections' codes (at least one).</param>
    /// <returns>The sentence.</returns>
    // approved wording (8a)
    public static string MismatchSentence(string courseCode, string typeName, IReadOnlyList<string> sectionCodes)
    {
        bool many = sectionCodes.Count > 1;
        string article = many ? string.Empty : (Regex.IsMatch(typeName, "^[aeiou]", RegexOptions.IgnoreCase) ? "an " : "a ");
        return $"{courseCode} {JoinAnd(sectionCodes)} {(many ? "are" : "is")} {article}{typeName} section{(many ? "s" : string.Empty)}, " +
               $"but {typeName} isn't one of {courseCode}'s components.";
    }

    /// <summary>Rule 8a's sentences for one course over a semester view.</summary>
    /// <param name="inputs">The semester view (its set-up state decides whether anything can mismatch).</param>
    /// <param name="courseId">The course id.</param>
    /// <returns>One sentence per offending type; empty when none.</returns>
    public static IReadOnlyList<string> MismatchSentences(AccessInputs inputs, string courseId) =>
        ComponentGrouping.Mismatches(inputs, [courseId])
            .Select(m => MismatchSentence(
                inputs.CourseCode(courseId),
                inputs.SectionTypes.First(t => t.Id == m.SectionTypeId).Name,
                m.Sections.Select(s => s.SectionCode).ToList()))
            .ToList();

    /// <summary>
    /// A course's Components column text: the names joined, or empty for none. (No "Any one section" here: Greg,
    /// 2026-11-24, it describes how the analyzer counts, not the course, and confused next to a lone ticked type.)
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="courseId">The course id.</param>
    /// <returns>The text.</returns>
    // approved wording (8b, 8e)
    public static string ComponentsLabel(AccessInputs inputs, string courseId)
    {
        var names = inputs.ComponentsOf(courseId)
            .Select(id => inputs.SectionTypes.FirstOrDefault(t => t.Id == id)?.Name)
            .OfType<string>().ToList();
        return string.Join(", ", names);
    }

    // ── Copy Semester and import ──────────────────────────────────────────────

    /// <summary>
    /// Rule 36's line under the unticked section types option: section types are still copied for courses with more
    /// than one component.
    /// </summary>
    /// <param name="courseCodes">The calendar codes of the courses concerned (at least one).</param>
    /// <returns>The line.</returns>
    // approved wording (rule 36)
    public static string CopyComponentTypesLine(IReadOnlyList<string> courseCodes) =>
        $"Section types are still copied for {NamedOrCounted(courseCodes, "courses")}, which " +
        $"{(courseCodes.Count == 1 ? "has" : "have")} more than one component (such as Lecture and Laboratory), " +
        "so access watches can count their sections.";

    /// <summary>The line under the unticked tags option: watched tags are still copied.</summary>
    // approved wording (Greg, 2026-11-24)
    public const string CopyWatchedTagsLine = "Watched tags are still copied, so access watches carry over.";

    /// <summary>The import's summary line when new courses were given two or more components (rule 8b).</summary>
    /// <param name="courseCodes">The calendar codes of those new courses.</param>
    /// <returns>The line.</returns>
    // approved wording (Greg, 2026-11-24)
    public static string ImportComponentsLine(IReadOnlyList<string> courseCodes) =>
        $"Components were filled in for {NamedOrCounted(courseCodes, "new courses")} from their sections' types. " +
        "You can change them under Courses.";

    /// <summary>Up to three names joined "A, B and C"; more than three become "12 courses".</summary>
    /// <param name="names">The names.</param>
    /// <param name="plural">The plural noun for the count form.</param>
    /// <returns>The phrase.</returns>
    private static string NamedOrCounted(IReadOnlyList<string> names, string plural) =>
        names.Count <= 3 ? JoinAnd(names) : $"{names.Count} {plural}";

    /// <summary>The section types whose name looks like a component, as the invitation names them.</summary>
    /// <param name="sectionTypes">The section types.</param>
    /// <returns>The names, in the order given.</returns>
    public static IReadOnlyList<string> LikelyComponentNames(IEnumerable<SchedulingEnvironmentValue> sectionTypes) =>
        sectionTypes.Where(t => ComponentNames.LooksLikeComponent(t.Name)).Select(t => t.Name).ToList();
}
