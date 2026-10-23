using System.Text.Json;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Persists app-level settings (e.g. database path) in a small JSON file
/// in a stable AppData location the app can always find on startup.
/// Use <see cref="Current"/> for all normal access — it loads once and caches the result
/// in memory so subsequent reads cost nothing. Only call <see cref="Load"/> when a
/// forced re-read from disk is explicitly required.
/// </summary>
public class AppSettings
{
    /// <summary>The settings folder every real run uses: <c>%AppData%\TermPoint</c>.</summary>
    private static readonly string DefaultSettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TermPoint");

    /// <summary>
    /// Test-only: when set, the settings file is read from and written to this folder instead of
    /// <see cref="DefaultSettingsDir"/>. The test assembly sets it once, before any test runs, so no test can read or
    /// overwrite the developer's real <c>settings.json</c> (spec parking lot P5). Always null in the app.
    /// </summary>
    internal static string? SettingsDirectoryOverride { get; set; }

    /// <summary>The folder holding the settings file: the test override when set, otherwise the real one.</summary>
    private static string SettingsDir => SettingsDirectoryOverride ?? DefaultSettingsDir;

    /// <summary>Full path of the settings file.</summary>
    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    /// <summary>Full path of the settings file, for tests that inspect or replace it.</summary>
    internal static string SettingsFilePath => SettingsPath;

    /// <summary>
    /// Guards <see cref="_instance"/> initialization and all <see cref="Save"/> /
    /// <see cref="AddRecentDatabase"/> mutations so that concurrent callers (e.g. the
    /// autosave timer and the UI thread) cannot interleave writes to <see cref="RecentDatabases"/>
    /// or produce partially-written settings files. (F15, F12 — data-integrity-agenda 2026-05-04.)
    /// </summary>
    private static readonly object _settingsLock = new object();

    /// <summary>
    /// In-memory singleton. Populated on first access or by an explicit <see cref="Load"/> call.
    /// Mutations made to the returned instance are reflected in all subsequent <see cref="Current"/>
    /// accesses until the app restarts, so callers only need to call <see cref="Save"/> after mutating.
    /// </summary>
    private static AppSettings? _instance;

    /// <summary>
    /// Returns the cached <see cref="AppSettings"/> instance, loading from disk on first access.
    /// This is the preferred accessor — it reads the file at most once per app session.
    /// Uses double-checked locking so the common (already-loaded) path is lock-free. (F15.)
    /// </summary>
    public static AppSettings Current
    {
        get
        {
            if (_instance is not null) return _instance;
            lock (_settingsLock) { return _instance ??= Load(); }
        }
    }

    /// <summary>
    /// Raised, on the calling (UI) thread, after a setting that live views react to has been changed
    /// and saved; the argument is the setting's property name (for example
    /// <c>nameof(VirtualizeSectionList)</c>). Most settings are simply re-read when used and never
    /// raise it. Static, so it survives <see cref="Load"/> replacing the cached instance; a
    /// subscriber must unsubscribe when it is disposed.
    /// </summary>
    public static event Action<string>? SettingChanged;

    /// <summary>
    /// Raises <see cref="SettingChanged"/>. Call it after assigning the new value and saving.
    /// </summary>
    /// <param name="settingName">The changed property's name.</param>
    internal static void RaiseSettingChanged(string settingName) => SettingChanged?.Invoke(settingName);

    /// <summary>
    /// Anonymous install identifier, generated once on first launch and persisted.
    /// Sent to BugSnag as the user ID so error reports can be grouped by install
    /// without identifying the person. Never displayed in-app or transmitted anywhere
    /// other than BugSnag error reports.
    /// </summary>
    public string InstallId { get; set; } = string.Empty;

    /// <summary>
    /// True after the first-run wizard has successfully created a database.
    /// When false, the app routes to the startup wizard instead of the main window.
    /// </summary>
    public bool IsInitialSetupComplete { get; set; } = false;

    /// <summary>
    /// When true, the app skips the database chooser at startup and opens the most
    /// recently used database directly. When false, the chooser is shown so the user
    /// can pick from recent databases, open an existing file, or create a new one.
    /// Togglable in the chooser itself and under Configuration &gt; Preferences.
    /// Default: false (show the chooser).
    /// </summary>
    public bool AlwaysOpenMostRecentDatabase { get; set; } = false;

