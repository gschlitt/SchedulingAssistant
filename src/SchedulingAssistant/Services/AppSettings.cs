using System.Text.Json;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Persists app-level settings (e.g. database path) in a small JSON file
/// in a stable AppData location the app can always find on startup.
/// Use <see cref="Current"/> for all normal access — it loads once and caches the result
/// in memory so subsequent reads cost nothing. Only call <see cref="Load"/> when a
/// forced re-read from disk is explicitly required.
/// </summary>
public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TermPoint");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    /// <summary>
    /// In-memory singleton. Populated on first access or by an explicit <see cref="Load"/> call.
    /// Mutations made to the returned instance are reflected in all subsequent <see cref="Current"/>
    /// accesses until the app restarts, so callers only need to call <see cref="Save"/> after mutating.
    /// </summary>
    private static AppSettings? _instance;

    /// <summary>
    /// Returns the cached <see cref="AppSettings"/> instance, loading from disk on first access.
    /// This is the preferred accessor — it reads the file at most once per app session.
    /// </summary>
    public static AppSettings Current => _instance ??= Load();

    /// <summary>
    /// True after the first-run wizard has successfully created a database.
    /// When false, the app routes to the startup wizard instead of the main window.
    /// </summary>
    public bool IsInitialSetupComplete { get; set; } = false;

    /// <summary>Name of the institution (e.g. "Greendale Community College").</summary>
    public string InstitutionName { get; set; } = string.Empty;

    /// <summary>Short abbreviation for the institution (e.g. "GCC").</summary>
    public string InstitutionAbbrev { get; set; } = string.Empty;

    public string? DatabasePath { get; set; }
    public bool IncludeSaturday { get; set; } = false;
    public bool IncludeSunday { get; set; } = false;
    public double? PreferredBlockLength { get; set; } = null;
    public bool ShowOnlyActiveInstructors { get; set; } = true;

    /// <summary>
    /// When true, only active courses are shown in the Courses management flyout.
    /// Inactive courses are always hidden from the section editor and grid filter
    /// regardless of this setting.
    /// </summary>
    public bool ShowOnlyActiveCourses { get; set; } = true;

    public SectionSortMode SectionSortMode { get; set; } = SectionSortMode.SubjectCourseCode;
    public InstructorSortMode InstructorSortMode { get; set; } = InstructorSortMode.LastName;

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
    /// Folder path where automated backups are written.
    /// Null means no backup folder has been configured and backups are skipped.
    /// </summary>
    public string? BackupFolderPath { get; set; }

    /// <summary>
    /// Interval in minutes between periodic backups during an active write session.
    /// Default: 30. Must be at least 1.
    /// </summary>
    public int BackupIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of backup files to retain per database.
    /// When exceeded, the oldest backup pair (.db + _sections.csv) is deleted.
    /// Default: 5.
    /// </summary>
    public int MaxBackupCount { get; set; } = 5;

    /// <summary>
    /// The highest application version whose feature announcements the user has
    /// acknowledged. <see cref="AppNotificationService.EnqueueUnseenAnnouncements"/>
    /// compares this against <see cref="AppAnnouncements.All"/> to decide which
    /// announcements to show, and updates it after enqueueing. Null on a fresh install.
    /// </summary>
    public string? LastAcknowledgedVersion { get; set; }

    /// <summary>
    /// When true, the app automatically saves D' to D on a timer while in write mode.
    /// The interval is controlled by <see cref="AutoSaveIntervalMinutes"/>.
    /// </summary>
    public bool AutoSaveEnabled { get; set; } = false;

    /// <summary>
    /// Interval in minutes between automatic saves when <see cref="AutoSaveEnabled"/> is true.
    /// Minimum effective value is 1.
    /// </summary>
    public int AutoSaveIntervalMinutes { get; set; } = 10;

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

    /// <summary>
    /// Reads the settings JSON file from disk and caches the result in <see cref="Current"/>.
    /// Prefer <see cref="Current"/> for routine reads; call this only when a forced re-read is needed.
    /// Returns a default <see cref="AppSettings"/> if the file does not exist or cannot be parsed.
    /// </summary>
    public static AppSettings Load()
    {
        AppSettings result;
        if (!File.Exists(SettingsPath))
        {
            result = new AppSettings();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(SettingsPath);
                result = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                result = new AppSettings();
            }
        }

        _instance = result;
        return result;
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
