using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// Reads a semester back through the real repositories and checks it against <see cref="ImportSectionSpec"/>s:
/// that each section exists exactly once with the right course, code, section type, campus, tags, capacity,
/// instructors and meetings. It answers "did the importer write what the file said?" for any number of
/// sections, with the lookups (courses, instructors, rooms and the like) built once, so checking the 5,000-row
/// file costs a few seconds, not a query per section.
///
/// <para>Expected values follow the design, not the file alone: a blank Capacity becomes the course's capacity
/// else <see cref="AppSettings.Current"/>'s default (D1), and a section also gets its course's tags (D2). For
/// the new courses a stress file creates both of those are empty, so the file's own values are expected.</para>
/// </summary>
internal sealed class StressVerifier
{
    private readonly Dictionary<string, Course> _coursesByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Instructor> _instructors;
    private readonly Dictionary<string, Room> _rooms;
    private readonly Dictionary<string, Campus> _campuses;
    private readonly Dictionary<string, string> _valueNames = new();
    private readonly ILookup<string, Section> _sections;
    private readonly int? _defaultCapacity;

    /// <summary>
    /// Reads every list the checks need from the rig's database.
    /// </summary>
    /// <param name="rig">The rig whose database was imported into.</param>
    /// <param name="semesterId">The semester to read the sections of.</param>
    public StressVerifier(ImportRig rig, string semesterId)
    {
        foreach (var course in rig.Courses.GetAll())
            _coursesByCode.TryAdd(NameMatch.NormalizeCode(course.CalendarCode), course);
        _instructors = rig.Instructors.GetAll().ToDictionary(i => i.Id);
        _rooms = rig.Rooms.GetAll().ToDictionary(r => r.Id);
        _campuses = rig.Campuses.GetAll().ToDictionary(c => c.Id);
        foreach (var type in new[] { SchedulingEnvironmentTypes.SectionType, SchedulingEnvironmentTypes.MeetingType, SchedulingEnvironmentTypes.Tag })
            foreach (var value in rig.Environment.GetAll(type))
                _valueNames[value.Id] = value.Name;
        _sections = rig.Sections.GetAll(semesterId).ToLookup(s => SectionKey(s.CourseId, s.SectionCode));
        _defaultCapacity = AppSettings.Current.DefaultSectionCapacity;
    }

    /// <summary>The key sections are looked up by: course id and the normalised, case-folded code.</summary>
    private static string SectionKey(string? courseId, string code) =>
        $"{courseId}\u001F{NameMatch.Normalize(code).ToLowerInvariant()}";

    /// <summary>The course with this code (spaces and case ignored), or null.</summary>
    /// <param name="code">The calendar code, e.g. <c>SML101</c>.</param>
    /// <returns>The course, or null.</returns>
    public Course? FindCourse(string code) =>
        _coursesByCode.GetValueOrDefault(NameMatch.NormalizeCode(code));

