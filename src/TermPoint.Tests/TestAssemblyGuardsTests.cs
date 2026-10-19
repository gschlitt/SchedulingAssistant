using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pins the assembly-wide guards in <see cref="TestAssemblyGuards"/>: no test may write the developer's real log
/// file, send a BugSnag report from their machine, or touch their real settings file.
/// </summary>
public sealed class TestAssemblyGuardsTests
{
    /// <summary>
    /// <see cref="App.Logger"/> is the silent logger for the whole run, so production code that logs an error during
    /// a test reaches neither <c>%AppData%\TermPoint\Logs</c> nor BugSnag.
    /// </summary>
    [Fact]
    public void AppLogger_IsSilent_ForTheWholeTestRun()
        => Assert.IsType<SilentAppLogger>(App.Logger);

    /// <summary>
    /// The silent logger still behaves like the real one for callers: an error raises
    /// <see cref="IAppLogger.ErrorLogged"/> with the context text, and nothing else happens.
    /// </summary>
    [Fact]
    public void SilentLogger_RaisesErrorLogged_LikeTheRealOne()
    {
        var logger = new SilentAppLogger();
        string? raised = null;
        logger.ErrorLogged += (_, message) => raised = message;

        logger.LogError(new InvalidOperationException("boom"), "Saving failed");

        Assert.Equal("Saving failed", raised);
    }

    /// <summary>With <see cref="IAppLogger.ThrowOnError"/> set, the silent logger re-throws, as the real one does.</summary>
    [Fact]
    public void SilentLogger_RethrowsWhenThrowOnErrorIsSet()
    {
        var logger = new SilentAppLogger { ThrowOnError = true };

        Assert.Throws<InvalidOperationException>(() => logger.LogError(new InvalidOperationException("boom")));
    }

    /// <summary>The settings file the tests use is the run's private copy, never the real one (P5).</summary>
    [Fact]
    public void Settings_AreThePrivateTestCopy()
        => Assert.StartsWith(TestAssemblyGuards.SettingsDirectory, AppSettings.SettingsFilePath);
}
