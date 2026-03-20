using System.Text;
using Microsoft.Data.Sqlite;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Services;

/// <summary>
/// Manages automated SQLite backups and companion section CSV exports.
///
/// <para><b>Backup file naming:</b> <c>{dbName}_{yyyy-MM-dd_HH-mm-ss}.db</c> and
/// <c>{dbName}_{yyyy-MM-dd_HH-mm-ss}_sections.csv</c>, all written to
/// <see cref="AppSettings.BackupFolderPath"/>. Using the database filename as a prefix
/// keeps backups from multiple databases cleanly separated within the same folder.</para>
///
/// <para><b>Session lifecycle:</b> Call <see cref="StartSessionAsync"/> when the user
/// acquires the write lock. The service performs an immediate backup and then starts a
/// periodic timer (interval: <see cref="AppSettings.BackupIntervalMinutes"/>). Call
/// <see cref="StopSession"/> (or <see cref="Dispose"/>) to stop the timer on shutdown
/// or database switch.</para>
///
/// <para><b>Rotation:</b> After every backup, the oldest backup pairs are deleted until
/// only <see cref="AppSettings.MaxBackupCount"/> remain.</para>
///
/// <para><b>Integrity check:</b> <see cref="CheckIntegrity"/> is a static method that
/// opens a raw connection (without touching the app's live connection) and runs
/// <c>PRAGMA integrity_check</c>. It can be called before <see cref="StartSessionAsync"/>
/// to verify the main database before any writes occur.</para>
/// </summary>
public class BackupService : IDisposable
{
    private readonly IDatabaseContext _db;
    private readonly SemesterContext _semesterContext;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly ISectionPropertyRepository _propertyRepo;
    private readonly IAppLogger _logger;

    private Timer? _periodicTimer;
    private string? _dbName;   // filename-without-extension prefix for backup filenames
    private bool _disposed;

    /// <summary>
    /// Result of the most recent backup attempt, or null if no backup has been attempted
    /// in this session. Updated after every call to <see cref="PerformBackupAsync"/>.
    /// </summary>
    public BackupResult? LastBackupResult { get; private set; }

    /// <summary>
    /// Fired on the UI thread after every backup attempt (success or failure), so that
    /// the Settings view can refresh its status line and backup list without polling.
    /// </summary>
    public event EventHandler? BackupCompleted;

