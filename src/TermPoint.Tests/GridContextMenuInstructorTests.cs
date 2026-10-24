using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The schedule grid's right-click Instructors panel (<see cref="SectionContextMenuViewModel"/>):
/// <list type="bullet">
///   <item>Under chosen subjects (spec item 20) it groups instructors like the section editor's chooser: in-focus
///   first, the rest flagged out of focus with one "Other instructors" heading. Nobody is dropped.</item>
///   <item>An inactive instructor the section already has is listed, ticked, so Confirm cannot silently unassign
///   them (before this, the panel listed active instructors only and Confirm rebuilt the assignments from it).</item>
/// </list>
/// Real temp database and repository (<see cref="ExtensionDataDbFixture"/>); the grouping input is what
/// <c>FocusService.OrderInstructorsForChooser</c> hands over: the ordered list plus the out-of-focus ids.
/// </summary>
public sealed class GridContextMenuInstructorTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer (Confirm needs it).</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;

    // In-focus first (as the focus service orders them), then the out-of-focus ones.
    private readonly Instructor _ada   = new() { Id = "ada",   FirstName = "Ada",   LastName = "Lindqvist" };
    private readonly Instructor _ines  = new() { Id = "ines",  FirstName = "Ines",  LastName = "Castillo" };
    private readonly Instructor _leah  = new() { Id = "leah",  FirstName = "Leah",  LastName = "Novak" };
    private readonly Instructor _hugo  = new() { Id = "hugo",  FirstName = "Hugo",  LastName = "Martins", IsActive = false };
    private readonly Instructor _grace = new() { Id = "grace", FirstName = "Grace", LastName = "Tanaka" };

    public GridContextMenuInstructorTests()
    {
        _sectionRepo = new SectionRepository(_fx.Db);
    }

    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>Stores a one-meeting section with the given assignments and returns it as the grid would load it.</summary>
    private Section StoreSection(params InstructorAssignment[] assignments)
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            InstructorAssignments = assignments.ToList(),
        };
        _sectionRepo.Insert(section);
        return _sectionRepo.GetById(section.Id)!;
    }

    /// <summary>A menu loaded for <paramref name="section"/> with the given order and out-of-focus ids.</summary>
    private SectionContextMenuViewModel LoadMenu(
        Section section, IReadOnlyList<Instructor> ordered, IReadOnlySet<string>? outOfFocus)
    {
        var menu = new SectionContextMenuViewModel(_sectionRepo, () => { }, _fx.Lock);
        menu.Load(section, 1, 510, ordered, new List<Room>(), new List<SchedulingEnvironmentValue>(), outOfFocus);
        return menu;
    }

    /// <summary>Under chosen subjects: order kept, out-of-focus rows flagged, one heading on the first of them.</summary>
    [Fact]
    public void UnderFocus_InFocusFirst_ThenOthersFlagged_WithOneHeading()
    {
        var section = StoreSection();
        var menu = LoadMenu(section, [_ada, _ines, _leah, _grace], new HashSet<string> { "leah", "grace" });

        Assert.Equal(["ada", "ines", "leah", "grace"], menu.Instructors.Select(i => i.Id));
        Assert.Equal([false, false, true, true], menu.Instructors.Select(i => i.IsOutOfFocus));
        Assert.Equal([false, false, true, false], menu.Instructors.Select(i => i.ShowOthersHeading));
    }

    /// <summary>Under All or None (no out-of-focus ids): nothing flagged, no heading.</summary>
    [Fact]
    public void WithoutFocusGrouping_NothingFlagged()
    {
        var section = StoreSection();
        var menu = LoadMenu(section, [_ada, _leah, _grace], null);

        Assert.Equal(["ada", "leah", "grace"], menu.Instructors.Select(i => i.Id));
        Assert.DoesNotContain(menu.Instructors, i => i.IsOutOfFocus || i.ShowOthersHeading);
    }

    /// <summary>Only out-of-focus rows: flagged, but no heading (there is no in-focus group to separate them from).</summary>
    [Fact]
    public void OnlyOutOfFocusRows_NoHeading()
    {
        var section = StoreSection();
        var menu = LoadMenu(section, [_leah, _grace], new HashSet<string> { "leah", "grace" });

        Assert.All(menu.Instructors, i => Assert.True(i.IsOutOfFocus));
        Assert.DoesNotContain(menu.Instructors, i => i.ShowOthersHeading);
    }

    /// <summary>An inactive instructor nobody assigned stays hidden, as before.</summary>
    [Fact]
    public void UnassignedInactiveInstructor_IsNotListed()
    {
        var section = StoreSection();
        var menu = LoadMenu(section, [_ada, _hugo], null);

        Assert.Equal(["ada"], menu.Instructors.Select(i => i.Id));
    }

    /// <summary>
    /// An inactive instructor the section already has is listed and ticked, and an untouched Confirm keeps every
    /// assignment with its workload: before, the panel left them out and Confirm silently unassigned them.
    /// </summary>
    [Fact]
    public void AssignedInactiveInstructor_IsListedTicked_AndConfirmKeepsIt()
    {
        var section = StoreSection(
            new InstructorAssignment { InstructorId = "ada", Workload = 1m },
            new InstructorAssignment { InstructorId = "hugo", Workload = 0.5m });
        var menu = LoadMenu(section, [_ada, _hugo], new HashSet<string> { "hugo" });

        var hugoRow = Assert.Single(menu.Instructors, i => i.Id == "hugo");
        Assert.True(hugoRow.IsChecked);
        Assert.True(hugoRow.IsOutOfFocus);

        menu.ShowInstructorsCommand.Execute(null);
        menu.ConfirmCommand.Execute(null);

        var saved = _sectionRepo.GetById(section.Id)!;
        Assert.Equal(
            [("ada", (decimal?)1m), ("hugo", (decimal?)0.5m)],
            saved.InstructorAssignments.OrderBy(a => a.InstructorId).Select(a => (a.InstructorId, a.Workload)));
    }
}
