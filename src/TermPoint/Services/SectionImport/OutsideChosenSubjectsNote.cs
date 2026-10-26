using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// The sentence an import adds to its "done" message when some of the sections it just created are in
/// subjects the user has not chosen (spec item 20, step 7, and import decision D6), so the user is not
/// left wondering where they went. The import is never scoped to the subject focus; this only explains
/// why some of the new sections are not shown in the section list or grid.
///
/// <para>Used by the Import Sections flyout (<c>ImportSectionsViewModel</c>). The sentences are the ones the
/// retired Dev Tools section import used (step 24.10), moved here from its <c>BuildOutsideChosenSubjectsNote</c>.</para>
/// </summary>
public static class OutsideChosenSubjectsNote
{
    /// <summary>Said when every created section is outside the chosen subjects.</summary>
    public const string NoneAreShown = "None of them are in the subjects you've chosen, so they're not shown.";

    /// <summary>Said when exactly one created section is outside the chosen subjects.</summary>
    public const string OneIsNotShown = "1 of them is in a subject you haven't chosen, so it's not shown.";

    /// <summary>
    /// Builds the sentence: how many of the sections just created are in subjects the user has not chosen
    /// and so will not appear in the section list or grid. A course that cannot be found counts as outside
    /// (no view could show its section). Never throws: this runs after the import has committed, so a
    /// failure to read the courses must not be reported as a failed import.
    /// </summary>
    /// <param name="focus">The subject focus in force; <see cref="FocusService.IsCourseInFocus"/> decides each course.</param>
    /// <param name="courses">Read for the created sections' courses (all courses are read, after the write).</param>
    /// <param name="createdCourseIds">The course id of each section created, one entry per section.</param>
    /// <param name="logger">Where a failure to read the courses is noted; <c>App.Logger</c> when null.</param>
    /// <returns>
    /// The sentence, or null when nothing needs saying: the focus is All, nothing was created, every created
    /// section is in the chosen subjects, or the courses could not be read.
    /// </returns>
    public static string? Build(
        FocusService focus,
        ICourseRepository courses,
        IReadOnlyList<string> createdCourseIds,
        IAppLogger? logger = null)
    {
        try
        {
            if (focus.Current.Kind == FocusKind.All || createdCourseIds.Count == 0) return null;

            var byId = new Dictionary<string, Course>();
            foreach (var course in courses.GetAll()) byId.TryAdd(course.Id, course);

            var outside = createdCourseIds.Count(id =>
                !byId.TryGetValue(id, out var course) || !focus.IsCourseInFocus(course));

            if (outside == 0) return null;
            if (outside == createdCourseIds.Count) return NoneAreShown;
            return outside == 1
                ? OneIsNotShown
                : $"{outside} of them are in subjects you haven't chosen, so they're not shown.";
        }
        catch (Exception ex)
        {
            (logger ?? App.Logger).LogInfo(
                $"[SectionImport] Could not count sections outside the chosen subjects: {ex.Message}");
            return null;
        }
    }
}
