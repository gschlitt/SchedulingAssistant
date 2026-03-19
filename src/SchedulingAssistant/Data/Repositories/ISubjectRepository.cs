using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Subject"/> entities (academic disciplines / departments).
/// </summary>
public interface ISubjectRepository
{
    /// <summary>Returns all subjects, ordered by name.</summary>
    List<Subject> GetAll();

    /// <summary>Returns the subject with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Subject? GetById(string id);

    /// <summary>Returns <c>true</c> if any course exists that belongs to the given subject.</summary>
    bool HasCourses(string subjectId);

    /// <summary>
    /// Returns <c>true</c> if a subject with the given <paramref name="name"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByName(string name, string? excludeId = null);

    /// <summary>
    /// Returns <c>true</c> if a subject with the given calendar <paramref name="abbreviation"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByAbbreviation(string abbreviation, string? excludeId = null);

    /// <summary>Inserts a new subject. The <see cref="Subject.Id"/> must already be set.</summary>
    void Insert(Subject subject);

    /// <summary>Updates the subject matched by <see cref="Subject.Id"/>.</summary>
    void Update(Subject subject);

    /// <summary>Deletes the subject with the given <paramref name="id"/>.</summary>
    void Delete(string id);
}
