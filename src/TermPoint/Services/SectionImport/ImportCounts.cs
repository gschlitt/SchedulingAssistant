namespace TermPoint.Services.SectionImport;

/// <summary>
/// How many records of each kind an import inserted. Returned by <see cref="SectionImportWriter.Write"/>
/// so the caller can tell the user what was added.
/// </summary>
/// <param name="Subjects">Subjects inserted.</param>
/// <param name="Campuses">Campuses inserted.</param>
/// <param name="SectionTypes">Section types inserted.</param>
/// <param name="MeetingTypes">Meeting types inserted.</param>
/// <param name="Tags">Tags inserted.</param>
/// <param name="Rooms">Rooms inserted.</param>
/// <param name="Courses">Courses inserted.</param>
/// <param name="Instructors">Instructors inserted.</param>
/// <param name="Sections">Sections inserted.</param>
public sealed record ImportCounts(
    int Subjects,
    int Campuses,
    int SectionTypes,
    int MeetingTypes,
    int Tags,
    int Rooms,
    int Courses,
    int Instructors,
    int Sections)
{
    /// <summary>The counts of an import that inserted nothing.</summary>
    public static ImportCounts None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The number of setup values inserted: subjects, campuses, section types, meeting types, tags and rooms.</summary>
    public int SetupValues => Subjects + Campuses + SectionTypes + MeetingTypes + Tags + Rooms;

    /// <summary>The number of records inserted, of every kind.</summary>
    public int Total => SetupValues + Courses + Instructors + Sections;
}
