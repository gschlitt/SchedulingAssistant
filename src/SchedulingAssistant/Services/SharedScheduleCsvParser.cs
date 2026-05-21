using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Parses a shared schedule CSV file into a <see cref="SharedScheduleSet"/>.
/// Stateless — all results returned via <see cref="ImportResult"/>.
/// Implements the partial-success model: malformed rows are skipped, valid rows imported.
/// </summary>
public class SharedScheduleCsvParser
{
    private const int MaxDataRows = 3000;

    private static readonly Dictionary<string, int> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Monday"] = 1, ["Mon"] = 1, ["Mo"] = 1, ["M"] = 1,
        ["Tuesday"] = 2, ["Tue"] = 2, ["Tu"] = 2, ["T"] = 2,
        ["Wednesday"] = 3, ["Wed"] = 3, ["We"] = 3, ["W"] = 3,
        ["Thursday"] = 4, ["Thu"] = 4, ["Th"] = 4, ["R"] = 4,
        ["Friday"] = 5, ["Fri"] = 5, ["Fr"] = 5, ["F"] = 5,
        ["Saturday"] = 6, ["Sat"] = 6, ["Sa"] = 6,
        ["Sunday"] = 7, ["Sun"] = 7, ["Su"] = 7,
    };

    private static readonly HashSet<string> ExpectedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "CourseCode", "SectionCode", "Notes", "Day", "StartTime",
        "EndTime", "DurationMin", "StartMinutes", "Frequency"
    };

    /// <summary>
    /// Parses the given stream as a shared schedule CSV.
    /// </summary>
    /// <param name="stream">UTF-8 CSV content.</param>
    /// <param name="fallbackSourceLabel">Used as SourceLabel when the header comment is absent.</param>
    public ImportResult Parse(Stream stream, string fallbackSourceLabel)
    {
        try
        {
            return ParseCore(stream, fallbackSourceLabel);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[SharedScheduleCsvParser] Parse failed: {ex.Message}");
            return ImportResult.Failed("Unable to read file.");
        }
    }

    private ImportResult ParseCore(Stream stream, string fallbackSourceLabel)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var firstLine = reader.ReadLine();
        if (firstLine is null)
            return ImportResult.Failed("File is empty.");

        // Strip BOM if present
        if (firstLine.Length > 0 && firstLine[0] == '﻿')
            firstLine = firstLine[1..];

        // Detect header comment
        string sourceLabel = fallbackSourceLabel;
        DateTime? exportedAt = null;
        string? columnHeaderLine;

        if (firstLine.StartsWith('#'))
        {
            ParseHeaderComment(firstLine, ref sourceLabel, ref exportedAt);
            columnHeaderLine = reader.ReadLine();
        }
        else
        {
            columnHeaderLine = firstLine;
        }

        if (columnHeaderLine is null)
            return ImportResult.Failed("File contains no data.");

        // Validate column headers
        var columnIndex = ParseColumnHeaders(columnHeaderLine);
        if (columnIndex is null)
            return ImportResult.Failed("Column headers do not match the expected shared schedule format.");

        // Read data rows
        var rows = new List<(int lineNumber, string[] fields)>();
        int lineNumber = firstLine.StartsWith('#') ? 3 : 2;
        string? line;
        while ((line = ReadCsvLine(reader)) is not null)
        {
            if (rows.Count >= MaxDataRows)
                return ImportResult.Failed($"File has more than {MaxDataRows} data rows — this may not be a shared schedule file.");

            var fields = ParseCsvRow(line);
            rows.Add((lineNumber, fields));
            lineNumber++;
        }

        if (rows.Count == 0)
            return ImportResult.Failed("File contains no data rows.");

        // Parse rows and group into sections
        var warnings = new List<(int LineNumber, string Reason)>();
        var sectionMap = new Dictionary<string, SharedSection>(StringComparer.OrdinalIgnoreCase);
        int skippedRows = 0;

        foreach (var (ln, fields) in rows)
        {
            var result = ParseDataRow(fields, columnIndex, ln);
            if (result.Error is not null)
            {
                warnings.Add((ln, result.Error));
                skippedRows++;
                continue;
            }

            if (result.Warning is not null)
                warnings.Add((ln, result.Warning));

            var key = $"{result.CourseCode!}\t{result.SectionCode!}";
            if (!sectionMap.TryGetValue(key, out var section))
            {
                section = new SharedSection
                {
                    CourseCode = result.CourseCode!,
                    SectionCode = result.SectionCode!,
                    Notes = result.Notes
                };
                sectionMap[key] = section;
            }

            if (result.Meeting is not null)
            {
                // Ignore unscheduled rows for sections that already have meetings
                section.Meetings.Add(result.Meeting);
            }
        }

        if (sectionMap.Count == 0)
            return ImportResult.Failed("No valid sections found in file.");

        var set = new SharedScheduleSet
        {
            SourceLabel = sourceLabel,
            ExportedAt = exportedAt,
            Sections = sectionMap.Values.ToList()
        };

        return new ImportResult(set, rows.Count, skippedRows, warnings, null);
    }

    private static void ParseHeaderComment(string line, ref string sourceLabel, ref DateTime? exportedAt)
    {
        // Format: #TermPoint Schedule Overlay,<source label>,<ISO date>
        var content = line[1..]; // strip '#'
        var parts = content.Split(',');
        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
            sourceLabel = parts[1].Trim();
        if (parts.Length >= 3 && DateTime.TryParse(parts[2].Trim(), out var dt))
            exportedAt = dt;
    }

    private static Dictionary<string, int>? ParseColumnHeaders(string line)
    {
        var headers = ParseCsvRow(line);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (!string.IsNullOrEmpty(h))
                index[h] = i;
        }

        // Require at minimum: CourseCode, SectionCode, Day, StartMinutes, DurationMin
        if (!index.ContainsKey("CourseCode") || !index.ContainsKey("SectionCode") ||
            !index.ContainsKey("Day") || !index.ContainsKey("StartMinutes") ||
            !index.ContainsKey("DurationMin"))
        {
            // Check if at least the core columns exist (could be a valid file with extra columns)
            int matches = index.Keys.Count(k => ExpectedColumns.Contains(k));
            if (matches < 4)
                return null;
        }

        return index;
    }

    private record RowParseResult(
        string? CourseCode, string? SectionCode, string? Notes,
        SharedMeeting? Meeting, string? Error, string? Warning);

    private static RowParseResult ParseDataRow(string[] fields, Dictionary<string, int> columnIndex, int lineNumber)
    {
        string GetField(string name) =>
            columnIndex.TryGetValue(name, out int idx) && idx < fields.Length ? fields[idx].Trim() : "";

        var courseCode = GetField("CourseCode");
        var sectionCode = GetField("SectionCode");

        if (string.IsNullOrEmpty(courseCode))
            return new RowParseResult(null, null, null, null, "Missing CourseCode", null);
        if (string.IsNullOrEmpty(sectionCode))
            return new RowParseResult(null, null, null, null, "Missing SectionCode", null);

        var notes = GetField("Notes");
        var dayStr = GetField("Day");
        var startStr = GetField("StartMinutes");
        var durationStr = GetField("DurationMin");

        // All time fields blank = unscheduled section (valid)
        if (string.IsNullOrEmpty(dayStr) && string.IsNullOrEmpty(startStr) && string.IsNullOrEmpty(durationStr))
            return new RowParseResult(courseCode, sectionCode, NullIfEmpty(notes), null, null, null);

        // Partial time fields = malformed
        if (string.IsNullOrEmpty(dayStr) || string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(durationStr))
            return new RowParseResult(null, null, null, null, "Partial time fields (Day, StartMinutes, or DurationMin missing)", null);

        // Parse day
        if (!DayMap.TryGetValue(dayStr, out int day))
            return new RowParseResult(null, null, null, null, $"Unrecognized day: '{dayStr}'", null);

        // Parse start minutes
        if (!int.TryParse(startStr, out int startMinutes) || startMinutes < 0 || startMinutes > 1439)
            return new RowParseResult(null, null, null, null, $"Invalid StartMinutes: '{startStr}'", null);

        // Parse duration
        if (!int.TryParse(durationStr, out int duration) || duration <= 0)
            return new RowParseResult(null, null, null, null, $"Invalid DurationMin: '{durationStr}'", null);

        // Parse frequency (optional)
        var freqStr = GetField("Frequency");
        string? frequency = null;
        string? warning = null;

        if (!string.IsNullOrEmpty(freqStr))
        {
            if (IsValidFrequency(freqStr))
                frequency = freqStr;
            else
                warning = $"Invalid frequency '{freqStr}' treated as weekly";
        }

        var meeting = new SharedMeeting
        {
            Day = day,
            StartMinutes = startMinutes,
            DurationMinutes = duration,
            Frequency = frequency
        };

        return new RowParseResult(courseCode, sectionCode, NullIfEmpty(notes), meeting, null, warning);
    }

    private static bool IsValidFrequency(string freq)
    {
        if (freq.Equals("odd", StringComparison.OrdinalIgnoreCase) ||
            freq.Equals("even", StringComparison.OrdinalIgnoreCase))
            return true;

        // Comma-separated integers
        var parts = freq.Split(',');
        return parts.All(p => int.TryParse(p.Trim(), out int n) && n > 0);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// Reads a logical CSV line, handling quoted fields that span multiple physical lines.
    /// Returns null at end of stream.
    /// </summary>
    private static string? ReadCsvLine(StreamReader reader)
    {
        var line = reader.ReadLine();
        if (line is null) return null;

        // Count unescaped quotes — if odd, the field continues on the next line
        while (CountUnescapedQuotes(line) % 2 != 0)
        {
            var next = reader.ReadLine();
            if (next is null) break;
            line = line + "\n" + next;
        }

        return line;
    }

    private static int CountUnescapedQuotes(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                count++;
                // Skip escaped quotes ("")
                if (i + 1 < s.Length && s[i + 1] == '"')
                    i++;
            }
        }
        return count;
    }

    /// <summary>
    /// Parses a single CSV row into fields, respecting RFC-4180 quoted field rules.
    /// </summary>
    internal static string[] ParseCsvRow(string line)
    {
        var fields = new List<string>();
        int i = 0;

        while (i <= line.Length)
        {
            if (i == line.Length)
            {
                fields.Add("");
                break;
            }

            if (line[i] == '"')
            {
                // Quoted field
                var sb = new System.Text.StringBuilder();
                i++; // skip opening quote
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(sb.ToString());
                // Skip comma after closing quote
                if (i < line.Length && line[i] == ',')
                    i++;
            }
            else
            {
                // Unquoted field
                int start = i;
                while (i < line.Length && line[i] != ',')
                    i++;
                fields.Add(line[start..i]);
                if (i < line.Length)
                    i++; // skip comma
            }
        }

        return fields.ToArray();
    }
}

/// <summary>
/// Result of parsing a shared schedule CSV. Either <see cref="FileError"/> is set (entire file rejected)
/// or <see cref="Set"/> is non-null (at least some sections were imported).
/// </summary>
public record ImportResult(
    SharedScheduleSet? Set,
    int TotalRows,
    int SkippedRows,
    List<(int LineNumber, string Reason)> Warnings,
    string? FileError)
{
    /// <summary>Creates a file-level rejection result.</summary>
    public static ImportResult Failed(string error) => new(null, 0, 0, new(), error);
}
