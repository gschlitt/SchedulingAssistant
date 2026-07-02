namespace TermPoint.Licensing;

/// <summary>
/// Whether the user gets full editing access or read-only.
/// </summary>
public enum AccessLevel
{
    /// <summary>Full editing access (licensed or in trial).</summary>
    FullAccess,

    /// <summary>Can view the timetable but not edit (unlicensed/expired).</summary>
    ReadOnly
}

/// <summary>
/// Why the current access level was granted.
/// </summary>
public enum AccessReason
{
    /// <summary>A valid, non-expired license was found.</summary>
    Licensed,

    /// <summary>No license, but the trial period is still active.</summary>
    Trial,

    /// <summary>No license and the trial has elapsed.</summary>
    Unlicensed,

    /// <summary>A license was found but it has expired.</summary>
    Expired
}

/// <summary>
/// Combined license + trial evaluation, used by the UI layer to decide
/// what to show and what to gate.
/// </summary>
public class AppAccessResult
{
    /// <summary>Whether the user can edit or only view.</summary>
    public AccessLevel AccessLevel { get; init; }

    /// <summary>Why this access level was granted.</summary>
    public AccessReason Reason { get; init; }

    /// <summary>Department name from the license, if licensed.</summary>
    public string? DepartmentName { get; init; }

    /// <summary>Trial days remaining, if in trial.</summary>
    public int? DaysRemaining { get; init; }

    /// <summary>License expiry date, if licensed with an expiry.</summary>
    public DateTime? ExpiryDate { get; init; }

    /// <summary>Whether the UI should show a purchase prompt.</summary>
    public bool ShowPurchasePrompt { get; init; }
}
