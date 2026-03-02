using System.Text.Json;
using System.Text.Json.Serialization;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Utility for exporting LegalStartTimes data to JSON for reference and persistence.
/// Captures the block length / start time configuration for each academic year.
/// </summary>
public class LegalStartTimesDataExporter
{
    private readonly AcademicYearRepository _ayRepo;
    private readonly LegalStartTimeRepository _startTimeRepo;

    public record AcademicYearStartTimesExport(
        [property: JsonPropertyName("academic_year_id")] string AcademicYearId,
        [property: JsonPropertyName("academic_year_name")] string AcademicYearName,
        [property: JsonPropertyName("block_lengths")] List<BlockLengthExport> BlockLengths
    );

    public record BlockLengthExport(
        [property: JsonPropertyName("block_length_hours")] double BlockLengthHours,
        [property: JsonPropertyName("start_times_minutes")] List<int> StartTimesMinutes,
        [property: JsonPropertyName("start_times_display")] string StartTimesDisplay
    );

    public record ExportData(
        [property: JsonPropertyName("export_timestamp")] DateTime ExportTimestamp,
        [property: JsonPropertyName("database_note")] string DatabaseNote,
        [property: JsonPropertyName("academic_years")] List<AcademicYearStartTimesExport> AcademicYears
    );

    public LegalStartTimesDataExporter(
        AcademicYearRepository ayRepo,
        LegalStartTimeRepository startTimeRepo)
    {
        _ayRepo = ayRepo;
        _startTimeRepo = startTimeRepo;
    }

    /// <summary>
    /// Export all LegalStartTimes data for all academic years to a JSON structure.
    /// </summary>
    public ExportData ExportAll()
    {
        var academicYears = _ayRepo.GetAll().OrderBy(ay => ay.Name).ToList();
        var exports = new List<AcademicYearStartTimesExport>();

        foreach (var ay in academicYears)
        {
            var startTimes = _startTimeRepo.GetAll(ay.Id).OrderBy(st => st.BlockLength).ToList();
            var blockLengths = new List<BlockLengthExport>();

            foreach (var st in startTimes)
            {
                var displayTimes = FormatStartTimesDisplay(st.StartTimes);
                blockLengths.Add(new BlockLengthExport(
                    st.BlockLength,
                    st.StartTimes.OrderBy(x => x).ToList(),
                    displayTimes
                ));
            }

            exports.Add(new AcademicYearStartTimesExport(
                ay.Id,
                ay.Name,
                blockLengths
            ));
        }

        return new ExportData(
            DateTime.UtcNow,
            "Exported LegalStartTimes configuration from SchedulingAssistant database",
            exports
        );
    }

    /// <summary>
    /// Save exported data to a JSON file.
    /// </summary>
    public void SaveToFile(ExportData data, string filePath)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Export all data and save to file in one operation.
    /// </summary>
    public void ExportAndSaveAll(string filePath)
    {
        var data = ExportAll();
        SaveToFile(data, filePath);
    }

    /// <summary>
    /// Format start times (in minutes from midnight) as human-readable HH:MM strings.
    /// </summary>
    private static string FormatStartTimesDisplay(List<int> startTimesMinutes)
    {
        if (startTimesMinutes.Count == 0) return "(none)";

        var times = startTimesMinutes
            .OrderBy(m => m)
            .Select(m =>
            {
                var hours = m / 60;
                var mins = m % 60;
                return $"{hours:D2}:{mins:D2}";
            });

        return string.Join(", ", times);
    }

}
