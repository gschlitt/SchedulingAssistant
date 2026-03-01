namespace SchedulingAssistant.Models;

/// <summary>
/// A named favourite day pattern (e.g. "MWF" = days [1, 3, 5]).
/// Stored in the database so all users of the same database see the same patterns.
/// </summary>
public class BlockPattern
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User-facing label, e.g. "MWF".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Day numbers (1=Mon … 6=Sat) included in the pattern.</summary>
    public List<int> Days { get; set; } = new();
}
