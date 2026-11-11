using System.Text.Json;
using TermPoint.Models;

namespace TermPoint.Services;

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
    /// Writes a .tpconfig file to <paramref name="dbFolder"/> under the network deadline (1.2.4 item 4.10, A5): the
    /// folder is the database's own, usually on a share, and a share that stops answering used to freeze the window
    /// here. The filename is derived from the academic unit abbreviation: "{abbrev}-TT.tpconfig", or
    /// "config.tpconfig" if the abbreviation is blank. The file is JSON written as UTF-8 WITHOUT a byte-order mark,
    /// the same bytes <c>File.WriteAllText</c> wrote before. Non-fatal: never throws; the outcome says what happened.
    /// </summary>
    /// <param name="dbFolder">Folder that contains the database file.</param>
    /// <param name="data">Configuration data to serialize.</param>
    /// <param name="acUnitAbbrev">Academic unit abbreviation used for the filename.</param>
    /// <returns>
    /// The path the file was (or would have been) written to, and the write's result
    /// (<see cref="ExportWriteResult.Message"/> holds the wording to show when it did not succeed).
    /// </returns>
    public static async Task<(string Path, ExportWriteResult Result)> WriteAsync(
        string dbFolder, TpConfigData data, string acUnitAbbrev)
    {
        var stem = string.IsNullOrWhiteSpace(acUnitAbbrev) ? "config" : acUnitAbbrev.Trim();
        var path = System.IO.Path.Combine(dbFolder, $"{stem}-TT.tpconfig");

        byte[] bytes;
        try
        {
            bytes = ExportFileWriter.EncodeUtf8(JsonSerializer.Serialize(data, _jsonOptions), withBom: false);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "TpConfigService.WriteAsync failed to serialize");
            return (path, new ExportWriteResult(
                ExportWriteOutcome.Failed, System.IO.Path.GetFileName(path), ExportFileWriter.GenericReason));
        }

        return (path, await ExportFileWriter.WriteAsync(path, bytes));
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
