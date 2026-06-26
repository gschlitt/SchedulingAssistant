using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TermPoint.Services;

/// <summary>
/// Classifies a folder's suitability for storing a TermPoint database. Detects CFA-protected
/// paths, cloud-synced folders, and write-access problems, returning structured warnings that
/// callers can use for suggestions (filter silently) or validation (show warnings, let user proceed).
///
/// The classification logic is separated from environment discovery: the constructor accepts
/// pre-resolved lists of CFA roots and cloud sync roots so that unit tests can supply controlled
/// inputs. The static <see cref="CreateForCurrentMachine"/> factory resolves the real environment.
/// </summary>
public class FolderAssessor
{
    private readonly IReadOnlyList<string> _cfaRoots;
    private readonly IReadOnlyList<CloudSyncRoot> _cloudSyncRoots;
    private readonly Func<string, bool>? _writabilityProbe;

    /// <param name="cfaRoots">Resolved absolute paths of CFA-protected folders (Documents, Desktop, etc.).</param>
    /// <param name="cloudSyncRoots">Resolved cloud sync root folders with their provider names.</param>
    /// <param name="writabilityProbe">
    /// Optional override for the write-probe function. When null, defaults to
    /// <see cref="WriteAccessProbe.CanCreateFileIn"/>. Inject a fake for unit tests.
    /// </param>
    public FolderAssessor(
        IReadOnlyList<string> cfaRoots,
        IReadOnlyList<CloudSyncRoot> cloudSyncRoots,
        Func<string, bool>? writabilityProbe = null)
    {
        _cfaRoots = cfaRoots ?? throw new ArgumentNullException(nameof(cfaRoots));
        _cloudSyncRoots = cloudSyncRoots ?? throw new ArgumentNullException(nameof(cloudSyncRoots));
        _writabilityProbe = writabilityProbe;
    }

    /// <summary>
    /// Assesses a single folder path for suitability as a database location.
    /// Returns a structured result with any warnings found.
    /// </summary>
    /// <param name="path">Absolute path to assess.</param>
    /// <returns>Assessment result with warnings (if any) and writability status.</returns>
    public FolderAssessment Assess(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new FolderAssessment(path ?? "", false, new[] { FolderWarning.InvalidPath() });

        string resolved;
        try { resolved = Path.GetFullPath(path); }
        catch { return new FolderAssessment(path, false, new[] { FolderWarning.InvalidPath() }); }

        var warnings = new List<FolderWarning>();

        // Check CFA-protected status.
        var cfaRoot = FindMatchingRoot(resolved, _cfaRoots);
        if (cfaRoot != null)
            warnings.Add(FolderWarning.CfaProtected(cfaRoot));

        // Check cloud sync status.
        var syncMatch = FindMatchingCloudRoot(resolved);
        if (syncMatch != null)
            warnings.Add(FolderWarning.CloudSynced(syncMatch.Provider));

        // Probe writability (uses real probe or injected fake).
        // When a fake probe is injected (tests), pass the path directly — the fake
        // doesn't need the directory to exist. With the real probe, walk up to the
        // nearest existing ancestor.
        var probe = _writabilityProbe ?? WriteAccessProbe.CanCreateFileIn;
        string? parentToProbe;
        if (_writabilityProbe != null)
            parentToProbe = resolved;
        else
            parentToProbe = Directory.Exists(resolved) ? resolved : FindExistingAncestor(resolved);
        var isWritable = parentToProbe != null && probe(parentToProbe);
        if (!isWritable)
            warnings.Add(FolderWarning.NotWritable());

        return new FolderAssessment(resolved, isWritable, warnings);
    }

    /// <summary>
    /// Timeout for per-drive probes. Shorter than <see cref="NetworkFileOps.TimeoutMs"/>
    /// because we probe multiple drives and don't want the total to accumulate.
    /// </summary>
    internal const int DriveProbeTimeoutMs = 3000;

