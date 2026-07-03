namespace TermPoint.Licensing;

/// <summary>
/// Combines license validation and trial evaluation into a single access decision.
/// </summary>
public class AppLicenseManager : IAppLicenseManager
{
    private readonly ILicenseValidator _licenseValidator;
    private readonly ITrialService _trialService;

    /// <param name="licenseValidator">Validates the <c>termpoint.lic</c> file.</param>
    /// <param name="trialService">Evaluates the per-user trial clock.</param>
    public AppLicenseManager(ILicenseValidator licenseValidator, ITrialService trialService)
    {
        _licenseValidator = licenseValidator;
        _trialService = trialService;
    }

    /// <inheritdoc />
    public AppAccessResult EvaluateAccess(string shareDirectoryPath)
    {
        // Always evaluate the trial so trial.json is seeded on first launch,
        // even when a license is present. This prevents a fresh 30-day trial
        // if the license file is later deleted.
        var trial = _trialService.GetTrialStatus();

        var license = _licenseValidator.ValidateLicenseFile(shareDirectoryPath);

        if (license.State == LicenseState.Licensed)
        {
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
        }

        if (license.State == LicenseState.Expired)
        {
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

        // NotFound or Invalid → fall through to trial

        if (trial.IsInTrial)
        {
            return new AppAccessResult
            {
                AccessLevel = AccessLevel.FullAccess,
                Reason = AccessReason.Trial,
                DepartmentName = null,
                ExpiryDate = null,
                DaysRemaining = trial.DaysRemaining,
                ShowPurchasePrompt = true
            };
        }

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
