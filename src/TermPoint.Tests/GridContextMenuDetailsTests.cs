using System;
using System.Collections.Generic;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The details block at the top of the schedule grid's right-click menu (<see cref="SectionContextMenuViewModel"/>):
/// heading, instructors' full names (always, one per line, "none" when there are none), section type and tags, each
/// of the last two left out when empty. Approved wording (Greg, 2026-11-16).
/// </summary>
public sealed class GridContextMenuDetailsTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock (the menu's constructor needs both).</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;

    private readonly Instructor _ada  = new() { Id = "ada",  FirstName = "Ada",  LastName = "Lindqvist" };
    private readonly Instructor _ines = new() { Id = "ines", FirstName = "Ines", LastName = "Castillo" };
    private readonly Instructor _hugo = new() { Id = "hugo", FirstName = "Hugo", LastName = "Martins", IsActive = false };

    private readonly SchedulingEnvironmentValue _online = new() { Id = "t-online", Name = "Online" };
    private readonly SchedulingEnvironmentValue _first  = new() { Id = "t-first",  Name = "First-year" };
    private readonly SchedulingEnvironmentValue _lab    = new() { Id = "st-lab",   Name = "Lab" };

    public GridContextMenuDetailsTests()
    {
        _sectionRepo = new SectionRepository(_fx.Db);
    }

    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>A one-meeting section with the given instructors (in stored order).</summary>
    private static Section MakeSection(params string[] instructorIds)
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
        };
        foreach (var id in instructorIds)
            section.InstructorAssignments.Add(new InstructorAssignment { InstructorId = id });
        return section;
    }

    /// <summary>A menu loaded for <paramref name="section"/> with the test's instructors, tags and section type.</summary>
    private SectionContextMenuViewModel LoadMenu(Section section, string heading = "HIST 101 A")
    {
        var menu = new SectionContextMenuViewModel(_sectionRepo, () => { }, _fx.Lock, new FakeSectionCardState(), new SectionMarks());
        menu.Load(section, 1, 510, [_ines, _ada, _hugo], new List<Room>(), [_first, _online], null, 0,
            heading, [_lab]);
        return menu;
    }

    /// <summary>One instructor: singular label, full name (shown even though the tile's initials aren't shortened).</summary>
    [Fact]
    public void OneInstructor_SingularLabel_FullName()
    {
        var menu = LoadMenu(MakeSection("ada"));

        Assert.Equal("HIST 101 A", menu.DetailHeading);
        Assert.Equal("Instructor:", menu.DetailInstructorLabel);
        Assert.Equal("Ada Lindqvist", menu.DetailInstructorNames);
    }

    /// <summary>Several instructors: plural label, one per line in the section's stored order (inactive included).</summary>
    [Fact]
    public void SeveralInstructors_PluralLabel_OnePerLine_InStoredOrder()
    {
        var menu = LoadMenu(MakeSection("hugo", "ada", "ines"));

        Assert.Equal("Instructors:", menu.DetailInstructorLabel);
        Assert.Equal(string.Join(Environment.NewLine, "Hugo Martins", "Ada Lindqvist", "Ines Castillo"),
            menu.DetailInstructorNames);
    }

    /// <summary>No instructors (or only ids that no longer resolve): "Instructor: none".</summary>
    [Fact]
    public void NoInstructors_SaysNone()
    {
        Assert.Equal("none", LoadMenu(MakeSection()).DetailInstructorNames);

        var gone = LoadMenu(MakeSection("deleted-id"));
        Assert.Equal("Instructor:", gone.DetailInstructorLabel);
        Assert.Equal("none", gone.DetailInstructorNames);
    }

    /// <summary>Type and tags are named; tags follow the configured tag order, not the section's.</summary>
    [Fact]
    public void TypeAndTags_Named_TagsInConfiguredOrder()
    {
        var section = MakeSection("ada");
        section.SectionTypeId = "st-lab";
        section.TagIds = ["t-online", "t-first"];

        var menu = LoadMenu(section);

        Assert.Equal("Lab", menu.DetailSectionType);
        Assert.Equal("First-year, Online", menu.DetailTags);
    }

    /// <summary>No type, no tags (or ids that no longer resolve): both lines are left out (null).</summary>
    [Fact]
    public void NoTypeOrTags_LinesLeftOut()
    {
        Assert.Null(LoadMenu(MakeSection("ada")).DetailSectionType);
        Assert.Null(LoadMenu(MakeSection("ada")).DetailTags);

        var stale = MakeSection("ada");
        stale.SectionTypeId = "st-deleted";
        stale.TagIds = ["t-deleted"];
        var menu = LoadMenu(stale);
        Assert.Null(menu.DetailSectionType);
        Assert.Null(menu.DetailTags);
    }

    /// <summary>An empty heading falls back to the section code.</summary>
    [Fact]
    public void EmptyHeading_FallsBackToSectionCode()
    {
        Assert.Equal("A", LoadMenu(MakeSection(), heading: "").DetailHeading);
    }

    /// <summary>Loading a second section replaces every detail (the menu object is reused across right-clicks).</summary>
    [Fact]
    public void Reload_ReplacesDetails()
    {
        var first = MakeSection("ada", "ines");
        first.SectionTypeId = "st-lab";
        first.TagIds = ["t-online"];
        var menu = LoadMenu(first);

        menu.Load(MakeSection(), 1, 510, [_ada], new List<Room>(), [_online], null, 0, "BIOL 201 B", [_lab]);

        Assert.Equal("BIOL 201 B", menu.DetailHeading);
        Assert.Equal("none", menu.DetailInstructorNames);
        Assert.Null(menu.DetailSectionType);
        Assert.Null(menu.DetailTags);
    }
}