    /// <param name="db">Live database context — used for VACUUM INTO.</param>
    /// <param name="semesterContext">Provides the currently selected academic year for CSV scope.</param>
    /// <param name="sectionRepo">Section data for CSV export.</param>
    /// <param name="courseRepo">Course names for CSV export.</param>
    /// <param name="instructorRepo">Instructor names for CSV export.</param>
    /// <param name="roomRepo">Room names for CSV export.</param>
    /// <param name="semesterRepo">Semester names for CSV export.</param>
    /// <param name="propertyRepo">Section property values (section type, campus) for CSV export.</param>
    /// <param name="logger">App-wide logger.</param>
    public BackupService(
        IDatabaseContext db,
        SemesterContext semesterContext,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ISemesterRepository semesterRepo,
        ISectionPropertyRepository propertyRepo,
        IAppLogger logger)
    {
        _db             = db;
        _semesterContext = semesterContext;
        _sectionRepo    = sectionRepo;
        _courseRepo     = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo       = roomRepo;
        _semesterRepo   = semesterRepo;
        _propertyRepo   = propertyRepo;
        _logger         = logger;
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Called when the user acquires the write lock. Performs an immediate backup and
    /// starts the periodic backup timer.
    /// </summary>
    /// <param name="dbPath">Full path to the active database file. Used to derive the backup filename prefix.</param>
    public async Task StartSessionAsync(string dbPath)
    {
        _dbName = Path.GetFileNameWithoutExtension(dbPath);
        await PerformBackupAsync();
        StartPeriodicTimer();
    }

    /// <summary>
    /// Converts an academic year name (e.g. "2024-2025" or "Fall 2024") into a string
    /// that is safe to embed in a filename. Spaces become underscores; any character that
    /// is not alphanumeric, a hyphen, or an underscore is removed.
    /// </summary>
    /// <param name="name">Raw academic year name from the data model.</param>
    /// <returns>Filesystem-safe slug, e.g. "2024-2025" or "Fall_2024".</returns>
    private static string ToFileSlug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-') sb.Append(ch);
            else if (ch == ' ')                         sb.Append('_');
            // all other chars silently dropped
        }
        return sb.Length > 0 ? sb.ToString() : "Unknown";
    }

    /// <summary>
    /// Stops the periodic backup timer without performing a final backup.
    /// Safe to call multiple times. Also called by <see cref="Dispose"/>.
    /// </summary>
    public void StopSession()
    {
        _periodicTimer?.Dispose();
        _periodicTimer = null;
    }

    // ── Core backup logic ────────────────────────────────────────────────────

    /// <summary>
    /// Performs one complete backup cycle:
    /// <list type="number">
    ///   <item>VACUUM INTO — creates a compacted snapshot of the live database.</item>
    ///   <item>Integrity check — verifies the backup file is structurally sound.</item>
    ///   <item>CSV export — writes a human-readable section dump alongside the .db file.</item>
    ///   <item>Rotation — deletes the oldest backup pair(s) to stay within <see cref="AppSettings.MaxBackupCount"/>.</item>
    /// </list>
    /// Failures are logged and surfaced via <see cref="LastBackupResult"/>; they do not throw.
    /// </summary>
    /// <returns>A <see cref="BackupResult"/> describing the outcome.</returns>
    public async Task<BackupResult> PerformBackupAsync()
    {
        var settings = AppSettings.Current;
        var folder   = settings.BackupFolderPath;

        if (string.IsNullOrWhiteSpace(folder))
        {
            var r = BackupResult.NoFolderConfigured();
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }

        if (!Directory.Exists(folder))
        {
            var r = BackupResult.FolderUnavailable(folder);
            _logger.LogError(null, $"Backup folder unavailable: {folder}");
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }

        var prefix    = _dbName ?? "backup";
        var aySlug    = ToFileSlug(_semesterContext.SelectedAcademicYear?.Name ?? "Unknown");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var dbDest    = Path.Combine(folder, $"{prefix}_{aySlug}_{timestamp}.db");
        var csvDest   = Path.Combine(folder, $"{prefix}_{aySlug}_{timestamp}_sections.csv");

        // Step 1 — VACUUM INTO
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            var p = cmd.CreateParameter();
            p.ParameterName = "$path";
            p.Value = dbDest;
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VACUUM INTO failed during backup");
            var r = BackupResult.Failed($"Database backup failed: {ex.Message}");
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }

        // Step 2 — integrity check on the backup (non-fatal if it fails)
        bool integrityOk = CheckIntegrityOnFile(dbDest);
        if (!integrityOk)
            _logger.LogError(null, $"Integrity check failed on backup file: {dbDest}");

        // Step 3 — CSV export (non-fatal; DB backup already succeeded)
        bool csvOk = false;
        try
        {
            await WriteSectionsCsvAsync(csvDest);
            csvOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Section CSV export failed during backup");
        }

        // Step 4 — rotate old backups (per academic year, so older years are never evicted)
        try
        {
            RotateBackups(folder, prefix, aySlug, settings.MaxBackupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup rotation failed");
        }

        var result = new BackupResult
        {
            Success         = true,
            Timestamp       = DateTime.Now,
            DbPath          = dbDest,
            CsvPath         = csvOk ? csvDest : null,
            IntegrityPassed = integrityOk
        };

        LastBackupResult = result;
        FireBackupCompleted();
        return result;
    }

    // ── Backup list ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all backup entries for the current database, newest first.
    /// Scans <see cref="AppSettings.BackupFolderPath"/> for files matching
    /// <c>{dbName}_*.db</c>. Returns an empty list when no folder is configured
    /// or the folder is unavailable.
    /// </summary>
    public List<BackupEntry> GetBackups()
    {
        var folder = AppSettings.Current.BackupFolderPath;
        var prefix = _dbName
            ?? Path.GetFileNameWithoutExtension(AppSettings.Current.DatabasePath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return new List<BackupEntry>();

        return Directory
            .GetFiles(folder, $"{prefix}_*.db")
            .OrderByDescending(f => f)
            .Select(f =>
            {
                var csv = Path.ChangeExtension(f, null) + "_sections.csv";
                return new BackupEntry
                {
                    DbPath    = f,
                    CsvPath   = File.Exists(csv) ? csv : null,
                    Timestamp = ParseTimestampFromFilename(f) ?? File.GetCreationTime(f)
                };
            })
            .ToList();
    }

    // ── Static integrity check ───────────────────────────────────────────────

    /// <summary>
    /// Opens a temporary read-only connection to <paramref name="dbPath"/> and runs
    /// <c>PRAGMA integrity_check</c>. Returns true when SQLite reports "ok", false on any
    /// structural problem or if the file cannot be opened. This is a static method so it
    /// can be called during startup before the BackupService session has started.
    /// </summary>
    /// <param name="dbPath">Full path to the SQLite file to check.</param>
    /// <returns>True if the database passes integrity check; false otherwise.</returns>
    public static bool CheckIntegrity(string dbPath)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode       = SqliteOpenMode.ReadOnly
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar() as string;
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c> on an already-existing backup file using a
    /// fresh read-only connection. Delegates to <see cref="CheckIntegrity"/>.
    /// </summary>
    private static bool CheckIntegrityOnFile(string path) => CheckIntegrity(path);

    /// <summary>
    /// Writes a human-readable CSV of all sections in the currently selected academic year.
    /// Columns are grouped: section-level fields first (AcademicYear … Reserves), then
    /// meeting-level fields (Day … MeetingType). For multi-meeting sections the first row
    /// carries all section-level data; subsequent rows for the same section leave those
    /// columns blank to avoid repetition. Sections with no scheduled meetings still emit
    /// one row (with empty meeting columns) so no section is silently omitted.
    /// All foreign-key IDs are resolved to display names.
    /// </summary>
    /// <param name="destPath">Full path of the CSV file to create (overwrite if exists).</param>
    private async Task WriteSectionsCsvAsync(string destPath)
    {
        // Build lookup dictionaries from the repositories.
        var courses     = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructors = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var rooms       = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var sectionTypes = _propertyRepo.GetAll(SectionPropertyTypes.SectionType)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var campuses     = _propertyRepo.GetAll(SectionPropertyTypes.Campus)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var tags         = _propertyRepo.GetAll(SectionPropertyTypes.Tag)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var meetingTypes = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var resourceProps = _propertyRepo.GetAll(SectionPropertyTypes.Resource)
                                         .ToDictionary(p => p.Id, p => p.Name);
        var reserveProps  = _propertyRepo.GetAll(SectionPropertyTypes.Reserve)
                                         .ToDictionary(p => p.Id, p => p.Name);

        // Resolve the current academic year so we can label each row.
        var ay = _semesterContext.SelectedAcademicYear;
        var ayName = ay?.Name ?? string.Empty;

        // Load all semesters and sections for the current academic year.
        var allSemesters = ay is not null
            ? _semesterRepo.GetAll().Where(s => s.AcademicYearId == ay.Id).ToList()
            : new List<Semester>();

        var sb = new StringBuilder();

        // Header row — section-level columns first, then meeting-level columns.
        AppendCsvRow(sb,
            "AcademicYear", "Semester",
            "CourseCode", "CourseTitle",
            "SectionCode", "Instructors",
            "SectionType", "Campus", "Tags", "Resources", "Reserves",
            "Day", "StartTime", "EndTime", "DurationMin",
            "Room", "Frequency", "MeetingType");

        // Semesters in calendar order (Fall → Winter → Early Summer → Summer → Late Summer).
        // Within each semester, sections sorted by "{CourseCode} {SectionCode}" —
        // the same heading-based alphabetic order used by SectionListViewModel.
        // Materialise to List<Section> immediately so that section.Schedule is
        // stable during the meeting loop (avoids any deferred-execution surprises).
        foreach (var semester in allSemesters.OrderBy(s => s.SortOrder))
        {
            var sections = _sectionRepo.GetAll(semester.Id)
                .OrderBy(s =>
                {
                    var code = s.CourseId is not null && courses.TryGetValue(s.CourseId, out var cx)
                               ? cx.CalendarCode : string.Empty;
                    return $"{code} {s.SectionCode}".Trim();
                }, StringComparer.Ordinal)
                .ToList();

            foreach (var section in sections)
            {
                // Resolve names
                var courseCode  = section.CourseId is not null && courses.TryGetValue(section.CourseId, out var c)
                                  ? c.CalendarCode : string.Empty;
                var courseTitle = section.CourseId is not null && courses.TryGetValue(section.CourseId, out var ct)
                                  ? ct.Title : string.Empty;

                var instructorNames = section.InstructorAssignments
                    .Select(a => instructors.TryGetValue(a.InstructorId, out var ins)
                                 ? $"{ins.FirstName} {ins.LastName}".Trim()
                                 : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var instructorsCsv = string.Join("; ", instructorNames);

                var sectionType = section.SectionTypeId is not null
                                  && sectionTypes.TryGetValue(section.SectionTypeId, out var st)
                                  ? st : string.Empty;
                var campus      = section.CampusId is not null
                                  && campuses.TryGetValue(section.CampusId, out var cp)
                                  ? cp : string.Empty;
                var tagNames    = section.TagIds
                    .Select(id => tags.TryGetValue(id, out var tn) ? tn : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var tagsCsv = string.Join("; ", tagNames);

                var resourceNames = section.ResourceIds
                    .Select(id => resourceProps.TryGetValue(id, out var rn) ? rn : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var resourcesCsv = string.Join("; ", resourceNames);

                // Reserves are displayed as "Name (Code)" pairs, e.g. "EDUC 101 (50); NURS 200 (25)".
                var reserveNames = section.Reserves
                    .Select(r =>
                    {
                        var name = reserveProps.TryGetValue(r.ReserveId, out var rn) ? rn : string.Empty;
                        return name.Length > 0 ? $"{name} ({r.Code})" : string.Empty;
                    })
                    .Where(n => n.Length > 0)
                    .ToList();
                var reservesCsv = string.Join("; ", reserveNames);

                if (section.Schedule.Count == 0)
                {
                    // No scheduled meetings — still emit one row so the section is visible.
                    AppendCsvRow(sb,
                        ayName, semester.Name,
                        courseCode, courseTitle,
                        section.SectionCode, instructorsCsv,
                        sectionType, campus, tagsCsv, resourcesCsv, reservesCsv,
                        "", "", "", "",
                        "", "", "");
                }
                else
                {
                    // First meeting row carries all section-level info.
                    // Subsequent meeting rows for the same section leave section-level columns
                    // blank so the CSV is easier to read — the data isn't repeated unnecessarily.
                    bool firstMeeting = true;
                    foreach (var meeting in section.Schedule)
                    {
                        var dayName     = DayName(meeting.Day);
                        var startStr    = MinutesToTimeString(meeting.StartMinutes);
                        var endStr      = MinutesToTimeString(meeting.EndMinutes);
                        var durStr      = meeting.DurationMinutes.ToString();
                        var roomName    = meeting.RoomId is not null
                                          && rooms.TryGetValue(meeting.RoomId, out var rm)
                                          ? $"{rm.Building} {rm.RoomNumber}".Trim()
                                          : string.Empty;
                        var freq        = SectionDaySchedule.FormatFrequency(meeting.Frequency);
                        var meetingType = meeting.MeetingTypeId is not null
                                          && meetingTypes.TryGetValue(meeting.MeetingTypeId, out var mt)
                                          ? mt : string.Empty;

                        if (firstMeeting)
                        {
                            AppendCsvRow(sb,
                                ayName, semester.Name,
                                courseCode, courseTitle,
                                section.SectionCode, instructorsCsv,
                                sectionType, campus, tagsCsv, resourcesCsv, reservesCsv,
                                dayName, startStr, endStr, durStr,
                                roomName, freq, meetingType);
                            firstMeeting = false;
                        }
                        else
                        {
                            // Continuation row — section-level columns left blank.
                            AppendCsvRow(sb,
                                "", "",
                                "", "",
                                "", "",
                                "", "", "", "", "",
                                dayName, startStr, endStr, durStr,
                                roomName, freq, meetingType);
                        }
                    }
                }
            }
        }

        await File.WriteAllTextAsync(destPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Enforces the backup retention limit for a single academic year. Only files whose
    /// name contains <paramref name="aySlug"/> are considered; backups from other academic
    /// years are never touched. Oldest backup pairs (db + csv) are deleted until the count
    /// for this academic year is at or below <paramref name="maxCount"/>.
    /// </summary>
    /// <param name="folder">Backup folder path.</param>
    /// <param name="prefix">Database name prefix, e.g. "ComputerScience".</param>
    /// <param name="aySlug">Filesystem-safe academic year slug, e.g. "2025-2026".</param>
    /// <param name="maxCount">Maximum number of backup pairs to retain for this academic year.</param>
    private static void RotateBackups(string folder, string prefix, string aySlug, int maxCount)
    {
        // Clamp to a sensible minimum to prevent accidental deletion of everything.
        maxCount = Math.Max(maxCount, 1);

        // Only rotate the files that belong to the current academic year.
        // File pattern: {prefix}_{aySlug}_{yyyy-MM-dd_HH-mm-ss}.db
        var files = Directory.GetFiles(folder, $"{prefix}_{aySlug}_*.db")
                             .OrderBy(f => f)   // ascending = oldest first
                             .ToList();

        while (files.Count > maxCount)
        {
            var oldest = files[0];
            files.RemoveAt(0);

            TryDelete(oldest);

            // Delete companion CSV
            var csv = Path.ChangeExtension(oldest, null) + "_sections.csv";
            TryDelete(csv);
        }
    }

    /// <summary>Starts or restarts the periodic backup timer using the current interval setting.</summary>
    private void StartPeriodicTimer()
    {
        _periodicTimer?.Dispose();

        var intervalMinutes = Math.Max(AppSettings.Current.BackupIntervalMinutes, 1);
        var interval        = TimeSpan.FromMinutes(intervalMinutes);

        _periodicTimer = new Timer(
            _ => _ = PerformBackupAsync(),   // fire-and-forget; errors surfaced via LastBackupResult
            null,
            interval,   // first fire after one interval (we already backed up at session start)
            interval);
    }

    /// <summary>
    /// Fires <see cref="BackupCompleted"/> on the UI thread so subscribers can safely update
    /// observable properties.
    /// </summary>
    private void FireBackupCompleted()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => BackupCompleted?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Tries to parse the timestamp embedded in a backup filename, e.g.
    /// <c>ComputerScience_2026-03-20_14-30-00.db</c> → 2026-03-20 14:30:00.
    /// Returns null when the filename does not match the expected pattern.
    /// </summary>
    private static DateTime? ParseTimestampFromFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        // Expected suffix: _yyyy-MM-dd_HH-mm-ss  (19 chars + 1 underscore = 20)
        if (name.Length < 20) return null;
        var suffix = name[^19..];   // last 19 chars: "yyyy-MM-dd_HH-mm-ss"
        if (DateTime.TryParseExact(suffix, "yyyy-MM-dd_HH-mm-ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    /// <summary>Converts minutes-from-midnight to a display string, e.g. 510 → "8:30 AM".</summary>
    private static string MinutesToTimeString(int totalMinutes)
    {
        var hours   = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var dt      = new DateTime(2000, 1, 1, hours, minutes, 0);
        return dt.ToString("h:mm tt");
    }

    /// <summary>Returns the full English day name for day codes 1–7 (1=Monday … 7=Sunday).</summary>
    private static string DayName(int day) => day switch
    {
        1 => "Monday",
        2 => "Tuesday",
        3 => "Wednesday",
        4 => "Thursday",
        5 => "Friday",
        6 => "Saturday",
        7 => "Sunday",
        _ => day.ToString()
    };

    /// <summary>
    /// Appends one RFC-4180-compliant CSV row to <paramref name="sb"/>.
    /// Values containing commas, double quotes, or newlines are quoted and internal
    /// double quotes are doubled.
    /// </summary>
    private static void AppendCsvRow(StringBuilder sb, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var v = values[i] ?? string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
            {
                sb.Append('"');
                sb.Append(v.Replace("\"", "\"\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(v);
            }
        }
        sb.AppendLine();
    }

    /// <summary>Deletes a file, swallowing any exceptions (e.g. file already gone).</summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
    }
}

// ── Supporting types ─────────────────────────────────────────────────────────

/// <summary>
/// Outcome of a single <see cref="BackupService.PerformBackupAsync"/> call.
/// </summary>
public class BackupResult
{
    /// <summary>True when the .db backup file was written successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When the backup completed. Null when <see cref="Success"/> is false.</summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>Full path of the written .db backup file. Null when <see cref="Success"/> is false.</summary>
    public string? DbPath { get; init; }

    /// <summary>Full path of the written sections CSV. Null when the CSV step failed or was skipped.</summary>
    public string? CsvPath { get; init; }

    /// <summary>
    /// True when <c>PRAGMA integrity_check</c> passed on the backup file.
    /// Only meaningful when <see cref="Success"/> is true.
    /// </summary>
    public bool IntegrityPassed { get; init; }

    /// <summary>
    /// A short one-line summary suitable for display in the Settings view status line.
    /// </summary>
    public string StatusSummary =>
        Success
            ? $"Last backup: {Timestamp:yyyy-MM-dd  HH:mm:ss}" +
              (IntegrityPassed ? "  ✓" : "  ⚠ integrity check failed")
            : $"Backup failed: {ErrorMessage}";

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult NoFolderConfigured() => new()
        { Success = false, ErrorMessage = "No backup folder configured." };

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult FolderUnavailable(string folder) => new()
        { Success = false, ErrorMessage = $"Backup folder not accessible: {folder}" };

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult Failed(string message) => new()
        { Success = false, ErrorMessage = message };
}
