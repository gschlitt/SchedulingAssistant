using System.Globalization;
using System.Text.Json;

namespace TermPoint.Licensing;

/// <summary>
/// Manages a per-user, per-installation trial clock.
/// Trial state is stored as <c>trial.json</c> in the given AppData directory.
/// The trial lasts 30 days from the first launch that creates the file.
/// </summary>
public class TrialService : ITrialService
{
    private const int TrialDays = 30;
    private const string TrialFileName = "trial.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _trialFilePath;
    private readonly IClock _clock;
    private readonly Action<string>? _log;

    /// <summary>
    /// Creates a new trial service.
    /// </summary>
    /// <param name="appDataDirectory">
    /// Directory where <c>trial.json</c> is stored (e.g. <c>%APPDATA%\TermPoint</c>).
    /// </param>
    /// <param name="clock">Clock for date math. Use <see cref="SystemClock"/> in production.</param>
    /// <param name="log">Optional sink for diagnostic lines (reset start dates, failed writes). Null = silent.</param>
    public TrialService(string appDataDirectory, IClock clock, Action<string>? log = null)
    {
        _trialFilePath = Path.Combine(appDataDirectory, TrialFileName);
        _clock = clock;
        _log = log;
    }

    /// <inheritdoc />
    public TrialResult GetTrialStatus()
    {
        var now = _clock.UtcNow;
        var startDate = ReadTrialStart();

        if (startDate == null)
        {
            // First launch (or unreadable file): the trial starts now.
            startDate = now;
            TryWriteTrialStart(startDate.Value);
        }
        else if (startDate.Value > now)
        {
            // A start date in the future means the clock was wrong when the file was written.
            // Restart from today so the user gets 30 days, never more.
            _log?.Invoke($"[Licensing] Trial start {startDate.Value:o} is in the future; resetting it to now");
            startDate = now;
            TryWriteTrialStart(startDate.Value);
        }

        var endsUtc = startDate.Value.AddDays(TrialDays);
        var elapsedDays = (int)Math.Floor((now - startDate.Value).TotalDays);

        if (elapsedDays >= TrialDays)
        {
            return new TrialResult
            {
                IsInTrial = false,
                DaysRemaining = 0,
                TrialExpired = true,
                TrialEndsUtc = endsUtc
            };
        }

        return new TrialResult
        {
            IsInTrial = true,
            DaysRemaining = Math.Min(TrialDays, TrialDays - elapsedDays),
            TrialExpired = false,
            TrialEndsUtc = endsUtc
        };
    }

    /// <summary>
    /// Reads the trial start date from <c>trial.json</c>.
    /// Returns null if the file doesn't exist or is corrupt.
    /// </summary>
    private DateTime? ReadTrialStart()
    {
        if (!File.Exists(_trialFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(_trialFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var data = JsonSerializer.Deserialize<TrialData>(json, JsonOptions);
            if (data?.TrialStartedUtc == null)
                return null;

            if (DateTime.TryParse(data.TrialStartedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToUniversalTime();

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the trial start timestamp to <c>trial.json</c>. A failed write is logged and swallowed: the
    /// caller still reports the trial as started now, so opening a database never fails because of it.
    /// </summary>
    /// <param name="startUtc">The trial start to save.</param>
    private void TryWriteTrialStart(DateTime startUtc)
    {
        try
        {
            var data = new TrialData
            {
                TrialStartedUtc = startUtc.ToString("o", CultureInfo.InvariantCulture),
                Version = 1
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_trialFilePath)!);
            File.WriteAllText(_trialFilePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Licensing] Could not save trial.json ({ex.GetType().Name}: {ex.Message}); trial treated as starting now");
        }
    }

    /// <summary>
    /// Shape of the <c>trial.json</c> file.
    /// </summary>
    private class TrialData
    {
        public string? TrialStartedUtc { get; set; }
        public int Version { get; set; }
    }
}
