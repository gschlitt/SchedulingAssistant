namespace TermPoint.Licensing;

/// <summary>
/// Result of evaluating the local trial clock.
/// Returned by <see cref="ITrialService.GetTrialStatus"/>.
/// </summary>
public class TrialResult
{
    /// <summary>True when the trial period is active (days remaining > 0).</summary>
    public bool IsInTrial { get; init; }

    /// <summary>Days left in the trial. Zero when expired or not started.</summary>
    public int DaysRemaining { get; init; }

    /// <summary>True when the trial existed and has elapsed.</summary>
    public bool TrialExpired { get; init; }
}
