using TermPoint.Models;
using TermPoint.Services.Access;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// The r9 wireframe's mock semester (Fall 2027) as a fixture: the same courses, sections, meeting times, campuses
/// and course tags as <c>sections(variant)</c> in <c>designs/wireframes/access-panel-r9.html</c>, so the engine can
/// be checked against the scenarios the design was agreed on. Variants are the mock's "Semester changes" 0-8. The
/// watches are the tags "Enviro Y2", "Chem Y2" and "Biomed Y2" (each a former course-list watch, D28).
/// </summary>
internal static class AccessMockSemester
{
    public const string EnviroY2 = "Enviro Y2";
    public const string ChemY2 = "Chem Y2";
    public const string BiomedY2 = "Biomed Y2";
    public const string Abbotsford = "Abbotsford";
    public const string Chilliwack = "Chilliwack";

    private static readonly Dictionary<string, string[]> CourseTags = new()
    {
        ["PSYCH 101"] = [EnviroY2], ["MATH 111"] = [ChemY2], ["STAT 104"] = [EnviroY2, ChemY2],
        ["BIOL 201"] = [EnviroY2], ["CHEM 213"] = [EnviroY2], ["GEOG 250"] = [EnviroY2], ["CHEM 201"] = [ChemY2],
        ["BMEG 230"] = [BiomedY2], ["CHBE 201"] = [BiomedY2], ["ELEC 203"] = [BiomedY2],
    };

    /// <summary>The courses with their stored components (the setup was done), as the wizard would pre-fill them.</summary>
    public static List<Course> Courses() =>
    [
        Crs("BIOL 201", Lecture, Lab), Crs("CHEM 213", Lecture), Crs("CHEM 201", Lecture, Lab),
        Crs("PSYCH 101", Lecture), Crs("STAT 104", Lecture), Crs("GEOG 250", Lecture),
        Crs("BIOL 111", Lecture), Crs("MATH 111", Lecture), Crs("BMEG 230", Lecture, Lab, Discussion),
        Crs("CHBE 201", Lecture), Crs("ELEC 203", Lecture), Crs("HIST 101", Lecture),
        Crs("ENGL 105", Lecture), Crs("BIOL 340", Lecture, Lab),
    ];

    /// <summary>Inputs for one variant of the mock semester, set up, with the stored components above.</summary>
    public static AccessInputs Inputs(int variant = 0, Proposal? proposal = null) =>
        AccessTestKit.Inputs(Sections(variant), Courses(), true, null, proposal);

