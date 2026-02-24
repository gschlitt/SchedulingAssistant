namespace SchedulingAssistant.Models;

/// <summary>
/// A named favourite day pattern (e.g. "Mon/Wed" = days [1, 3]).
/// Stored in AppSettings, not the database — these are app-level preferences.
/// </summary>
public class BlockPattern
{
    /// <summary>Fixed slot number: 1 or 2.</summary>
    public int Slot { get; set; }

    /// <summary>User-facing label, e.g. "Mon/Wed".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Day numbers (1=Mon … 6=Sat) included in the pattern.</summary>
    public List<int> Days { get; set; } = new();
}
