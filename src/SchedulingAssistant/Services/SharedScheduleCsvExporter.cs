using System.Text;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Exports sections visible in the current filter state as a shared schedule CSV.
/// Stateless — all data passed in as parameters.
/// </summary>
public class SharedScheduleCsvExporter
{
    private static readonly Dictionary<int, string> DayNames = new()
    {
        [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday",
        [4] = "Thursday", [5] = "Friday", [6] = "Saturday", [7] = "Sunday"
    };

    /// <summary>
    /// Writes a shared schedule CSV to the given stream.
    /// </summary>
    /// <param name="output">Target stream (caller is responsible for closing).</param>
    /// <param name="sourceLabel">Source label for the header comment (e.g. institution name).</param>
    /// <param name="sections">Sections to export (already filtered by caller).</param>
    /// <param name="courseCodeLookup">Resolves CourseId → display course code.</param>
    /// <returns>Null on success, or an error message string on failure.</returns>
    public string? Export(Stream output, string sourceLabel, IReadOnlyList<Section> sections,
                          Func<string, string> courseCodeLookup)
    {
        try
        {
            ExportCore(output, sourceLabel, sections, courseCodeLookup);
            return null;
        }
        catch (Exception)
        {
            return "Export failed — unable to write file.";
        }
    }

    private void ExportCore(Stream output, string sourceLabel, IReadOnlyList<Section> sections,
                            Func<string, string> courseCodeLookup)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        // Header comment
        writer.WriteLine($"#TermPoint Schedule Overlay,{sourceLabel},{DateTime.Today:yyyy-MM-dd}");

        // Column header
        writer.WriteLine("CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency");

        // Build sorted export rows
        var exportRows = BuildExportRows(sections, courseCodeLookup);
        foreach (var row in exportRows)
            writer.WriteLine(row);
    }

    private List<string> BuildExportRows(IReadOnlyList<Section> sections, Func<string, string> courseCodeLookup)
    {
        var rows = new List<string>();

        // Group by CourseCode + SectionCode, sorted alphabetically
        var ordered = sections
            .Select(s => (Section: s, CourseCode: courseCodeLookup(s.CourseId ?? "")))
            .OrderBy(x => x.CourseCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Section.SectionCode, StringComparer.OrdinalIgnoreCase);

        foreach (var (section, courseCode) in ordered)
        {
            if (section.Schedule.Count == 0)
            {
                // Unscheduled section — one row with blank time fields
                rows.Add(FormatRow(courseCode, section.SectionCode, section.Notes, "", "", "", "", "", ""));
            }
            else
            {
                // One row per meeting, sorted by Day then StartMinutes
                var meetings = section.Schedule
                    .OrderBy(m => m.Day)
                    .ThenBy(m => m.StartMinutes);

                foreach (var mtg in meetings)
                {
                    var dayName = DayNames.GetValueOrDefault(mtg.Day, "");
                    var startTime = FormatTime(mtg.StartMinutes);
                    var endTime = FormatTime(mtg.EndMinutes);

                    rows.Add(FormatRow(
                        courseCode, section.SectionCode, section.Notes,
                        dayName, startTime, endTime,
                        mtg.DurationMinutes.ToString(), mtg.StartMinutes.ToString(),
                        mtg.Frequency ?? ""));
                }
            }
        }

        return rows;
    }

    private static string FormatRow(string courseCode, string sectionCode, string notes,
                                     string day, string startTime, string endTime,
                                     string durationMin, string startMinutes, string frequency)
    {
        return $"{CsvEscape(courseCode)},{CsvEscape(sectionCode)},{CsvEscape(notes)}," +
               $"{CsvEscape(day)},{CsvEscape(startTime)},{CsvEscape(endTime)}," +
               $"{CsvEscape(durationMin)},{CsvEscape(startMinutes)},{CsvEscape(frequency)}";
    }

    /// <summary>
    /// RFC-4180 escaping: wraps the field in quotes if it contains a comma, newline, or quote.
    /// Embedded quotes are doubled.
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>Formats minutes-from-midnight as "h:mm tt" (e.g. 480 → "8:00 AM").</summary>
    private static string FormatTime(int minutes)
    {
        int hours = minutes / 60;
        int mins = minutes % 60;
        var period = hours >= 12 ? "PM" : "AM";
        var displayHour = hours % 12;
        if (displayHour == 0) displayHour = 12;
        return $"{displayHour}:{mins:D2} {period}";
    }
}