    /// <summary>The mock's sections for one "Semester changes" variant.</summary>
    public static List<Section> Sections(int variant)
    {
        var psychA = variant is 1 or 2 ? At(Tt, 10, 11.33) : variant == 4 ? At(Tt, 9.5, 10.83) : At(Tt, 8.5, 9.83);
        var psychB = variant == 2 ? At(Tt, 10, 11.33) : variant == 4 ? At(Tt, 9.5, 10.83) : At(Mw, 10, 11.33);
        SectionDaySchedule[] ChemLec(SectionDaySchedule[] meets) => variant == 3 ? At(Tt, 14.5, 15.83) : meets;
        SectionDaySchedule[] Geog(SectionDaySchedule[] meets) =>
            variant == 4 ? At(Tt, 11, 12.33) : variant == 7 ? At(Mwf, 9, 9.83) : meets;
        var bmegLec = variant == 5 ? At([1, 2, 4], 14, 15.33) : At(Mw, 9.5, 10.83);
        var chem213 = variant == 7 ? At(Mwf, 9, 9.83) : At(Tt, 10, 11.33);

        var list = new List<(string Course, string Code, string? Type, SectionDaySchedule[] Meets)>
        {
            ("BIOL 201", "A", Lecture, At(Mwf, 9, 9.83)),
            ("BIOL 201", "L1", Lab, At(1, 13, 15.83)),
            ("BIOL 201", "L2", Lab, At(3, 13, 15.83)),
            ("BIOL 201", "L3", Lab, At(2, 13, 15.83)),
            ("CHEM 213", "A", Lecture, chem213),
            ("GEOG 250", "A", Lecture, Geog(At(Mw, 13, 14.33))),
            ("GEOG 250", "B", Lecture, Geog(At(Tt, 13, 14.33))),
            ("PSYCH 101", "A", Lecture, psychA),
            ("PSYCH 101", "B", Lecture, psychB),
            ("PSYCH 101", "C", Lecture, At(Tt, 10, 11.33)),
            ("STAT 104", "A", Lecture, At(Mwf, 11, 11.83)),
            ("STAT 104", "C", Lecture, At(Tt, 14.5, 15.83)),
            ("STAT 104", "D", Online, []),
            ("BIOL 111", "A", Lecture, At(Mwf, 8, 8.83)),
            ("MATH 111", "A", Lecture, At(Mwf, 12, 12.83)),
            ("MATH 111", "B", Lecture, At(Tt, 8.5, 9.83)),
            ("BMEG 230", "101", Lecture, bmegLec),
            ("BMEG 230", "L1A", Lab, At(1, 15, 17.83)),
            ("BMEG 230", "L1B", Lab, At(2, 14, 16.83)),
            ("BMEG 230", "L1C", Lab, At(4, 13, 15.83)),
            ("BMEG 230", "T1A", Discussion, At(1, 15, 15.83)),
            ("BMEG 230", "T1B", Discussion, At(3, 9, 9.83)),
            ("BMEG 230", "X01", null, At(4, 9, 9.83)),
            ("CHBE 201", "101", Lecture, At(Tt, 11, 12.33)),
            ("ELEC 203", "101", Lecture, At([0, 2], 14, 14.83)),
            ("ELEC 203", "102", Lecture, At(Tt, 13, 14.33)),
            ("CHEM 201", "LEC1", Lecture, At(Mwf, 8, 8.83)),
            ("CHEM 201", "LEC2", Lecture, ChemLec(At(Mwf, 10, 10.83))),
            ("CHEM 201", "LEC3", Lecture, ChemLec(At(Mwf, 13, 13.83))),
            ("CHEM 201", "LEC4", Lecture, ChemLec(At(Mwf, 12, 12.83))),
            ("CHEM 201", "LAB-A", Lab, At(0, 10, 13.83)),
            ("CHEM 201", "LAB-B", Lab, At(1, 8.5, 11.33)),
            ("CHEM 201", "LAB-C", Lab, At(1, 13, 15.83)),
            ("CHEM 201", "LAB-D", Lab, At(2, 14, 16.83)),
            ("CHEM 201", "LAB-E", Lab, At(3, 8.5, 11.33)),
            ("CHEM 201", "LAB-F", Lab, At(3, 13, 15.83)),
            ("CHEM 201", "LAB-G", Lab, At(4, 14, 16.83)),
            ("CHEM 201", "LAB-H", Lab, At(1, 15, 17.83)),
            ("HIST 101", "A", Lecture, At(Tt, 8.5, 9.83)),
            ("ENGL 105", "B", Lecture, At(Tt, 14.5, 15.83)),
            ("BIOL 340", "A", Lecture, At(Tt, 13, 14.33)),
            ("BIOL 340", "L1", Lab, At(1, 13, 15.83)),
            ("BIOL 340", "L2", Lab, At(2, 9, 11.83)),
            ("BIOL 340", "L3", Lab, At(4, 13, 15.83)),
        };
        if (variant == 8) list.RemoveAll(x => x.Course == "BMEG 230" && x.Type == Discussion);
        if (variant == 8) list.Add(("ENGL 105", "T1", Discussion, At(2, 15, 15.83)));

        var chilliwack = new HashSet<string> { "BIOL 201 L3", "STAT 104 C", "MATH 111 B" };
        return list.Select(x =>
        {
            var id = x.Course + " " + x.Code;
            var campus = chilliwack.Contains(id) || (variant == 6 && id == "CHEM 213 A") ? Chilliwack : Abbotsford;
            return Sec(x.Course, x.Code, x.Type, x.Meets, campus,
                CourseTags.TryGetValue(x.Course, out var tags) ? tags : []);
        }).ToList();
    }
}
