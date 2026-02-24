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
}
