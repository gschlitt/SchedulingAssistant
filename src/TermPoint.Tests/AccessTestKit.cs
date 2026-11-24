using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.Tests;

/// <summary>
/// Builders for the access-engine tests: hand-made sections, courses and section types, so a test reads as the
/// scenario it checks. Days follow the r9 wireframe's mock: 0 = Monday ... 4 = Friday (stored as 1..5), times are
/// decimal hours (11.33 = 11:20).
/// </summary>
internal static class AccessTestKit
{
    public const string Lecture = "Lecture";
    public const string Lab = "Laboratory";
    public const string Discussion = "Discussion";
    public const string Online = "Online";

    public static readonly int[] Tt = [1, 3];
    public static readonly int[] Mwf = [0, 2, 4];
    public static readonly int[] Mw = [0, 2];

    /// <summary>One meeting per listed day, from <paramref name="start"/> to <paramref name="end"/> (decimal hours).</summary>
    public static SectionDaySchedule[] At(int[] days, double start, double end, string? frequency = null) =>
        days.Select(d => new SectionDaySchedule
        {
            Day = d + 1,
            StartMinutes = (int)Math.Round(start * 60),
            DurationMinutes = (int)Math.Round(end * 60) - (int)Math.Round(start * 60),
            Frequency = frequency,
        }).ToArray();

    /// <summary>A single-day shorthand for <see cref="At(int[], double, double, string?)"/>.</summary>
    public static SectionDaySchedule[] At(int day, double start, double end, string? frequency = null) =>
        At([day], start, end, frequency);

    /// <summary>A section whose id is "course code" (e.g. "BIOL 201 L1"), with its meetings, campus and tags.</summary>
    public static Section Sec(string course, string code, string? type = null,
        IEnumerable<SectionDaySchedule>? meetings = null, string? campus = null, IEnumerable<string>? tags = null) =>
        new()
        {
            Id = course + " " + code,
            SemesterId = "sem",
            CourseId = course,
            SectionCode = code,
            SectionTypeId = type,
            CampusId = campus,
            Schedule = meetings?.ToList() ?? [],
            TagIds = tags?.ToList() ?? [],
        };

    /// <summary>The standard section types in sort order; Online is not a component unless asked.</summary>
    public static List<SchedulingEnvironmentValue> Types(bool onlineIsComponent = false) =>
    [
        new() { Id = Lecture, Name = Lecture, SortOrder = 0, IsComponent = true },
        new() { Id = Lab, Name = Lab, SortOrder = 1, IsComponent = true },
        new() { Id = Discussion, Name = Discussion, SortOrder = 2, IsComponent = true },
        new() { Id = Online, Name = Online, SortOrder = 3, IsComponent = onlineIsComponent },
    ];

    /// <summary>A course whose id is its calendar code and whose stored components are <paramref name="components"/> (null: never set).</summary>
    public static Course Crs(string code, params string[]? components) => new()
    {
        Id = code,
        CalendarCode = code,
        ComponentTypeIds = components?.ToList(),
    };

    /// <summary>A course that was never set up (stored components null).</summary>
    public static Course UnsetCrs(string code) => new() { Id = code, CalendarCode = code, ComponentTypeIds = null };

    /// <summary>Builds inputs for one semester.</summary>
    public static AccessInputs Inputs(
        IEnumerable<Section> sections, IEnumerable<Course> courses, bool setUp = true,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? suggestion = null,
        Proposal? proposal = null, IEnumerable<SchedulingEnvironmentValue>? types = null) =>
        new(sections, courses.ToDictionary(c => c.Id), types ?? Types(), setUp,
            suggestion ?? new Dictionary<string, IReadOnlySet<string>>(), proposal);

    /// <summary>Inputs where every section's course is a single-component course (stored list [Lecture]) unless listed.</summary>
    public static AccessInputs Simple(IReadOnlyList<Section> sections, params Course[] courses)
    {
        var known = courses.Select(c => c.Id).ToHashSet();
        var all = courses.Concat(sections.Select(s => s.CourseId!).Distinct()
            .Where(c => !known.Contains(c)).Select(c => Crs(c, Lecture)));
        return Inputs(sections, all);
    }

    /// <summary>Analyses every section of the inputs as one set, Multi-campus off, no subset, no pins.</summary>
    public static Analysis AnalyzeAll(AccessInputs inputs, bool multiCampus = false,
        IReadOnlySet<string>? subset = null, IReadOnlySet<string>? pins = null, int cap = AccessAnalyzer.DefaultCap) =>
        AccessAnalyzer.Analyze(inputs, inputs.Sections, multiCampus, subset, pins, cap);

    /// <summary>The ids of the given sections.</summary>
    public static string[] Ids(IEnumerable<Section> sections) => sections.Select(s => s.Id).ToArray();

    /// <summary>
    /// Reference slate counter: the whole product of candidate choices, checked pairwise with the public
    /// conflict and campus tests. Independent of <see cref="SlateSearch"/>.
    /// </summary>
    public static int BruteForceCount(IReadOnlyList<ComponentGroup> groups, bool oneCampusRule)
    {
        int count = 0;
        var pick = new List<Section>();
        void Go(int i)
        {
            if (i == groups.Count) { count++; return; }
            foreach (var s in groups[i].Candidates)
            {
                if (pick.Any(p => SectionOverlap.Conflicts(p, s) || (oneCampusRule && CampusRule.Splits(p, s)))) continue;
                pick.Add(s);
                Go(i + 1);
                pick.RemoveAt(pick.Count - 1);
            }
        }
        Go(0);
        return count;
    }
}
