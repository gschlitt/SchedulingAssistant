namespace TermPoint.Licensing;

/// <summary>
/// Combines license validation and trial evaluation into a single access decision.
/// A remembered copy of the licence file (<see cref="LicenseCache"/>) stands in when the share can't be read.
/// </summary>
public class AppLicenseManager : IAppLicenseManager
{
    private readonly ILicenseValidator _licenseValidator;
    private readonly ITrialService _trialService;
    private readonly LicenseCache? _cache;
    private readonly Action<string>? _log;

    /// <param name="licenseValidator">Validates the <c>termpoint.lic</c> file.</param>
    /// <param name="trialService">Evaluates the per-user trial clock.</param>
    /// <param name="cache">Remembers the last good licence text per folder; null disables remembering.</param>
    /// <param name="log">Optional sink for diagnostic lines. Null = silent.</param>
    public AppLicenseManager(
        ILicenseValidator licenseValidator,
        ITrialService trialService,
        LicenseCache? cache = null,
        Action<string>? log = null)
    {
        _licenseValidator = licenseValidator;
        _trialService = trialService;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// Returns the access result when the licence file could not be read in time (the share is unreachable):
    /// the remembered licence for that folder if there is one (checked again for signature and expiry),
    /// otherwise the trial clock.
    /// </summary>
    /// <param name="shareDirectoryPath">Directory that should hold <c>termpoint.lic</c>; keys the remembered copy.</param>
    /// <returns>The access result.</returns>
    public AppAccessResult EvaluateAccessWithoutLicense(string shareDirectoryPath)
    {
        // Seed/read the trial first so trial.json exists exactly as on the normal path.
        var trial = _trialService.GetTrialStatus();
        return TryRememberedLicense(shareDirectoryPath) ?? FromTrial(trial);
    }

    /// <inheritdoc />
    public AppAccessResult EvaluateAccess(string shareDirectoryPath)
    {
        // Always evaluate the trial so trial.json is seeded on first launch,
        // even when a license is present. This prevents a fresh 30-day trial
        // if the license file is later deleted.
        var trial = _trialService.GetTrialStatus();

        var license = _licenseValidator.ValidateLicenseFile(shareDirectoryPath);

        if (license.State is LicenseState.Licensed or LicenseState.Expired)
        {
            if (license.RawContent is not null)
                _cache?.Remember(shareDirectoryPath, license.RawContent);
            return FromLicense(license);
        }

        if (license.State == LicenseState.NotFound)
            _cache?.Forget(shareDirectoryPath);
        else if (license.State == LicenseState.Unreadable)
        {
            _log?.Invoke($"[Licensing] License file unreadable ({license.ErrorReason})");
            var remembered = TryRememberedLicense(shareDirectoryPath);
            if (remembered is not null)
                return remembered;
        }

        // NotFound, Invalid, or Unreadable with nothing remembered → trial
        return FromTrial(trial);
    }

    /// <summary>
    /// Validates the remembered licence text for a folder, if any.
    /// </summary>
    /// <param name="shareDirectoryPath">The shared-database folder.</param>
    /// <returns>A Licensed or Expired result, or null when nothing usable is remembered.</returns>
    private AppAccessResult? TryRememberedLicense(string shareDirectoryPath)
    {
        var text = _cache?.Get(shareDirectoryPath);
        if (text is null)
            return null;

        var license = _licenseValidator.ValidateLicenseText(text);
        if (license.State is not (LicenseState.Licensed or LicenseState.Expired))
        {
            _log?.Invoke("[Licensing] Remembered license is not valid; ignoring it");
            return null;
        }

        _log?.Invoke("[Licensing] using remembered license");
        return FromLicense(license);
    }

    /// <summary>
    /// Builds the access result for a validated Licensed or Expired licence.
    /// </summary>
    /// <param name="license">The validation result.</param>
    /// <returns>Full access when Licensed; read-only (with purchase prompt) when Expired.</returns>
    private static AppAccessResult FromLicense(LicenseResult license)
    {
        if (license.State == LicenseState.Licensed)
            return new AppAccessResult
            {
                AccessLevel = AccessLevel.FullAccess,
                Reason = AccessReason.Licensed,
                DepartmentName = license.Department,
                InstitutionName = license.Institution,
                ExpiryDate = license.Expiry,
                DaysRemaining = null,
                ShowPurchasePrompt = false
            };

        return new AppAccessResult
        {
            AccessLevel = AccessLevel.ReadOnly,
            Reason = AccessReason.Expired,
            DepartmentName = license.Department,
            InstitutionName = license.Institution,
            ExpiryDate = license.Expiry,
            DaysRemaining = null,
            ShowPurchasePrompt = true
        };
    }

    /// <summary>
    /// Builds the access result from the trial clock alone.
    /// </summary>
    /// <param name="trial">The trial status.</param>
    /// <returns>Full access while in trial; read-only (unlicensed) after.</returns>
    private static AppAccessResult FromTrial(TrialResult trial)
    {
        if (trial.IsInTrial)
            return new AppAccessResult
            {
                AccessLevel = AccessLevel.FullAccess,
                Reason = AccessReason.Trial,
                DepartmentName = null,
                ExpiryDate = null,
                DaysRemaining = trial.DaysRemaining,
                TrialEndsUtc = trial.TrialEndsUtc,
                ShowPurchasePrompt = true
            };

        return new AppAccessResult
        {
            AccessLevel = AccessLevel.ReadOnly,
            Reason = AccessReason.Unlicensed,
            DepartmentName = null,
            ExpiryDate = null,
            DaysRemaining = null,
            ShowPurchasePrompt = true
        };
    }
}
