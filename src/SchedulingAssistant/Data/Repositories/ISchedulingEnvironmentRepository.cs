using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="SchedulingEnvironmentValue"/> entities.
/// Values are typed (e.g. sectionType, tag, staffType) and shared across the scheduling environment.
/// </summary>
public interface ISchedulingEnvironmentRepository
{
    /// <summary>
    /// Returns all values of the given <paramref name="type"/>
    /// (see <c>SchedulingEnvironmentTypes</c> constants), ordered by sort order then name.
    /// </summary>
    List<SchedulingEnvironmentValue> GetAll(string type);

    /// <summary>Returns the value with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    SchedulingEnvironmentValue? GetById(string id);

    /// <summary>
    /// Inserts a new value of the given <paramref name="type"/>.
    /// The <see cref="SchedulingEnvironmentValue.Id"/> must already be set.
    /// </summary>
    void Insert(string type, SchedulingEnvironmentValue value);

    /// <summary>Updates the value matched by <see cref="SchedulingEnvironmentValue.Id"/>.</summary>
    void Update(SchedulingEnvironmentValue value);

    /// <summary>
    /// Deletes the value with the given <paramref name="id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Delete(string id, DbTransaction? tx = null);

    /// <summary>
    /// Returns <c>true</c> if a value with the given <paramref name="name"/> already exists
    /// within the specified <paramref name="type"/>.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByName(string type, string name, string? excludeId = null);
}
