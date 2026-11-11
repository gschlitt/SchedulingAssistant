using System.Text.Json;

namespace TermPoint.Licensing;

/// <summary>
/// Remembers, on this computer, the last <c>termpoint.lic</c> text that was read successfully (and was
/// <see cref="LicenseState.Licensed"/> or <see cref="LicenseState.Expired"/>) for each shared-database folder.
/// When the share doesn't answer in time, or the read fails for a reason other than "not found", the remembered
/// text can be validated again instead of falling back to the trial.
///
/// <para>Stored as <c>license-cache.json</c> next to <c>trial.json</c>. Folder paths are normalised
/// (<see cref="Path.GetFullPath(string)"/>, trailing separator trimmed) and compared case-insensitively. At most
/// <see cref="MaxEntries"/> folders are kept; the oldest are dropped first. The cache is only a convenience:
/// every IO or JSON failure is swallowed (and logged) and a missing or corrupt file is simply empty. The text is
/// never trusted on its own: callers re-check its signature and expiry.</para>
/// </summary>
public class LicenseCache
{
    /// <summary>The cache file name inside the AppData folder.</summary>
    public const string CacheFileName = "license-cache.json";

    /// <summary>The most folders remembered at once.</summary>
    public const int MaxEntries = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _cacheFilePath;
    private readonly Action<string>? _log;
    private readonly object _sync = new();

    /// <summary>
    /// Creates a cache stored in the given AppData directory.
    /// </summary>
    /// <param name="appDataDirectory">Directory holding <c>trial.json</c> (e.g. <c>%APPDATA%\TermPoint</c>).</param>
    /// <param name="log">Optional sink for diagnostic lines. Null = silent.</param>
    public LicenseCache(string appDataDirectory, Action<string>? log = null)
    {
        _cacheFilePath = Path.Combine(appDataDirectory, CacheFileName);
        _log = log;
    }

    /// <summary>
    /// Returns the remembered licence text for a folder.
    /// </summary>
    /// <param name="folder">The shared-database folder (the one that holds <c>termpoint.lic</c>).</param>
    /// <returns>The remembered text, or null when nothing is remembered or the cache can't be read.</returns>
    public string? Get(string folder)
    {
        lock (_sync)
        {
            var key = Normalise(folder);
            return Load().Entries
                .FirstOrDefault(e => string.Equals(e.Folder, key, StringComparison.OrdinalIgnoreCase))?.Text;
        }
    }

    /// <summary>
    /// Remembers the licence text for a folder, replacing any earlier entry for it and dropping the oldest
    /// entries beyond <see cref="MaxEntries"/>. Never throws.
    /// </summary>
    /// <param name="folder">The shared-database folder.</param>
    /// <param name="text">The licence file text that was just read.</param>
    public void Remember(string folder, string text)
    {
        lock (_sync)
        {
            var key = Normalise(folder);
            var data = Load();
            data.Entries.RemoveAll(e => string.Equals(e.Folder, key, StringComparison.OrdinalIgnoreCase));
            data.Entries.Add(new Entry { Folder = key, Text = text, SavedUtc = DateTime.UtcNow });

            // Newest last; drop from the front (the oldest) when over the limit.
            while (data.Entries.Count > MaxEntries)
                data.Entries.RemoveAt(0);

            Save(data);
        }
    }

    /// <summary>
    /// Removes the entry for a folder (used when the licence file is genuinely gone). Never throws.
    /// </summary>
    /// <param name="folder">The shared-database folder.</param>
    public void Forget(string folder)
    {
        lock (_sync)
        {
            var key = Normalise(folder);
            var data = Load();
            if (data.Entries.RemoveAll(e => string.Equals(e.Folder, key, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(data);
        }
    }

    /// <summary>
    /// Normalises a folder path so equivalent spellings (case, trailing separator, relative parts) match.
    /// </summary>
    /// <param name="folder">The folder path.</param>
    /// <returns>The normalised path; the input trimmed of a trailing separator if it can't be resolved.</returns>
    private static string Normalise(string folder)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(folder ?? string.Empty);
        }
    }

    /// <summary>
    /// Reads the cache file. A missing, empty or corrupt file gives an empty cache.
    /// </summary>
    /// <returns>The cache contents (never null).</returns>
    private CacheData Load()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return new CacheData();

            var json = File.ReadAllText(_cacheFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new CacheData();

            var data = JsonSerializer.Deserialize<CacheData>(json, JsonOptions);
            data ??= new CacheData();
            data.Entries ??= new List<Entry>();
            data.Entries.RemoveAll(e => e is null || string.IsNullOrEmpty(e.Folder) || string.IsNullOrEmpty(e.Text));
            return data;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Licensing] Could not read {CacheFileName} ({ex.GetType().Name}); treating it as empty");
            return new CacheData();
        }
    }

    /// <summary>
    /// Writes the cache file (via a temporary file, then a rename). Failures are logged and swallowed.
    /// </summary>
    /// <param name="data">The contents to save.</param>
    private void Save(CacheData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);
            var temp = _cacheFilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temp, _cacheFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Licensing] Could not save {CacheFileName} ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>Shape of <c>license-cache.json</c>.</summary>
    private class CacheData
    {
        public List<Entry> Entries { get; set; } = new();
    }

    /// <summary>One remembered folder.</summary>
    private class Entry
    {
        public string Folder { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime SavedUtc { get; set; }
    }
}
