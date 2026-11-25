using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.Access;

/// <summary>
/// Builds <see cref="AccessInputs"/> over a given set of sections (not necessarily a loaded semester), reading the
/// courses, section types, set-up mark and suggestion source the way <see cref="AccessState"/> does. Used by the
/// screens that need the engine's rules for one question (Courses, Copy Semester) without the whole state.
/// </summary>
public static class AccessInputsFactory
{
    /// <summary>Reads what the engine needs from the repositories and builds the inputs.</summary>
    /// <param name="sections">The sections to treat as the semester view.</param>
    /// <param name="courseRepo">Reads the courses.</param>
    /// <param name="envRepo">Reads the section types.</param>
    /// <param name="setupRepo">Reads the set-up mark.</param>
    /// <param name="sectionRepo">Reads the suggestion source.</param>
    /// <returns>The inputs.</returns>
    public static AccessInputs Build(
        IEnumerable<Section> sections,
        ICourseRepository courseRepo,
        ISchedulingEnvironmentRepository envRepo,
        IAccessChecksSetupRepository setupRepo,
        ISectionRepository sectionRepo) =>
        new(sections,
            courseRepo.GetAll().ToDictionary(c => c.Id),
            envRepo.GetAll(SchedulingEnvironmentTypes.SectionType),
            setupRepo.IsSetUp(),
            sectionRepo.GetSectionTypeIdsByCourse());
}
