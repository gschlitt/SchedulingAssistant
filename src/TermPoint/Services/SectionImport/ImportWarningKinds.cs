namespace TermPoint.Services.SectionImport;

/// <summary>
/// The identifiers of the kinds of <see cref="ImportWarning"/> the planner produces, so tests and the
/// preview can tell them apart without comparing message text.
/// </summary>
public static class ImportWarningKinds
{
    /// <summary>A Begin time that is not a legal start time for the meeting's length (or at all).</summary>
    public const string LegalStartTime = "LegalStartTime";

    /// <summary>The academic year has no legal start times, so none were checked (file-wide).</summary>
    public const string NoLegalStartTimes = "NoLegalStartTimes";

    /// <summary>An existing course's title differs from the one in the file (the existing title is kept).</summary>
    public const string CourseTitleDiffers = "CourseTitleDiffers";

    /// <summary>A matched course or instructor is marked inactive.</summary>
    public const string Inactive = "Inactive";

    /// <summary>A new instructor shares a last name with an existing instructor.</summary>
    public const string InstructorLastName = "InstructorLastName";

    /// <summary>A new setup value (or building) is close to an existing one, or to another new one.</summary>
    public const string NearMiss = "NearMiss";

    /// <summary>A room's campus differs from the campus of the section that meets in it.</summary>
    public const string RoomCampus = "RoomCampus";

    /// <summary>Two meetings of one section overlap on the same day in a week they both meet.</summary>
    public const string OverlappingMeetings = "OverlappingMeetings";
}
