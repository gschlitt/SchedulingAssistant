// ─────────────────────────────────────────────────────────────────────────────
// ONE-TIME MIGRATION UTILITY — DELETE AFTER USE
//
// Converts SSMS CSV exports of old app tables into clean, indented JSON files
// for inspection before the phase-2 conversion to the new SchedulingAssistant
// schema.
//
// Supported tables:
//   XYZ_FLOW_YEARS  — key: AcademicYearKey,  json: JsonData
//   XYZ_UNITS       — key: AcademicUnitName,  json: PermanentProperties
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SchedulingAssistant.Migration;

/// <summary>
/// Converts SSMS CSV table exports into one pretty-printed .json file per row.
///
/// The CSV uses standard RFC-4180 quoting: fields may be wrapped in
/// double-quotes, and embedded double-quotes are escaped as "".
///
/// NOTE: JsonData and PermanentProperties use Json.NET's $id/$ref
/// reference-preservation format. References are preserved as-is in the
/// output — they are NOT resolved here. Phase 2 will use Newtonsoft.Json
/// to resolve them during the actual schema conversion.
/// </summary>
public static class MigrationRunner
{
    // ── Public entry points ────────────────────────────────────────────────────

    /// <summary>
    /// Converts an XYZ_FLOW_YEARS CSV export: one .json file per academic year.
    /// Expected columns: <c>AcademicYearKey</c>, <c>JsonData</c>.
    /// </summary>
    /// <param name="csvPath">Path to the SSMS CSV export.</param>
    /// <param name="outputDir">
    ///   Directory for output files; created automatically if absent.
    /// </param>
    /// <returns>Human-readable summary of files written and any errors.</returns>
    public static Task<string> ConvertYearsCsvToJsonAsync(string csvPath, string outputDir)
        => ConvertCsvToJsonAsync(
            csvPath,
            outputDir,
            keyColumn:  "AcademicYearKey",
            jsonColumn: "JsonData",
            rowLabel:   "year");

    /// <summary>
    /// Converts an XYZ_UNITS CSV export: one .json file per academic unit.
    /// Expected columns: <c>AcademicUnitName</c>, <c>PermanentProperties</c>.
    /// The output filename is derived from <c>AcademicUnitName</c>.
    /// </summary>
    /// <param name="csvPath">Path to the SSMS CSV export.</param>
    /// <param name="outputDir">
    ///   Directory for output files; created automatically if absent.
    /// </param>
    /// <returns>Human-readable summary of files written and any errors.</returns>
    public static Task<string> ConvertUnitsCsvToJsonAsync(string csvPath, string outputDir)
        => ConvertCsvToJsonAsync(
            csvPath,
            outputDir,
            keyColumn:  "AcademicUnitName",
            jsonColumn: "PermanentProperties",
            rowLabel:   "unit");

    // ── Shared core ────────────────────────────────────────────────────────────

