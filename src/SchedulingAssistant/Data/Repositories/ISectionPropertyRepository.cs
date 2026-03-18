using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="SectionPropertyValue"/> entities.
/// Property values are typed (e.g. sectionType, tag, campus) and shared across all sections.
/// </summary>
public interface ISectionPropertyRepository
{
    /// <summary>
    /// Returns all property values of the given <paramref name="type"/>
    /// (see <c>SectionPropertyTypes</c> constants), ordered by name.
    /// </summary>
    List<SectionPropertyValue> GetAll(string type);

    /// <summary>Returns the property value with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    SectionPropertyValue? GetById(string id);

    /// <summary>
    /// Inserts a new property value of the given <paramref name="type"/>.
    /// The <see cref="SectionPropertyValue.Id"/> must already be set.
    /// </summary>
    void Insert(string type, SectionPropertyValue value);

    /// <summary>Updates the property value matched by <see cref="SectionPropertyValue.Id"/>.</summary>
    void Update(SectionPropertyValue value);

    /// <summary>
    /// Deletes the property value with the given <paramref name="id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Delete(string id, DbTransaction? tx = null);

    /// <summary>
    /// Returns <c>true</c> if a property value with the given <paramref name="name"/> already exists
    /// within the specified <paramref name="type"/>.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByName(string type, string name, string? excludeId = null);
}
