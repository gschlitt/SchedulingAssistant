using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// 1.2.4: licence file read outcomes (NotFound vs Unreadable), expiry by local date, the remembered-licence
/// cache, and how <see cref="AppLicenseManager"/> uses it when the share can't be read.
/// </summary>
public sealed class LicenseCacheAndReadTests : IDisposable
{
    private readonly LicenseTestKit _kit = new();
    private readonly string _share;
    private readonly string _appData;

    public LicenseCacheAndReadTests()
    {
        _share = _kit.NewDir("share");
        _appData = _kit.NewDir("appdata");
    }

    public void Dispose() => _kit.Dispose();

    private static TestClock ClockOn(int y, int m, int d, int hour = 12) => new(new DateTime(y, m, d, hour, 0, 0, DateTimeKind.Utc));

    // ── Validator ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_ExpiryIsComparedWithToday_LastDayStillLicensed()
    {
        LicenseTestKit.WriteLicense(_share, _kit.SignedLicenseText("2027-07-01"));
        var validator = new LicenseValidator(_kit.PublicKeyPem, ClockOn(2027, 7, 1, hour: 23));
        Assert.Equal(LicenseState.Licensed, validator.ValidateLicenseFile(_share).State);

        validator = new LicenseValidator(_kit.PublicKeyPem, ClockOn(2027, 7, 2, hour: 0));
        Assert.Equal(LicenseState.Expired, validator.ValidateLicenseFile(_share).State);
    }

    [Fact]
    public void Validator_MissingFile_And_MissingDirectory_AreNotFound()
    {
        var validator = new LicenseValidator(_kit.PublicKeyPem, ClockOn(2027, 1, 1));
        Assert.Equal(LicenseState.NotFound, validator.ValidateLicenseFile(_share).State);
        Assert.Equal(LicenseState.NotFound,
            validator.ValidateLicenseFile(Path.Combine(_share, "no-such-folder")).State);
    }

    [Fact]
    public void Validator_ReadErrorOtherThanNotFound_IsUnreadable()
    {
        LicenseTestKit.WriteLicense(_share, _kit.SignedLicenseText(null));
        var validator = new LicenseValidator(_kit.PublicKeyPem, ClockOn(2027, 1, 1));

        using var hold = new FileStream(Path.Combine(_share, "termpoint.lic"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = validator.ValidateLicenseFile(_share);

        Assert.Equal(LicenseState.Unreadable, result.State);
    }

    [Fact]
    public void Validator_LicensedFile_CarriesRawContent()
    {
        var text = _kit.SignedLicenseText(null);
        LicenseTestKit.WriteLicense(_share, text);
        var result = new LicenseValidator(_kit.PublicKeyPem, ClockOn(2027, 1, 1)).ValidateLicenseFile(_share);
        Assert.Equal(text, result.RawContent);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cache_RememberGetForget_RoundTrip()
    {
        var cache = new LicenseCache(_appData);
        Assert.Null(cache.Get(_share));
        cache.Remember(_share, "abc");
        Assert.Equal("abc", cache.Get(_share));
        cache.Forget(_share);
        Assert.Null(cache.Get(_share));
    }

    [Fact]
    public void Cache_FolderMatchIgnoresCaseAndTrailingSeparator()
    {
        var cache = new LicenseCache(_appData);
        cache.Remember(_share, "abc");
        Assert.Equal("abc", cache.Get(_share.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.Equal("abc", cache.Get(_share.ToLowerInvariant()));
    }

    [Fact]
    public void Cache_CorruptFile_IsEmpty_AndDoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_appData, "license-cache.json"), "{ this is not json");
        var cache = new LicenseCache(_appData);

        Assert.Null(cache.Get(_share));
        cache.Remember(_share, "fresh");           // and it recovers
        Assert.Equal("fresh", cache.Get(_share));
    }

    [Fact]
    public void Cache_UnwritableLocation_DoesNotThrow()
    {
        var blocker = Path.Combine(_kit.NewDir("blocker"), "notadir");
        File.WriteAllText(blocker, "x");
        var logged = new List<string>();
        var cache = new LicenseCache(blocker, logged.Add);

        cache.Remember(_share, "abc");

        Assert.Null(cache.Get(_share));
        Assert.NotEmpty(logged);
    }

    [Fact]
    public void Cache_KeepsAtMost20_DroppingOldest()
    {
        var cache = new LicenseCache(_appData);
        var root = _kit.NewDir("many");
        for (var i = 0; i < 21; i++)
            cache.Remember(Path.Combine(root, $"f{i}"), $"text{i}");

        Assert.Null(cache.Get(Path.Combine(root, "f0")));
        for (var i = 1; i < 21; i++)
            Assert.Equal($"text{i}", cache.Get(Path.Combine(root, $"f{i}")));
    }

    // ── Manager + cache ───────────────────────────────────────────────────────

    [Fact]
    public void Manager_LicensedRead_RemembersIt()
    {
        var text = _kit.SignedLicenseText(null);
        LicenseTestKit.WriteLicense(_share, text);

        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);
        Assert.Equal(AccessReason.Licensed, manager.EvaluateAccess(_share).Reason);

        Assert.Equal(text, cache.Get(_share));
    }

    [Fact]
    public void Manager_ExpiredRead_RemembersIt_And_NotFoundForgets()
    {
        var text = _kit.SignedLicenseText("2026-01-01");
        LicenseTestKit.WriteLicense(_share, text);
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);

        Assert.Equal(AccessReason.Expired, manager.EvaluateAccess(_share).Reason);
        Assert.Equal(text, cache.Get(_share));

        File.Delete(Path.Combine(_share, "termpoint.lic"));
        manager.EvaluateAccess(_share);
        Assert.Null(cache.Get(_share));
    }

