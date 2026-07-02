namespace TermPoint.Licensing;

/// <summary>
/// Manages the per-user trial clock stored in AppData.
/// Standalone service with no UI dependencies.
/// </summary>
public interface ITrialService
{
    /// <summary>
    /// Evaluates trial status. On the very first call, creates <c>trial.json</c>
    /// in AppData and starts the 30-day trial.
    /// </summary>
    TrialResult GetTrialStatus();
}