    /// <summary>
    /// Core implementation shared by all table converters.
    /// Reads the CSV, locates the key and JSON columns by name, validates
    /// and pretty-prints each JSON value, then writes one file per row.
    /// </summary>
    /// <param name="csvPath">Path to the source CSV file.</param>
    /// <param name="outputDir">Directory to write output files into.</param>
    /// <param name="keyColumn">
    ///   Header name of the column whose value is used as the output filename.
    /// </param>
    /// <param name="jsonColumn">
    ///   Header name of the column that holds the JSON blob to pretty-print.
    /// </param>
    /// <param name="rowLabel">
    ///   Singular noun used in diagnostic messages (e.g. "year", "unit").
    /// </param>
    /// <returns>Human-readable conversion summary.</returns>
    private static async Task<string> ConvertCsvToJsonAsync(
        string csvPath,
        string outputDir,
        string keyColumn,
        string jsonColumn,
        string rowLabel)
    {
        Directory.CreateDirectory(outputDir);

        string[] lines;
        try
        {
            // UTF-8 with automatic BOM detection.
            lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return $"Could not read file: {ex.Message}";
        }

        if (lines.Length == 0)  return "File is empty.";
        if (lines.Length < 2)   return "File has a header row but no data rows.";

        // ── Locate required columns ────────────────────────────────────────────

        var headers = ParseCsvRow(lines[0]);
        int keyCol  = headers.FindIndex(h => string.Equals(h, keyColumn,  StringComparison.OrdinalIgnoreCase));
        int dataCol = headers.FindIndex(h => string.Equals(h, jsonColumn, StringComparison.OrdinalIgnoreCase));

        if (keyCol < 0 || dataCol < 0)
            return $"Expected columns '{keyColumn}' and '{jsonColumn}' not found.\n" +
                   $"Header row: {lines[0]}";

        // ── Process data rows ──────────────────────────────────────────────────

        var summary     = new StringBuilder();
        int written     = 0, errors = 0;
        int requiredCols = Math.Max(keyCol, dataCol) + 1;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            try
            {
                var fields = ParseCsvRow(lines[i]);

                if (fields.Count < requiredCols)
                {
                    summary.AppendLine($"Row {i + 1}: only {fields.Count} column(s) found (need {requiredCols}), skipped.");
                    errors++;
                    continue;
                }

                var key     = fields[keyCol].Trim();
                var jsonRaw = fields[dataCol];

                if (string.IsNullOrWhiteSpace(jsonRaw))
                {
                    summary.AppendLine($"Row {i + 1} ({key}): {jsonColumn} is empty, skipped.");
                    errors++;
                    continue;
                }

                // Validate and pretty-print. System.Text.Json parses $id/$ref as
                // ordinary object properties without resolving them — that is fine
                // for inspection; resolution happens in Phase 2.
                using var doc   = JsonDocument.Parse(jsonRaw);
                var pretty      = JsonSerializer.Serialize(
                    doc.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });

                // Build a safe filename from the key value.
                var safeName = string.Concat(key.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrWhiteSpace(safeName)) safeName = $"{rowLabel}_row_{i + 1}";

                var outPath = Path.Combine(outputDir, $"{safeName}.json");
                await File.WriteAllTextAsync(outPath, pretty, Encoding.UTF8);

                summary.AppendLine($"  {key}  →  {Path.GetFileName(outPath)}");
                written++;
            }
            catch (JsonException jex)
            {
                summary.AppendLine($"Row {i + 1}: invalid JSON — {jex.Message}");
                errors++;
            }
            catch (Exception ex)
            {
                summary.AppendLine($"Row {i + 1}: unexpected error — {ex.Message}");
                errors++;
            }
        }

        summary.AppendLine();
        summary.Append($"Done.  {written} file(s) written");
        if (errors > 0) summary.Append($",  {errors} error(s)");
        summary.AppendLine(".");
        summary.AppendLine($"Output folder:  {outputDir}");

        return summary.ToString();
    }

    // ── CSV parser ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single CSV line into an ordered list of field values,
    /// following RFC-4180 rules: fields may be optionally quoted, and an
    /// embedded double-quote inside a quoted field is represented as "".
    /// </summary>
    /// <param name="line">A single line from a CSV file.</param>
    /// <returns>List of unquoted field values in column order.</returns>
    private static List<string> ParseCsvRow(string line)
    {
        var fields = new List<string>();
        int i = 0;

        while (i <= line.Length)
        {
            if (i == line.Length) break; // no trailing empty field after final comma

            if (line[i] == '"')
            {
                // ── Quoted field ──────────────────────────────────────────────
                i++; // skip opening quote
                var sb = new StringBuilder();

                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"'); // "" → one literal "
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
                        sb.Append(line[i++]);
                    }
                }

                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++; // skip delimiter
            }
            else
            {
                // ── Unquoted field ────────────────────────────────────────────
                int end = line.IndexOf(',', i);
                if (end < 0) { fields.Add(line[i..]); break; }
                fields.Add(line[i..end]);
                i = end + 1;
            }
        }

        return fields;
    }
}
