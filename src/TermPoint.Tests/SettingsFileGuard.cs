using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// Test helper that protects the developer's real <c>settings.json</c>. Tests that change
/// <see cref="AppSettings.Current"/> and call <see cref="AppSettings.Save"/> (as the Preferences view
/// model does) would otherwise overwrite it. Create one in a <c>using</c> block: on dispose it puts the
/// file back exactly as it was (or deletes it if it did not exist) and reloads
/// <see cref="AppSettings.Current"/> from it, so later tests start from the same baseline.
/// Tests run without parallelism (see AssemblyInfo.cs), so there is no race on the shared file.
/// </summary>
internal sealed class SettingsFileGuard : IDisposable
{
    /// <summary>
    /// The settings file. Since P5 this is the test run's private copy (<see cref="AppSettings.SettingsFilePath"/>,
    /// redirected by <see cref="TestAssemblyGuards"/>), never the developer's real one; the guard still restores it
    /// so each test starts from the same baseline.
    /// </summary>
    private static string SettingsPath => AppSettings.SettingsFilePath;

    /// <summary>Folder of the settings file.</summary>
    private static string SettingsDir => Path.GetDirectoryName(SettingsPath)!;

    /// <summary>The file's content when the guard was created; null when it did not exist.</summary>
    private readonly string? _savedJson;

    /// <summary>Captures the current settings file so <see cref="Dispose"/> can restore it.</summary>
    public SettingsFileGuard()
    {
        _savedJson = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
    }

    /// <summary>Restores the settings file and reloads <see cref="AppSettings.Current"/> from it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            if (_savedJson is not null) File.WriteAllText(SettingsPath, _savedJson);
            else if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
        }
        catch { /* best effort */ }

        AppSettings.Load();
    }
}
