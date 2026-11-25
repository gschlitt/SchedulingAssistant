using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The wording of the Access tab's notices (rules 31 and 32): a port of the r9 wireframe's <c>noticeText</c>,
/// <c>causeOf</c> and <c>compactIds</c>. The six sentences and the first four causes are approved wording (rounds 6
/// and 7); the causes marked DRAFT are new in F and wait for Greg.
/// </summary>
public static partial class AccessText
{
    /// <summary>
    /// Names sections by course, each course's section codes joined: "CHEM 213 A and PSYCH 101 A and B" (the mock's
    /// <c>compactIds</c>). Courses come in order of first appearance.
    /// </summary>
    /// <param name="inputs">The semester view (for course codes).</param>
    /// <param name="sections">The sections.</param>
    /// <returns>The text; empty for none.</returns>
    public static string CompactSections(AccessInputs inputs, IEnumerable<Section> sections)
    {
        var courses = new List<string>();
        var codes = new Dictionary<string, List<string>>();
        foreach (var s in sections)
        {
            var key = s.CourseId ?? string.Empty;
            if (!codes.TryGetValue(key, out var list)) { codes[key] = list = []; courses.Add(key); }
            list.Add(s.SectionCode);
        }
        return string.Join(" and ", courses.Select(c =>
            (c.Length == 0 ? string.Empty : inputs.CourseCode(c) + " ") + JoinAnd(codes[c])));
    }

    /// <summary>
    /// The cause of a notice as the start of its sentence ("Moving CHEM 213 A to Chilliwack", "The conflict between
    /// CHEM 213 A and PSYCH 101 A") and whether it takes "mean" rather than "means".
    /// </summary>
    /// <param name="inputs">The semester view the notice is about (now).</param>
    /// <param name="cause">The cause.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The text and whether it is plural.</returns>
    // approved wording (round 6, round 8) for NewConflicts, MovedToCampus, Moved, SectionType, Components;
    // DRAFT wording for Added, Deleted, Tags, Course, LatestChanges and "Changing courses' components"
    public static (string Text, bool Plural) NoticeCauseText(AccessInputs inputs, NoticeCause cause, Func<string?, string> campusName)
    {
        string id = cause.Section is null ? string.Empty : SectionLabel(inputs, cause.Section);
        switch (cause.Kind)
        {
            case NoticeCauseKind.NewConflicts:
                return cause.Partners.Count == 1
                    ? ($"The conflict between {id} and {SectionLabel(inputs, cause.Partners[0])}", false)
                    : ($"The conflicts between {id} and {CompactSections(inputs, cause.Partners)}", true);
            case NoticeCauseKind.MovedToCampus: return ($"Moving {id} to {campusName(cause.CampusId)}", false);
            case NoticeCauseKind.Moved: return ($"Moving {id}", false);
            case NoticeCauseKind.SectionType: return ($"Changing {id}'s section type", false);
            case NoticeCauseKind.Added: return ($"Adding {id}", false);
            case NoticeCauseKind.Deleted: return ($"Deleting {id}", false);
            case NoticeCauseKind.Tags: return ($"Changing {id}'s tags", false);
            case NoticeCauseKind.Course: return ($"Changing {id}'s course", false);
            case NoticeCauseKind.Components:
                return cause.CourseIds.Count == 1
                    ? ($"Changing {inputs.CourseCode(cause.CourseIds[0])}'s components", false)
                    : ("Changing courses' components", false);
            default: return ("The latest changes", true);
        }
    }

    /// <summary>The sentence a saved change leaves in the Access tab.</summary>
    /// <param name="inputs">The semester view the notice is about (now).</param>
    /// <param name="watchName">The watch's name (its tag's name).</param>
    /// <param name="notice">The notice.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The sentence, e.g. "Moving CHEM 213 A means Chem Y2 has no full slate. Open Chem Y2 for more detail.".</returns>
    // approved wording (round 6; new kinds round 7)
    public static string NoticeText(AccessInputs inputs, string watchName, AccessNotice notice, Func<string?, string> campusName)
    {
        var (cause, plural) = NoticeCauseText(inputs, notice.Cause, campusName);
        string means = plural ? "mean" : "means";
        string w = watchName;
        string more = $" Open {w} for more detail.";
        var c = notice.Change;
        var sections = c.SectionIds
            .Select(id => inputs.Sections.FirstOrDefault(s => s.Id == id))
            .OfType<Section>().ToList();
        switch (c.Kind)
        {
            case AccessChangeKind.Lost:
                return $"{cause} {means} {(c.WasSingleSection ? $"{w}'s sections no longer fit together" : $"{w} has no full slate")}.{more}";
            case AccessChangeKind.Restored:
                return $"{cause} {means} {(c.WasSingleSection ? $"{w}'s sections fit together again" : $"{w} has a full slate again")}.";
            case AccessChangeKind.Essential:
                return $"{cause} {means} every {w} slate now uses {JoinAnd(sections.Select(s => SectionLabel(inputs, s)).ToList())}.{more}";
            case AccessChangeKind.Mixes:
                return $"{cause} {means} every {w} slate now mixes campuses.{more}";
            case AccessChangeKind.Stranded:
                return $"{cause} {means} {CompactSections(inputs, sections)} {(sections.Count > 1 ? "are" : "is")} stranded in {w}.{more}";
            default:
            {
                var codes = c.CourseIds.Select(inputs.CourseCode).ToList();
                return $"{cause} {means} {JoinAnd(codes)} {(codes.Count > 1 ? "are" : "is")} no longer in {w}.{more}";
            }
        }
    }
}
