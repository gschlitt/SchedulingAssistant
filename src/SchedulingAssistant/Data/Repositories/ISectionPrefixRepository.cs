using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="SectionPrefix"/> entities
/// (institutional prefix conventions used to auto-generate section codes).
/// </summary>
public interface ISectionPrefixRepository
{
    /// <summary>Returns all section prefixes, ordered by prefix text.</summary>
    List<SectionPrefix> GetAll();

    /// <summary>Inserts a new section prefix. The <see cref="SectionPrefix.Id"/> must already be set.</summary>
    void Insert(SectionPrefix prefix);

    /// <summary>Updates the section prefix matched by <see cref="SectionPrefix.Id"/>.</summary>
    void Update(SectionPrefix prefix);

    /// <summary>
    /// Deletes the section prefix with the given <paramref name="id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Delete(string id, DbTransaction? tx = null);

    /// <summary>
    /// Returns <c>true</c> if a section prefix with the given <paramref name="prefixText"/> already exists.
    /// Pass <paramref name="excludeId"/> to allow the current record to match without triggering a duplicate.
    /// </summary>
    bool ExistsByPrefix(string prefixText, string? excludeId = null);
}