    /// <summary>The section of the semester with this course and code, or null (also null when there are several).</summary>
    /// <param name="spec">The section's spec.</param>
    /// <returns>The section found.</returns>
    public Section? FindSection(ImportSectionSpec spec)
    {
        var course = FindCourse(spec.Subject + spec.CourseNumber);
        if (course is null)
            return null;
        var found = _sections[SectionKey(course.Id, spec.SectionCode)].ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// Checks one section against its spec.
    /// </summary>
    /// <param name="spec">What the file said.</param>
    /// <param name="checkTitle">Also compare the course title (true for a course the file created).</param>
    /// <returns>One line per difference; empty when the section is as the spec says.</returns>
    public List<string> Verify(ImportSectionSpec spec, bool checkTitle = true)
    {
        var differences = new List<string>();
        void Differ(string what, object? expected, object? found) =>
            differences.Add($"{spec.Label}: {what}: expected [{expected}] but found [{found}]");

        var course = FindCourse(spec.Subject + spec.CourseNumber);
        if (course is null)
        {
            differences.Add($"{spec.Label}: course {spec.Subject}{spec.CourseNumber} was not created");
            return differences;
        }
        if (checkTitle && spec.CourseTitle is not null && course.Title != spec.CourseTitle)
            Differ("course title", Shorten(spec.CourseTitle), Shorten(course.Title));

        var matches = _sections[SectionKey(course.Id, spec.SectionCode)].ToList();
        if (matches.Count != 1)
        {
            differences.Add($"{spec.Label}: expected exactly one section but found {matches.Count}");
            return differences;
        }
        var section = matches[0];

        if (!string.Equals(NameMatch.Normalize(section.SectionCode), NameMatch.Normalize(spec.SectionCode), StringComparison.OrdinalIgnoreCase))
            Differ("section code", spec.SectionCode, section.SectionCode);
        if (NameOf(section.SectionTypeId) != spec.SectionType)
            Differ("section type", spec.SectionType, NameOf(section.SectionTypeId));
        var campus = section.CampusId is null ? null : _campuses.GetValueOrDefault(section.CampusId)?.Name;
        if (campus != spec.Campus)
            Differ("campus", spec.Campus, campus);

        int? expectedCapacity = spec.Capacity ?? course.Capacity ?? _defaultCapacity;
        if (section.Capacity != expectedCapacity)
            Differ("capacity", expectedCapacity, section.Capacity);

        var expectedTags = spec.Tags.Concat(course.TagIds.Select(NameOf).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        var tags = section.TagIds.Select(NameOf).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (!expectedTags.SetEquals(tags))
            Differ("tags", string.Join("; ", expectedTags.OrderBy(t => t)), string.Join("; ", tags.OrderBy(t => t)));

        var expectedInstructors = spec.Instructors.Select(SplitName).ToList();
        var instructors = section.InstructorAssignments
            .Select(a => _instructors.GetValueOrDefault(a.InstructorId))
            .Select(i => (Last: i?.LastName ?? "?", First: i?.FirstName ?? "?"))
            .ToList();
        if (!expectedInstructors.SequenceEqual(instructors))
            Differ("instructors", string.Join(" | ", expectedInstructors), string.Join(" | ", instructors));

        var expectedMeetings = spec.Meetings.SelectMany(m => m.PerDay())
            .Select(m => MeetingText(m.Day, m.Start, m.Duration, m.Frequency, m.MeetingType, m.Building, m.Room))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        var meetings = section.Schedule
            .Select(m =>
            {
                var room = m.RoomId is null ? null : _rooms.GetValueOrDefault(m.RoomId);
                return MeetingText(m.Day, m.StartMinutes, m.DurationMinutes, m.Frequency, NameOf(m.MeetingTypeId),
                    room?.Building, room?.RoomNumber);
            })
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (!expectedMeetings.SequenceEqual(meetings))
            Differ("meetings", string.Join(" ; ", expectedMeetings), string.Join(" ; ", meetings));

        return differences;
    }

    /// <summary>
    /// Checks many sections, stopping to describe only the first few differences so a wholesale failure stays readable.
    /// </summary>
    /// <param name="specs">The sections.</param>
    /// <param name="checkTitle">Also compare course titles.</param>
    /// <param name="maxReported">How many difference lines to keep.</param>
    /// <param name="totalDifferences">The total number of differences found.</param>
    /// <returns>The first <paramref name="maxReported"/> difference lines.</returns>
    public List<string> VerifyAll(IEnumerable<ImportSectionSpec> specs, bool checkTitle, int maxReported, out int totalDifferences)
    {
        var kept = new List<string>();
        totalDifferences = 0;
        foreach (var spec in specs)
        {
            foreach (var difference in Verify(spec, checkTitle))
            {
                totalDifferences++;
                if (kept.Count < maxReported)
                    kept.Add(difference);
            }
        }
        return kept;
    }

    /// <summary>The name of a section type, meeting type or tag id; null for no id or an unknown one.</summary>
    private string? NameOf(string? id) => id is null ? null : _valueNames.GetValueOrDefault(id);

    /// <summary>Splits <c>Last, First</c> at the first comma, trimming both parts, as the importer does.</summary>
    private static (string Last, string First) SplitName(string entry)
    {
        var comma = entry.IndexOf(',');
        return comma < 0 ? (entry.Trim(), string.Empty) : (entry[..comma].Trim(), entry[(comma + 1)..].Trim());
    }

    /// <summary>One meeting as comparable text (null and empty frequency, building and room are the same).</summary>
    private static string MeetingText(int day, int start, int duration, string? frequency, string? meetingType, string? building, string? room) =>
        $"{day}|{start}|{duration}|{frequency ?? ""}|{meetingType ?? ""}|{building ?? ""}|{room ?? ""}";

    /// <summary>Cuts long text for a difference line.</summary>
    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + $"... ({text.Length} chars)";
}
