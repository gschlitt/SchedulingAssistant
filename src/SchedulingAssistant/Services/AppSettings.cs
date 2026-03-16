using System.Text.Json;
using SchedulingAssistant.Models;

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
    public bool ShowOnlyActiveInstructors { get; set; } = true;
    public SectionSortMode SectionSortMode { get; set; } = SectionSortMode.SubjectCourseCode;

    /// <summary>Last path used for PNG schedule export.</summary>
    public string? LastExportPath { get; set; }

    /// <summary>Last path used for workload report CSV export.</summary>
    public string? LastWorkloadReportPath { get; set; }

    /// <summary>
    /// The academic year ID that was selected when the app was last closed.
    /// Restored on startup so the user returns to where they left off.
    /// </summary>
    public string? LastSelectedAcademicYearId { get; set; }

    /// <summary>
    /// The semester IDs that were selected when the app was last closed.
    /// Supports multi-semester view restoration. Restored on startup.
    /// </summary>
    public List<string> LastSelectedSemesterIds { get; set; } = new();

    /// <summary>Recently opened database paths (most recent first). Max 10 entries.</summary>
    public List<string> RecentDatabases { get; set; } = new();

    /// <summary>
    /// Email subject template for the Workload Mailer.
    /// Supports placeholders: {FirstName}, {LastName}, {AcademicYear}, {Semester}.
    /// </summary>
    public string WorkloadMailerSubject { get; set; } = "Your Workload — {AcademicYear} {Semester}";

    /// <summary>
    /// Email body template for the Workload Mailer.
    /// Supports placeholders: {FirstName}, {LastName}, {AcademicYear}, {Semester}, {Workload}.
    /// </summary>
    public string WorkloadMailerBody { get; set; } =
        "Dear {FirstName},\n\nHere is your workload assignment for {Semester}.\n\n{Workload}\n\nPlease let us know if you have any questions.";

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
