using System.Text.Json;

namespace SchedulingAssistant.Services;

/// <summary>
/// Manages persistent storage of legal start times configuration.
/// Data is stored in the project directory and travels with the code.
/// </summary>
public static class LegalStartTimesDataStore
{
    /// <summary>
    /// Directory within the project where start times data is stored.
    /// This is relative to the executing assembly location.
    /// </summary>
    private const string DataDirectory = "Data";
    private const string DataFileName = "legal_start_times.json";

    /// <summary>
    /// Get the full path where the exported data is stored.
    /// </summary>
    public static string GetEmbeddedDataPath()
    {
        try
        {
            var assemblyDir = Path.GetDirectoryName(typeof(LegalStartTimesDataStore).Assembly.Location) ?? "./";
            var dataDir = Path.Combine(assemblyDir, DataDirectory);
            return Path.Combine(dataDir, DataFileName);
        }
        catch
        {
            // Fallback to relative path if assembly location can't be determined
            return Path.Combine("Data", DataFileName);
        }
    }

    /// <summary>
    /// Check if persisted start times data exists.
    /// </summary>
    public static bool HasPersistedData()
    {
        return File.Exists(GetEmbeddedDataPath());
    }

    /// <summary>
    /// Load persisted start times data from file.
    /// </summary>
    public static LegalStartTimesDataExporter.ExportData? LoadPersistedData()
    {
        var path = GetEmbeddedDataPath();
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LegalStartTimesDataExporter.ExportData>(json);
        }
        catch (Exception ex)
        {
            App.Logger.LogWarning($"Failed to load persisted start times data: {ex.Message}", "LegalStartTimesDataStore.LoadPersistedData");
            return null;
        }
    }

    /// <summary>
    /// Delete persisted start times data file.
    /// </summary>
    public static void DeletePersistedData()
    {
        try
        {
            var path = GetEmbeddedDataPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            App.Logger.LogWarning($"Failed to delete persisted start times data: {ex.Message}", "LegalStartTimesDataStore.DeletePersistedData");
        }
    }

    /// <summary>
    /// Get a human-readable summary of persisted data (if it exists).
    /// </summary>
    public static string? GetPersistedDataSummary()
    {
        var data = LoadPersistedData();
        if (data?.AcademicYears.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var ay in data!.AcademicYears)
        {
            sb.AppendLine(ay.AcademicYearName);
            foreach (var bl in ay.BlockLengths)
            {
                sb.AppendLine($"  {bl.BlockLengthHours:0.#} hr — {bl.StartTimesDisplay}");
            }
        }
        return sb.ToString().TrimEnd();
    }
}
