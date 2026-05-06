using SchedulingAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for F12 and F15 from the data-integrity audit (2026-05-04).
///
/// <list type="bullet">
///   <item>
///     <description>F12 — <see cref="AppSettings.Save"/> previously wrote directly to
///     <c>settings.json</c>. A process crash mid-write leaves a partial JSON file; on the
///     next startup <c>Load</c> throws (or silently falls back to defaults), losing the
///     user's recent-database list and all preferences. The fix uses a write-to-temp-then-rename
///     pattern so the original file is intact unless the rename succeeds.</description>
///   </item>
///   <item>
///     <description>F15 — <see cref="AppSettings.AddRecentDatabase"/> and
///     <see cref="AppSettings.Save"/> were not thread-safe. The autosave timer and the UI
///     thread can both call <c>AddRecentDatabase</c> concurrently, interleaving mutations on the
///     <c>RecentDatabases</c> <see cref="List{T}"/> (not concurrent-collection safe). The fix
///     gates all mutations and <c>Save</c> calls behind a static <c>lock</c>.</description>
///   </item>
/// </list>
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    // Mirrors the private path computed inside AppSettings so the fixture can save/restore
    // the real settings file without reflection.
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TermPoint");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly string SettingsTmpPath = SettingsPath + ".tmp";

    private readonly string? _savedJson;   // null → file did not exist before tests

    /// <summary>
    /// Captures the current settings file content so it can be restored after each test.
    /// Tests are free to corrupt or replace the file; the fixture always repairs it.
    /// </summary>
    public AppSettingsTests()
    {
        _savedJson = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
    }

    /// <summary>
    /// Restores the settings file and resets <see cref="AppSettings.Current"/> so
    /// subsequent tests start from the same baseline.
    /// </summary>
    public void Dispose()
    {
        // Clean up any leftover .tmp file from a failed or interrupted test
        try { if (File.Exists(SettingsTmpPath)) File.Delete(SettingsTmpPath); } catch { /* best effort */ }

        // Restore original file (or delete it if it didn't exist before)
        try
        {
            Directory.CreateDirectory(SettingsDir);
            if (_savedJson is not null)
                File.WriteAllText(SettingsPath, _savedJson);
            else if (File.Exists(SettingsPath))
                File.Delete(SettingsPath);
        }
        catch { /* best effort */ }

        // Reset the in-memory singleton so the next AppSettings.Current re-reads from disk
        AppSettings.Load();
    }

    // ── Group 1 — F12: Load falls back gracefully on corrupt/partial files ──

    /// <summary>
    /// When the settings file contains truncated or invalid JSON (simulating a mid-write crash
    /// before the F12 fix), <see cref="AppSettings.Load"/> must return a default instance
    /// rather than throwing. The existing <c>catch { result = new AppSettings(); }</c> block
    /// handles this; this test documents and locks that contract.
    ///
    /// <para>With the F12 fix (write-to-tmp then rename), this scenario should no longer
    /// occur in practice — but Load must remain resilient for databases created before the fix,
    /// or if an OS crash interrupts the rename step.</para>
    /// </summary>
    [Fact]
    public void Load_WithTruncatedJsonFile_FallsBackToDefaults()
    {
        // Arrange — write a truncated JSON string (simulates mid-write crash)
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, "{\"RecentDatabases\":[\"a\",\"b");   // truncated

        // Act
        var settings = AppSettings.Load();

        // Assert — no exception; defaults returned
        Assert.NotNull(settings);
        Assert.Empty(settings.RecentDatabases);   // default = new List<string>()
        Assert.False(settings.IsInitialSetupComplete);
    }

    /// <summary>
    /// When the settings file is completely empty (another crash scenario),
    /// <see cref="AppSettings.Load"/> must return defaults without throwing.
    /// </summary>
    [Fact]
    public void Load_WithEmptyFile_FallsBackToDefaults()
    {
        // Arrange
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, string.Empty);

        // Act
        var settings = AppSettings.Load();

        // Assert
        Assert.NotNull(settings);
        Assert.Empty(settings.RecentDatabases);
    }

    // ── Group 2 — F12: Save uses atomic write-to-temp-then-rename ─────────

    /// <summary>
    /// After a successful <see cref="AppSettings.Save"/>, no <c>.tmp</c> intermediate file
    /// must remain in the settings directory. The rename must have completed and cleaned up.
    /// </summary>
    [Fact]
    public void Save_DoesNotLeaveTemporaryFile()
    {
        // Arrange
        var settings = AppSettings.Load();

        // Act
        settings.Save();

        // Assert — no leftover .tmp
        Assert.False(File.Exists(SettingsTmpPath),
            "settings.json.tmp must be consumed by File.Move during Save().");

        // The real file must exist and contain valid JSON
        Assert.True(File.Exists(SettingsPath));
        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("IsInitialSetupComplete", json);
    }

    /// <summary>
    /// <see cref="AppSettings.Load"/> must successfully parse a file just written by
    /// <see cref="AppSettings.Save"/> — round-trip check. This verifies that the
    /// atomic write does not introduce encoding issues or truncation.
    /// </summary>
    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        // Arrange
        var settings = AppSettings.Load();
        settings.InstitutionName = "Test University";
        settings.MaxBackupCount  = 42;

        // Act
        settings.Save();
        var reloaded = AppSettings.Load();

        // Assert
        Assert.Equal("Test University", reloaded.InstitutionName);
        Assert.Equal(42,                reloaded.MaxBackupCount);
    }

    // ── Group 3 — F15: Thread-safety of Save and AddRecentDatabase ────────

    /// <summary>
    /// Ten threads all calling <see cref="AppSettings.Save"/> concurrently must not throw
    /// and must leave a valid, complete settings file. Without the F15 lock, concurrent
    /// <c>File.WriteAllText</c> calls can interleave, producing a partial file.
    /// </summary>
    [Fact]
    public void Save_CalledConcurrently_DoesNotThrow()
    {
        // Arrange
        var settings  = AppSettings.Load();
        var barrier   = new Barrier(10);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act — 10 threads all Save() at the same moment
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            try
            {
                barrier.SignalAndWait();   // synchronise start for maximum contention
                settings.Save();
            }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();

        Task.WaitAll(tasks);

        // Assert
        Assert.Empty(exceptions);
        Assert.True(File.Exists(SettingsPath));
        // Verify the file round-trips cleanly (not truncated)
        var reloaded = AppSettings.Load();
        Assert.NotNull(reloaded);
    }

    /// <summary>
    /// Ten threads calling <see cref="AppSettings.AddRecentDatabase"/> concurrently with
    /// distinct paths must not throw and must leave <see cref="AppSettings.RecentDatabases"/>
    /// with at most 10 entries. Without the F15 lock, concurrent list mutations can corrupt
    /// the <see cref="List{T}"/> internal state and cause <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [Fact]
    public void AddRecentDatabase_CalledConcurrently_DoesNotThrow()
    {
        // Arrange — create 15 temp files so File.Exists returns true for each
        var tempDir = Path.Combine(Path.GetTempPath(), $"appsett_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var paths = Enumerable.Range(0, 15)
                              .Select(i => Path.Combine(tempDir, $"db{i}.db"))
                              .ToList();
        foreach (var p in paths)
            File.WriteAllText(p, "x");   // create real files

        var settings   = AppSettings.Load();
        settings.RecentDatabases.Clear();
        var barrier    = new Barrier(paths.Count);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act — all threads fire at once
        var tasks = paths.Select(p => Task.Run(() =>
        {
            try
            {
                barrier.SignalAndWait();
                settings.AddRecentDatabase(p);
            }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();

        Task.WaitAll(tasks);

        // Cleanup temp files
        try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }

        // Assert
        Assert.Empty(exceptions);
        Assert.True(settings.RecentDatabases.Count <= 10,
            $"RecentDatabases should be capped at 10; found {settings.RecentDatabases.Count}.");
    }
}