    /// <summary>Name of the institution (e.g. "Greendale Community College").</summary>
    public string InstitutionName { get; set; } = string.Empty;

    /// <summary>Short abbreviation for the institution (e.g. "GCC").</summary>
    public string InstitutionAbbrev { get; set; } = string.Empty;

    public string? DatabasePath { get; set; }
    public bool IncludeSaturday { get; set; } = false;
    public bool IncludeSunday { get; set; } = false;
    public double? PreferredBlockLength { get; set; } = null;

    /// <summary>
    /// Controls whether block lengths are entered and displayed in hours or minutes throughout the app.
    /// This is a display-only preference; storage is always in hours.
    /// Default: Hours.
    /// </summary>
    public BlockLengthUnit BlockLengthUnit { get; set; } = BlockLengthUnit.Hours;

    public bool ShowOnlyActiveInstructors { get; set; } = true;

    /// <summary>
    /// Default seating capacity applied to a new section when its course has no capacity of its own.
    /// Null means no default — such sections start with unspecified (null) capacity.
    /// </summary>
    public int? DefaultSectionCapacity { get; set; }

    /// <summary>
    /// When true, only active courses are shown in the Courses management flyout.
    /// Inactive courses are always hidden from the section editor and grid filter
    /// regardless of this setting.
    /// </summary>
    public bool ShowOnlyActiveCourses { get; set; } = true;

    public SectionSortMode SectionSortMode { get; set; } = SectionSortMode.SubjectCourseCode;
    public InstructorSortMode InstructorSortMode { get; set; } = InstructorSortMode.LastName;

    /// <summary>
    /// When true, all section cards are collapsed whenever a new semester context is loaded
    /// (startup or semester switch). The user may expand them freely afterward; subsequent
    /// same-semester refreshes preserve whatever state the user left them in.
    /// Default: false.
    /// </summary>
    public bool OpenWithAllSectionsCollapsed { get; set; } = false;

    /// <summary>
    /// Opt-in (spec_version_1.2.3.md, item 23): when true, the Section List builds only the cards
    /// that are on screen (a virtualizing panel plus scroll anchoring), which cuts memory sharply on
    /// large semesters. When false, every card is built (the classic panel, no anchoring).
    /// The value actually in force is this setting AND
    /// <see cref="PlatformCapabilities.SupportsSectionListVirtualization"/>; the browser demo is
    /// always off. Takes effect immediately (the list switches panels) via <see cref="SettingChanged"/>.
    /// A settings file written before this property existed deserializes to the default, and an
    /// older TermPoint ignores the unknown key, so no migration is needed either way.
    /// Default: false.
    /// </summary>
    public bool VirtualizeSectionList { get; set; } = false;

    /// <summary>
    /// Spec item 20 (load by subject / focus): the user's remembered subject focus, one per database, including
    /// All and None. Nothing about a focus is ever written to the database itself.
    /// <para>
    /// <b>The key is the SHARED database's path (D)</b>, normalised with
    /// <see cref="CheckoutService.NormalizeSourcePath"/>, the same key <see cref="DatabaseInstanceGuard"/> uses;
    /// never the path of a local working copy (D' / D''), which is not the database the user thinks of. The same file
    /// reached by a mapped drive and by its UNC path gives two keys, and the second simply starts fresh
    /// (see <see cref="FocusService"/>, "starting value"). Read and written only by <see cref="FocusService"/>.
    /// </para>
    /// <para>
    /// A settings file written before this property existed deserializes to an empty dictionary, and an older
    /// TermPoint ignores the unknown key, so no migration is needed either way. Always empty in the browser demo,
    /// whose focus is never persisted.
    /// </para>
    /// </summary>
    public Dictionary<string, SavedFocus> FocusByDatabase { get; set; } = new();

    /// <summary>
    /// When true, this instance opens as a read-only observer: it never acquires the
    /// write lock (so it can never block a writer), never polls for the lock, and never
    /// offers to take over write access. It opens against a read-only snapshot (D'') and
    /// can still manually Refresh to pull a writer's saved changes.
    /// Changing this value takes effect on the next app start. Default: false.
    /// </summary>
    public bool OpenInReaderMode { get; set; } = false;

    /// <summary>
    /// Default font size for section label text inside schedule grid tiles.
    /// The user may override this per-session via the in-grid selector; this value
    /// is applied on startup. Default: 11.
    /// </summary>
    public double TileFontSize { get; set; } = 11.0;

