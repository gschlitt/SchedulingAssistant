using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.Access;

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
}
