namespace SchedulingAssistant.Models;

/// <summary>
/// Describes a single backup snapshot on disk — a .db file and its companion _sections.csv.
/// Instances are produced by <see cref="Services.BackupService.GetBackups"/> and consumed by
/// the Settings view for display and restore operations.
/// </summary>
public class BackupEntry
{
    /// <summary>Absolute path to the SQLite backup file.</summary>
    public string DbPath { get; init; } = string.Empty;

    /// <summary>
    /// Absolute path to the companion sections CSV, or null when no CSV exists alongside
    /// this backup (e.g. the file was deleted manually).
    /// </summary>
    public string? CsvPath { get; init; }

    /// <summary>Timestamp extracted from the backup filename (or file creation time as fallback).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>True when the companion CSV file is present on disk.</summary>
    public bool HasCsv => CsvPath is not null && File.Exists(CsvPath);

    /// <summary>Human-readable timestamp for display in the UI, e.g. "2026-03-20  14:30:00".</summary>
    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd  HH:mm:ss");

    /// <summary>Filename without extension, e.g. "ComputerScience_2026-03-20_14-30-00".</summary>
    public string DisplayName => Path.GetFileNameWithoutExtension(DbPath);
}
