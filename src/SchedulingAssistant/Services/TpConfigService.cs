using System.Text.Json;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Reads and writes .tpconfig files — portable institution configuration bundles.
/// A .tpconfig is written to the same folder as the database after first-run wizard
/// completion. It can be shared between colleagues or reused when creating a new DB
/// at the same institution.
/// </summary>
public static class TpConfigService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Writes a .tpconfig file to <paramref name="dbFolder"/>.
    /// The filename is derived from the academic unit abbreviation: "{abbrev}-TT.tpconfig".
    /// Falls back to "config.tpconfig" if the abbreviation is blank.
    /// Non-fatal: logs to App.Logger on failure and returns false.
    /// </summary>
    /// <param name="dbFolder">Folder that contains the database file.</param>
    /// <param name="data">Configuration data to serialize.</param>
    /// <param name="acUnitAbbrev">Academic unit abbreviation used for the filename.</param>
    /// <returns>The path written to, or null on failure.</returns>
    public static string? Write(string dbFolder, TpConfigData data, string acUnitAbbrev)
    {
        try
        {
            var stem = string.IsNullOrWhiteSpace(acUnitAbbrev) ? "config" : acUnitAbbrev.Trim();
            var path = Path.Combine(dbFolder, $"{stem}-TT.tpconfig");
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(path, json);
            return path;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "TpConfigService.Write failed");
            return null;
        }
    }

    /// <summary>
    /// Attempts to read and deserialize a .tpconfig file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Full path to the .tpconfig file.</param>
    /// <param name="data">Deserialized data on success; null on failure.</param>
    /// <returns>True if the file was read and parsed successfully.</returns>
    public static bool TryRead(string path, out TpConfigData? data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            data = JsonSerializer.Deserialize<TpConfigData>(json, _jsonOptions);
            return data is not null;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, $"TpConfigService.TryRead failed for {path}");
            return false;
        }
    }
}
