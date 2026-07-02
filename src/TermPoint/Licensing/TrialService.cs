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

    /// <summary>
    /// Creates a new trial service.
    /// </summary>
    /// <param name="appDataDirectory">
    /// Directory where <c>trial.json</c> is stored (e.g. <c>%APPDATA%\TermPoint</c>).
    /// </param>
    /// <param name="clock">Clock for date math. Use <see cref="SystemClock"/> in production.</param>
    public TrialService(string appDataDirectory, IClock clock)
    {
        _trialFilePath = Path.Combine(appDataDirectory, TrialFileName);
        _clock = clock;
    }

    /// <inheritdoc />
    public TrialResult GetTrialStatus()
    {
        var startDate = ReadTrialStart();

        if (startDate == null)
        {
            startDate = _clock.UtcNow;
            WriteTrialStart(startDate.Value);
        }

        var elapsed = (_clock.UtcNow - startDate.Value).TotalDays;
        var elapsedDays = (int)Math.Floor(elapsed);

        if (elapsedDays >= TrialDays)
        {
            return new TrialResult
            {
                IsInTrial = false,
                DaysRemaining = 0,
                TrialExpired = true
            };
        }

        return new TrialResult
        {
            IsInTrial = true,
            DaysRemaining = TrialDays - elapsedDays,
            TrialExpired = false
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

            if (DateTime.TryParse(data.TrialStartedUtc, out var parsed))
                return parsed.ToUniversalTime();

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the trial start timestamp to <c>trial.json</c>.
    /// </summary>
    private void WriteTrialStart(DateTime startUtc)
    {
        var data = new TrialData
        {
            TrialStartedUtc = startUtc.ToString("o"),
            Version = 1
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_trialFilePath)!);
        File.WriteAllText(_trialFilePath, JsonSerializer.Serialize(data, JsonOptions));
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
