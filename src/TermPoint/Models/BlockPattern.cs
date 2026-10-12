namespace TermPoint.Models;

/// <summary>
/// A named favourite day pattern (e.g. "MWF" = days [1, 3, 5]).
/// Stored in the database so all users of the same database see the same patterns.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class BlockPattern : PersistedRecord
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User-facing label, e.g. "MWF".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Day numbers (1=Mon … 6=Sat) included in the pattern.</summary>
    public List<int> Days { get; set; } = new();
}