    [Fact]
    public void Manager_Unreadable_WithCachedLicence_IsLicensed()
    {
        var text = _kit.SignedLicenseText(null);
        LicenseTestKit.WriteLicense(_share, text);
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);
        cache.Remember(_share, text);

        using var hold = new FileStream(Path.Combine(_share, "termpoint.lic"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = manager.EvaluateAccess(_share);

        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public void Manager_Unreadable_WithCachedExpiredLicence_IsExpired()
    {
        var text = _kit.SignedLicenseText("2026-01-01");
        LicenseTestKit.WriteLicense(_share, text);
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);
        cache.Remember(_share, text);

        using var hold = new FileStream(Path.Combine(_share, "termpoint.lic"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = manager.EvaluateAccess(_share);

        Assert.Equal(AccessReason.Expired, result.Reason);
        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
    }

    [Fact]
    public void Manager_Unreadable_WithoutCache_FallsToTrial()
    {
        LicenseTestKit.WriteLicense(_share, _kit.SignedLicenseText(null));
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out _);

        using var hold = new FileStream(Path.Combine(_share, "termpoint.lic"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = manager.EvaluateAccess(_share);

        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(30, result.DaysRemaining);
    }

    [Fact]
    public void Manager_WithoutLicense_UsesCache_When_Present_ElseTrial()
    {
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);

        Assert.Equal(AccessReason.Trial, manager.EvaluateAccessWithoutLicense(_share).Reason);

        cache.Remember(_share, _kit.SignedLicenseText(null));
        var withCache = manager.EvaluateAccessWithoutLicense(_share);
        Assert.Equal(AccessReason.Licensed, withCache.Reason);
        Assert.Equal("Geography", withCache.DepartmentName);
    }

    [Fact]
    public void Manager_TamperedCacheText_FallsThroughToTrial()
    {
        var manager = _kit.Manager(_appData, ClockOn(2027, 1, 1), out var cache);
        var text = _kit.SignedLicenseText(null);
        // Flip the payload so the signature no longer matches.
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var payloadIndex = Array.FindIndex(lines, l => l.Length > 20 && !l.StartsWith('#'));
        lines[payloadIndex] = lines[payloadIndex][..^6] + (lines[payloadIndex].EndsWith("AAAA==") ? "BBBB==" : "AAAA==");
        cache.Remember(_share, string.Join('\n', lines));

        var result = manager.EvaluateAccessWithoutLicense(_share);

        Assert.Equal(AccessReason.Trial, result.Reason);
    }
}
