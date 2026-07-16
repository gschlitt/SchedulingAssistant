using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class ImportResolutionIndexTests
{
    // ── Normalize ──────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, ImportResolutionIndex.Normalize(null));

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, ImportResolutionIndex.Normalize(""));

    [Fact]
    public void Normalize_Whitespace_ReturnsEmpty()
        => Assert.Equal(string.Empty, ImportResolutionIndex.Normalize("   "));

    [Fact]
    public void Normalize_Trims()
        => Assert.Equal("hello", ImportResolutionIndex.Normalize("  hello  "));

    [Fact]
    public void Normalize_CollapsesInternalWhitespace()
        => Assert.Equal("john smith", ImportResolutionIndex.Normalize("John   Smith"));

    [Fact]
    public void Normalize_Lowercases()
        => Assert.Equal("science building", ImportResolutionIndex.Normalize("Science Building"));

    [Fact]
    public void Normalize_CombinesTrimCollapseAndLowercase()
        => Assert.Equal("main campus", ImportResolutionIndex.Normalize("  Main   Campus  "));

    // ── Instructor Resolution ──────────────────────────────────────────────────

    [Fact]
    public void ResolveInstructor_SingleMatch_ReturnsInstructor()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = BuildIndex(instructors: [instructor]);

        var result = index.ResolveInstructor("Smith", "John", "JRS", out var warning);

        Assert.NotNull(result);
        Assert.Equal(instructor.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveInstructor_CaseInsensitive_ReturnsMatch()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = BuildIndex(instructors: [instructor]);

        var result = index.ResolveInstructor("SMITH", "JOHN", "jrs", out var warning);

        Assert.NotNull(result);
        Assert.Equal(instructor.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveInstructor_NoMatch_ReturnsNull()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = BuildIndex(instructors: [instructor]);

        var result = index.ResolveInstructor("Doe", "Jane", "JD", out var warning);

        Assert.Null(result);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveInstructor_EmptyName_ReturnsNull()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = BuildIndex(instructors: [instructor]);

        var result = index.ResolveInstructor("", "", "", out var warning);

        Assert.Null(result);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveInstructor_MultipleSameName_InitialsTiebreaker_Resolves()
    {
        var inst1 = MakeInstructor("Smith", "John", "JRS");
        var inst2 = MakeInstructor("Smith", "John", "JAS");
        var index = BuildIndex(instructors: [inst1, inst2]);

        var result = index.ResolveInstructor("Smith", "John", "JAS", out var warning);

        Assert.NotNull(result);
        Assert.Equal(inst2.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveInstructor_MultipleSameName_NoInitials_WarnsAmbiguous()
    {
        var inst1 = MakeInstructor("Smith", "John", "JRS");
        var inst2 = MakeInstructor("Smith", "John", "JAS");
        var index = BuildIndex(instructors: [inst1, inst2]);

        var result = index.ResolveInstructor("Smith", "John", null, out var warning);

        Assert.Null(result);
        Assert.NotNull(warning);
        Assert.Contains("Ambiguous", warning);
    }

    [Fact]
    public void ResolveInstructor_MultipleSameName_InitialsDontMatch_WarnsAmbiguous()
    {
        var inst1 = MakeInstructor("Smith", "John", "JRS");
        var inst2 = MakeInstructor("Smith", "John", "JAS");
        var index = BuildIndex(instructors: [inst1, inst2]);

        var result = index.ResolveInstructor("Smith", "John", "XYZ", out var warning);

        Assert.Null(result);
        Assert.NotNull(warning);
        Assert.Contains("none match initials", warning);
    }

    [Fact]
    public void ResolveInstructor_MultipleSameName_SameInitials_WarnsAmbiguous()
    {
        var inst1 = MakeInstructor("Smith", "John", "JS");
        var inst2 = MakeInstructor("Smith", "John", "JS");
        var index = BuildIndex(instructors: [inst1, inst2]);

        var result = index.ResolveInstructor("Smith", "John", "JS", out var warning);

        Assert.Null(result);
        Assert.NotNull(warning);
        Assert.Contains("Ambiguous", warning);
    }

    [Fact]
    public void ResolveInstructor_SingleMatch_IgnoresInitials()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = BuildIndex(instructors: [instructor]);

        // Initials don't match but there's only one candidate — still resolves
        var result = index.ResolveInstructor("Smith", "John", "XYZ", out var warning);

        Assert.NotNull(result);
        Assert.Equal(instructor.Id, result!.Id);
        Assert.Null(warning);
    }

    // ── Room Resolution ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveRoom_CompositeMatch_ReturnsRoom()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("Science Building", "204", out var warning);

        Assert.NotNull(result);
        Assert.Equal(room.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_CompositeMatch_CaseInsensitive()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("science building", "204", out var warning);

        Assert.NotNull(result);
        Assert.Equal(room.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_EmptyBuilding_FallsBackToNumberOnly_SingleMatch()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("", "204", out var warning);

        Assert.NotNull(result);
        Assert.Equal(room.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_EmptyBuilding_MultipleRoomsSameNumber_WarnsAmbiguous()
    {
        var room1 = MakeRoom("Science Building", "204");
        var room2 = MakeRoom("Arts Building", "204");
        var index = BuildIndex(rooms: [room1, room2]);

        var result = index.ResolveRoom("", "204", out var warning);

        Assert.Null(result);
        Assert.NotNull(warning);
        Assert.Contains("Ambiguous", warning);
    }

    [Fact]
    public void ResolveRoom_NullBuilding_FallsBackToNumberOnly()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom(null, "204", out var warning);

        Assert.NotNull(result);
        Assert.Equal(room.Id, result!.Id);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_NoMatch_ReturnsNull()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("Science Building", "999", out var warning);

        Assert.Null(result);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_EmptyRoomNumber_ReturnsNull()
    {
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("Science Building", "", out var warning);

        Assert.Null(result);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolveRoom_BuildingProvided_NoCompositeMatch_DoesNotFallBack()
    {
        // Building is provided but doesn't match — should NOT fall back to number-only
        var room = MakeRoom("Science Building", "204");
        var index = BuildIndex(rooms: [room]);

        var result = index.ResolveRoom("Arts Building", "204", out var warning);

        Assert.Null(result);
        Assert.Null(warning);
    }

    // ── Campus Resolution ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveCampus_NameMatch_ReturnsSev()
    {
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var index = BuildIndex(campuses: [campus]);

        var result = index.ResolveCampus("Main Campus");

        Assert.NotNull(result);
        Assert.Equal("c1", result!.Id);
    }

    [Fact]
    public void ResolveCampus_CaseInsensitive_ReturnsSev()
    {
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var index = BuildIndex(campuses: [campus]);

        var result = index.ResolveCampus("main campus");

        Assert.NotNull(result);
        Assert.Equal("c1", result!.Id);
    }

    [Fact]
    public void ResolveCampus_NoMatch_ReturnsNull()
    {
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var index = BuildIndex(campuses: [campus]);

        Assert.Null(index.ResolveCampus("Downtown Campus"));
    }

    [Fact]
    public void ResolveCampus_Empty_ReturnsNull()
    {
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var index = BuildIndex(campuses: [campus]);

        Assert.Null(index.ResolveCampus(""));
    }

    // ── Section Type Resolution ────────────────────────────────────────────────

    [Fact]
    public void ResolveSectionType_NameMatch()
    {
        var sev = MakeSev("st1", "Lecture");
        var index = BuildIndex(sectionTypes: [sev]);

        var result = index.ResolveSectionType("Lecture");

        Assert.NotNull(result);
        Assert.Equal("st1", result!.Id);
    }

    [Fact]
    public void ResolveSectionType_CaseInsensitive()
    {
        var sev = MakeSev("st1", "Lecture");
        var index = BuildIndex(sectionTypes: [sev]);

        Assert.NotNull(index.ResolveSectionType("lecture"));
    }

    [Fact]
    public void ResolveSectionType_NoMatch_ReturnsNull()
    {
        var sev = MakeSev("st1", "Lecture");
        var index = BuildIndex(sectionTypes: [sev]);

        Assert.Null(index.ResolveSectionType("Lab"));
    }

    // ── Tag Resolution ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveTag_NameMatch()
    {
        var sev = MakeSev("t1", "Upper Level");
        var index = BuildIndex(tags: [sev]);

        var result = index.ResolveTag("Upper Level");

        Assert.NotNull(result);
        Assert.Equal("t1", result!.Id);
    }

    [Fact]
    public void ResolveTag_CaseInsensitive()
    {
        var sev = MakeSev("t1", "Upper Level");
        var index = BuildIndex(tags: [sev]);

        Assert.NotNull(index.ResolveTag("upper level"));
    }

    [Fact]
    public void ResolveTag_NoMatch_ReturnsNull()
    {
        var sev = MakeSev("t1", "Upper Level");
        var index = BuildIndex(tags: [sev]);

        Assert.Null(index.ResolveTag("Graduate"));
    }

    [Fact]
    public void ResolveTag_DuplicateNormalizedNames_LastWriteWins()
    {
        var sev1 = MakeSev("t1", "Upper Level");
        var sev2 = MakeSev("t2", "upper level");
        var index = BuildIndex(tags: [sev1, sev2]);

        var result = index.ResolveTag("Upper Level");

        Assert.NotNull(result);
        Assert.Equal("t2", result!.Id);
    }

    // ── Meeting Type Resolution ────────────────────────────────────────────────

    [Fact]
    public void ResolveMeetingType_NameMatch()
    {
        var sev = MakeSev("mt1", "In Person");
        var index = BuildIndex(meetingTypes: [sev]);

        var result = index.ResolveMeetingType("In Person");

        Assert.NotNull(result);
        Assert.Equal("mt1", result!.Id);
    }

    [Fact]
    public void ResolveMeetingType_CaseInsensitive()
    {
        var sev = MakeSev("mt1", "In Person");
        var index = BuildIndex(meetingTypes: [sev]);

        Assert.NotNull(index.ResolveMeetingType("in person"));
    }

    [Fact]
    public void ResolveMeetingType_NoMatch_ReturnsNull()
    {
        var sev = MakeSev("mt1", "In Person");
        var index = BuildIndex(meetingTypes: [sev]);

        Assert.Null(index.ResolveMeetingType("Online"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Instructor MakeInstructor(string last, string first, string initials) =>
        new() { Id = Guid.NewGuid().ToString(), LastName = last, FirstName = first, Initials = initials };

    private static Room MakeRoom(string building, string roomNumber) =>
        new() { Id = Guid.NewGuid().ToString(), Building = building, RoomNumber = roomNumber };

    private static SchedulingEnvironmentValue MakeSev(string id, string name) =>
        new() { Id = id, Name = name };

    /// <summary>
    /// Builds an index with the supplied entities; missing params default to empty collections.
    /// </summary>
    private static ImportResolutionIndex BuildIndex(
        IEnumerable<Instructor>? instructors = null,
        IEnumerable<Room>? rooms = null,
        IEnumerable<Campus>? campuses = null,
        IEnumerable<SchedulingEnvironmentValue>? sectionTypes = null,
        IEnumerable<SchedulingEnvironmentValue>? tags = null,
        IEnumerable<SchedulingEnvironmentValue>? meetingTypes = null)
    {
        return ImportResolutionIndex.Build(
            instructors ?? [],
            rooms ?? [],
            campuses ?? [],
            sectionTypes ?? [],
            tags ?? [],
            meetingTypes ?? []);
    }
}