    /// <summary>
    /// Suggests database folder locations by enumerating fixed and network drives
    /// and filtering out unsuitable candidates. Yields each suggestion as soon as
    /// it is assessed, so local drives appear instantly while slow network probes
    /// trickle in.
    ///
    /// Network drive probes (IsReady, write-probe) can hang on unreachable shares.
    /// Each probe runs with a per-drive timeout. The method is fully defensive: any
    /// failure at any stage results in that candidate being silently dropped.
    /// </summary>
    /// <param name="institutionAbbrev">Optional institution abbreviation for folder naming.</param>
    /// <returns>Suggestions yielded incrementally, local drives first.</returns>
    public async IAsyncEnumerable<FolderSuggestion> SuggestLocationsAsync(
        string? institutionAbbrev,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var appFolderName = "TermPoint";
        var subFolder = string.IsNullOrWhiteSpace(institutionAbbrev)
            ? ""
            : institutionAbbrev!.Trim();

        var candidates = new List<(string path, string description, int priority, bool isNetwork)>();

        DriveInfo[] allDrives;
        try
        {
            allDrives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }

        var systemDrive = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

        // Phase 1: collect local-drive candidates synchronously (instant).
        foreach (var drive in allDrives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) continue;

                var root = drive.Name;
                bool isSystemDrive = string.Equals(
                    root, systemDrive, StringComparison.OrdinalIgnoreCase);

                var folderPath = string.IsNullOrEmpty(subFolder)
                    ? Path.Combine(root, appFolderName)
                    : Path.Combine(root, appFolderName, subFolder);

                if (isSystemDrive)
                {
                    var programData = Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData);
                    if (!string.IsNullOrEmpty(programData))
                    {
                        var pdPath = string.IsNullOrEmpty(subFolder)
                            ? Path.Combine(programData, appFolderName)
                            : Path.Combine(programData, appFolderName, subFolder);
                        candidates.Add((pdPath, "Shared application data folder", 20, false));
                    }
                    candidates.Add((folderPath, "System drive", 30, false));
                }
                else
                {
                    candidates.Add((folderPath, "Secondary drive", 10, false));
                }
            }
            catch
            {
                // Skip any drive that throws during property access.
            }
        }

        // Yield local candidates immediately (sorted by priority).
        foreach (var c in candidates.OrderBy(c => c.priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suggestion = await AssessCandidateAsync(c.path, c.description, c.isNetwork);
            if (suggestion != null)
                yield return suggestion;
        }

        // Phase 2: probe network drives (may be slow — each has a timeout).
        foreach (var drive in allDrives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FolderSuggestion? networkSuggestion = null;
            try
            {
                if (drive.DriveType != DriveType.Network) continue;

                var (completed, ready) = await RunWithTimeout(
                    () => drive.IsReady, DriveProbeTimeoutMs);
                if (!completed || !ready) continue;

                var folderPath = string.IsNullOrEmpty(subFolder)
                    ? Path.Combine(drive.Name, appFolderName)
                    : Path.Combine(drive.Name, appFolderName, subFolder);

                networkSuggestion = await AssessCandidateAsync(folderPath, "Network drive", isNetwork: true);
            }
            catch
            {
                // Skip any drive that throws.
            }
            if (networkSuggestion != null)
                yield return networkSuggestion;
        }
    }

    /// <summary>
    /// Assesses a single candidate path and returns a <see cref="FolderSuggestion"/>
    /// if it passes all checks, or null if it should be skipped.
    /// </summary>
    private async Task<FolderSuggestion?> AssessCandidateAsync(
        string candidatePath, string description, bool isNetwork)
    {
        try
        {
            var assessment = Assess(candidatePath);

            if (assessment.Warnings.Any(w => w.Kind != WarningKind.NotWritable))
                return null;

            bool writable;
            if (Directory.Exists(candidatePath))
            {
                writable = assessment.IsWritable;
            }
            else
            {
                var testDir = FindExistingAncestor(candidatePath);
                if (testDir == null) return null;

                if (isNetwork)
                {
                    var (completed, result) = await RunWithTimeout(
                        () => {
                            var p = _writabilityProbe ?? WriteAccessProbe.CanCreateFileIn;
                            return p(testDir);
                        }, DriveProbeTimeoutMs);
                    writable = completed && result;
                }
                else
                {
                    var probe = _writabilityProbe ?? WriteAccessProbe.CanCreateFileIn;
                    writable = probe(testDir);
                }
            }

            if (!writable) return null;

            return new FolderSuggestion(
                candidatePath, description, Directory.Exists(candidatePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a synchronous operation on a thread-pool thread, racing it against
    /// the given timeout. Returns <c>(false, default)</c> on timeout.
    /// </summary>
    private static async Task<(bool Completed, T? Result)> RunWithTimeout<T>(
        Func<T> operation, int timeoutMs)
    {
        var task = Task.Run(operation);
        var winner = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (winner == task)
            return (true, await task);
        return (false, default);
    }

    /// <summary>
    /// Creates an assessor wired to the current machine's environment: resolves
    /// CFA-protected known folders and discovers cloud sync roots.
    /// </summary>
    public static FolderAssessor CreateForCurrentMachine()
    {
        return new FolderAssessor(
            ResolveCfaRoots(),
            DiscoverCloudSyncRoots());
    }

    /// <summary>
    /// Resolves the default CFA-protected known folders on this machine.
    /// </summary>
    internal static IReadOnlyList<string> ResolveCfaRoots()
    {
        var folders = new[]
        {
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.Favorites,
        };

        return folders
            .Select(f => Environment.GetFolderPath(f))
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => Path.GetFullPath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Discovers cloud sync root folders on this machine by checking well-known
    /// environment variables and configuration files.
    /// </summary>
    internal static IReadOnlyList<CloudSyncRoot> DiscoverCloudSyncRoots()
    {
        var roots = new List<CloudSyncRoot>();

        // OneDrive: environment variables set by the OneDrive client.
        foreach (var envVar in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var val = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
            {
                try
                {
                    roots.Add(new CloudSyncRoot("OneDrive", Path.GetFullPath(val)));
                }
                catch { /* bad path */ }
            }
        }

        // Dropbox: info.json in %APPDATA%\Dropbox contains sync root paths.
        try
        {
            var dropboxInfo = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dropbox", "info.json");
            if (File.Exists(dropboxInfo))
            {
                var json = File.ReadAllText(dropboxInfo);
                // Lightweight parse: extract "path" values without a JSON dependency.
                foreach (var pathValue in ExtractJsonStringValues(json, "path"))
                {
                    if (Directory.Exists(pathValue))
                        roots.Add(new CloudSyncRoot("Dropbox", Path.GetFullPath(pathValue)));
                }
            }
        }
        catch { /* Dropbox detection is best-effort */ }

        // Google Drive: check common locations.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var googleDrive = Path.Combine(userProfile, "Google Drive");
            if (Directory.Exists(googleDrive))
                roots.Add(new CloudSyncRoot("Google Drive", Path.GetFullPath(googleDrive)));
        }

        // iCloud Drive: standard Windows location.
        if (!string.IsNullOrEmpty(userProfile))
        {
            var icloud = Path.Combine(userProfile, "iCloudDrive");
            if (Directory.Exists(icloud))
                roots.Add(new CloudSyncRoot("iCloud", Path.GetFullPath(icloud)));
        }

        return roots
            .GroupBy(r => r.RootPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Walks up the directory tree from <paramref name="path"/> until it finds a directory
    /// that actually exists on disk. Returns null if no ancestor exists (e.g. invalid drive).
    /// </summary>
    private static string? FindExistingAncestor(string path)
    {
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break; // at root
            dir = parent;
        }
        return null;
    }

    /// <summary>
    /// Returns the CFA root that contains <paramref name="fullPath"/>, or null if none match.
    /// </summary>
    private string? FindMatchingRoot(string fullPath, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (IsAtOrUnder(fullPath, root))
                return root;
        }
        return null;
    }

    /// <summary>
    /// Returns the cloud sync root that contains <paramref name="fullPath"/>, or null if none match.
    /// </summary>
    private CloudSyncRoot? FindMatchingCloudRoot(string fullPath)
    {
        foreach (var root in _cloudSyncRoots)
        {
            if (IsAtOrUnder(fullPath, root.RootPath))
                return root;
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> equals <paramref name="ancestor"/> or is
    /// nested beneath it. Case-insensitive, separator-normalized.
    /// </summary>
    internal static bool IsAtOrUnder(string fullPath, string ancestor)
    {
        string a;
        try { a = Path.GetFullPath(ancestor); }
        catch { return false; }

        a = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var p = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts string values for a given JSON key using simple text scanning.
    /// Not a full JSON parser — sufficient for the flat structure of Dropbox info.json.
    /// </summary>
    internal static IEnumerable<string> ExtractJsonStringValues(string json, string key)
    {
        var pattern = $"\"{key}\"";
        int pos = 0;
        while ((pos = json.IndexOf(pattern, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            pos += pattern.Length;
            // Skip whitespace and colon.
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == ':' || json[pos] == '\t'
                || json[pos] == '\r' || json[pos] == '\n'))
                pos++;
            if (pos < json.Length && json[pos] == '"')
            {
                pos++; // skip opening quote
                var end = json.IndexOf('"', pos);
                if (end > pos)
                {
                    var value = json.Substring(pos, end - pos)
                        .Replace("\\\\", "\\")
                        .Replace("\\/", "/");
                    yield return value;
                    pos = end + 1;
                }
            }
        }
    }
}

/// <summary>
/// Result of assessing a folder's suitability for a TermPoint database.
/// </summary>
/// <param name="ResolvedPath">The fully resolved absolute path that was assessed.</param>
/// <param name="IsWritable">Whether the write-probe succeeded (or the folder doesn't exist yet but its parent is writable).</param>
/// <param name="Warnings">Any suitability warnings found. Empty means the folder is a good candidate.</param>
public record FolderAssessment(
    string ResolvedPath,
    bool IsWritable,
    IReadOnlyList<FolderWarning> Warnings)
{
    /// <summary>True when no warnings were raised — the folder is suitable without caveats.</summary>
    public bool IsSuitable => Warnings.Count == 0;
}

/// <summary>
/// A single warning about a folder's suitability.
/// </summary>
public record FolderWarning(WarningKind Kind, string Message, string? Detail = null)
{
    /// <summary>The path is null, empty, or unparseable.</summary>
    public static FolderWarning InvalidPath() =>
        new(WarningKind.InvalidPath, "The path is not valid.");

    /// <summary>The folder is at or under a CFA-protected known folder.</summary>
    /// <param name="cfaRoot">The protected root that matched (e.g. Documents path).</param>
    public static FolderWarning CfaProtected(string cfaRoot) =>
        new(WarningKind.CfaProtected,
            "This folder is inside a Windows-protected location (Documents, Desktop, etc.). " +
            "Windows Defender may block the application from saving here.",
            $"Protected root: {cfaRoot}. Either store the database outside these folders, or add " +
            "this application under Windows Security → Virus & threat protection → " +
            "Ransomware protection → Allow an app through Controlled folder access.");

    /// <summary>The folder is inside a cloud-synced directory.</summary>
    /// <param name="provider">The cloud provider name (OneDrive, Dropbox, etc.).</param>
    public static FolderWarning CloudSynced(string provider) =>
        new(WarningKind.CloudSynced,
            $"This folder is synced by {provider}. Cloud sync services can corrupt SQLite " +
            "databases by partially syncing internal files. Store the database in a non-synced folder.",
            $"Cloud sync provider: {provider}");

    /// <summary>The application cannot create files in this folder.</summary>
    public static FolderWarning NotWritable() =>
        new(WarningKind.NotWritable,
            "The application cannot write to this folder. Choose a different location or " +
            "check folder permissions.");
}

/// <summary>
/// The kind of suitability warning raised during folder assessment.
/// </summary>
public enum WarningKind
{
    /// <summary>The path is null, empty, or cannot be parsed.</summary>
    InvalidPath,

    /// <summary>The folder is inside a CFA-protected known folder.</summary>
    CfaProtected,

    /// <summary>The folder is inside a cloud-synced directory.</summary>
    CloudSynced,

    /// <summary>The folder is not writable (CFA block, ACL, read-only media, etc.).</summary>
    NotWritable,
}

/// <summary>
/// A detected cloud sync root folder and its provider.
/// </summary>
/// <param name="Provider">The cloud provider name (e.g. "OneDrive", "Dropbox", "Google Drive", "iCloud").</param>
/// <param name="RootPath">The resolved absolute path of the sync root.</param>
public record CloudSyncRoot(string Provider, string RootPath);

/// <summary>
/// A suggested database folder location, pre-assessed as suitable.
/// </summary>
/// <param name="Path">The suggested absolute folder path.</param>
/// <param name="Description">A short human-readable description (e.g. "Secondary drive", "Shared application data folder").</param>
/// <param name="AlreadyExists">Whether the folder already exists on disk.</param>
public record FolderSuggestion(string Path, string Description, bool AlreadyExists);
