using SchedulingAssistant.Models;
using System.Text.Json;

namespace SchedulingAssistant.Services;

/// <summary>
/// Persists app-level settings (e.g. database path) in a small JSON file
/// in a stable AppData location the app can always find on startup.
/// </summary>
public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchedulingAssistant");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    public string? DatabasePath { get; set; }
    public bool IncludeSaturday { get; set; } = false;
    public double? PreferredBlockLength { get; set; } = null;

    /// <summary>Up to two saved favourite meeting-day patterns.</summary>
    public BlockPattern? Pattern1 { get; set; }
    public BlockPattern? Pattern2 { get; set; }

    /// <summary>Recently opened database paths (most recent first). Max 10 entries.</summary>
    public List<string> RecentDatabases { get; set; } = new();

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>
    /// Add a database to the recent list. Moves to front if already present, keeps max 10 entries.
    /// Only adds if the file exists.
    /// </summary>
    public void AddRecentDatabase(string databasePath)
    {
        if (!File.Exists(databasePath)) return;

        // Normalize path for comparison
        var normalized = Path.GetFullPath(databasePath);

        // Remove if already exists
        RecentDatabases.RemoveAll(p => Path.GetFullPath(p).Equals(normalized, StringComparison.OrdinalIgnoreCase));

        // Add to front
        RecentDatabases.Insert(0, normalized);

        // Keep only last 10
        if (RecentDatabases.Count > 10)
            RecentDatabases.RemoveRange(10, RecentDatabases.Count - 10);

        Save();
    }
}