    /// <summary>
    /// Controls what the Section List does when the Schedule Grid filter passes a section.
    /// Default: Highlight (existing behaviour — matching cards receive a visual tint).
    /// </summary>
    public FilterPassBehavior FilterPassBehavior { get; set; } = FilterPassBehavior.Highlight;

/// <summary>Last path used for PNG schedule export.</summary>
    public string? LastExportPath { get; set; }

    /// <summary>Last path used for workload report CSV export.</summary>
    public string? LastWorkloadReportPath { get; set; }

    /// <summary>Last path used for course history CSV export.</summary>
    public string? LastCourseHistoryExportPath { get; set; }

    /// <summary>
    /// Default folder for shared schedule import/export file dialogs.
    /// Typically a shared network drive (e.g. "\\dept-server\schedules\").
    /// Null when not configured — file dialogs use the OS default location.
    /// </summary>
    public string? SharedScheduleFolder { get; set; }

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
    /// Tour keys that have been completed or dismissed. Used to suppress auto-triggers
    /// on subsequent launches. On WASM, this list resets each session (AppSettings is
    /// in-memory only) so demo tours auto-trigger every visit.
    /// </summary>
    public List<string> CompletedTourKeys { get; set; } = new();

    /// <summary>
    /// When true, the app automatically saves D' to D on a timer while in write mode.
    /// The interval is controlled by <see cref="AutoSaveIntervalMinutes"/>.
    /// </summary>
    public bool AutoSaveEnabled { get; set; } = true;

    /// <summary>
    /// Interval in minutes between automatic saves when <see cref="AutoSaveEnabled"/> is true.
    /// Minimum effective value is 1.
    /// </summary>
    public int AutoSaveIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Whether the Events checkbox in the schedule grid filter bar is checked.
    /// Defaults to true on first startup; persisted thereafter.
    /// </summary>
    public bool ShowMeetingsOnGrid { get; set; } = true;

    /// <summary>
    /// Whether the Workload Mailer panel is shown in the Instructors flyout.
    /// </summary>
    public bool ShowWorkloadMailer { get; set; }

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
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[AppSettings] Could not load settings (using defaults): {ex.Message}");
                result = new AppSettings();
            }
        }

        // Generate a stable anonymous install ID on first launch (or upgrade from
        // a version that didn't have one). Saved immediately so it persists.
        if (string.IsNullOrEmpty(result.InstallId))
        {
            result.InstallId = Guid.NewGuid().ToString("N");
            _instance = result;
            result.Save();
            return result;
        }

        _instance = result;
        return result;
    }

    /// <summary>
    /// Persists this instance to disk using an atomic write-to-temp-then-rename pattern.
    /// Writes to <c>settings.json.tmp</c> first, then renames to <c>settings.json</c>.
    /// If the process crashes between the write and the rename, the original file is intact.
    /// Thread-safe: serialises concurrent callers via <see cref="_settingsLock"/>. (F12, F15.)
    /// Returns false and logs if the write fails; the in-memory state remains correct.
    /// </summary>
    public bool Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        lock (_settingsLock)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, SettingsPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[AppSettings] Save failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Add a database to the recent list. Moves to front if already present, keeps max 10 entries.
    /// Callers invoke this immediately after a successful open, so existence is already proven —
    /// there is deliberately no <c>File.Exists</c> guard here (on a network path that just went
    /// dark it would block the UI thread for the full SMB redirector timeout). Thread-safe:
    /// serialises list mutation and the subsequent <see cref="Save"/> call via
    /// <see cref="_settingsLock"/> so concurrent callers cannot interleave list writes.
    /// (F15, data-integrity-agenda 2026-05-04.)
    /// </summary>
    public void AddRecentDatabase(string databasePath)
    {
        // Normalize path for comparison
        var normalized = Path.GetFullPath(databasePath);

        lock (_settingsLock)
        {
            // Remove if already exists
            RecentDatabases.RemoveAll(p =>
                Path.GetFullPath(p).Equals(normalized, StringComparison.OrdinalIgnoreCase));

            // Add to front
            RecentDatabases.Insert(0, normalized);

            // Keep only last 10
            if (RecentDatabases.Count > 10)
                RecentDatabases.RemoveRange(10, RecentDatabases.Count - 10);

            // Save() acquires _settingsLock — re-entrant on same thread, so no deadlock.
            Save();
        }
    }
}
