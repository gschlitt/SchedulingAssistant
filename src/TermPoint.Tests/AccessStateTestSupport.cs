using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.Management;

namespace TermPoint.Tests;

/// <summary>Builds an <see cref="AccessState"/> over a temp database for the tests that need a real grid view model.</summary>
internal static class AccessStateTestSupport
{
    /// <summary>
    /// An access state over the database's repositories, wired to the given store and notifier (which must be the
    /// ones the grid under test uses, as the dependency container arranges).
    /// </summary>
    /// <param name="db">The temp database.</param>
    /// <param name="sectionStore">The test's section store.</param>
    /// <param name="notifier">The test's grid change notifier.</param>
    /// <returns>The access state.</returns>
    public static AccessState For(IDatabaseContext db, SectionStore sectionStore, GridChangeNotifier notifier) =>
        new(sectionStore, notifier, new SectionRepository(db), new CourseRepository(db),
            new SchedulingEnvironmentRepository(db), new AccessChecksSetupRepository(db));

    /// <summary>
    /// An Access tab with its open watch, detail and Check all courses page, built the way the dependency container
    /// builds them.
    /// </summary>
    /// <param name="db">The temp database.</param>
    /// <param name="state">The access state.</param>
    /// <param name="semesters">The loaded semesters.</param>
    /// <param name="sectionStore">The section store the access state watches.</param>
    /// <param name="toggler">The eye and the Multi-campus box.</param>
    /// <param name="setup">The access setup service.</param>
    /// <param name="picture">Receives the open watch the tab shares with its pages, so a test can look at it.</param>
    /// <returns>The tab.</returns>
    public static AccessTabViewModel TabFor(
        IDatabaseContext db, AccessState state, SemesterContext semesters, SectionStore sectionStore,
        AccessWatchToggler toggler, AccessSetupService setup, out AccessPicture picture)
    {
        picture = new AccessPicture(state);
        var campuses = new CampusRepository(db);
        var detail = new AccessWatchDetailViewModel(picture, state, toggler, semesters, campuses, sectionStore);
        var check = new AccessCheckViewModel(state, picture, setup, semesters, campuses, sectionStore);
        return new AccessTabViewModel(state, semesters, campuses, toggler, setup, picture, detail, check);
    }
}
