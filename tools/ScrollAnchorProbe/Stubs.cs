// The only part of the app the real behaviour sources touch is TermPoint.App.Logger (the oscillation
// guard logs once through it). This stand-in lets those sources compile here without the app, and
// keeps the probe's log lines in memory and on the console instead of in a user's real log file.
namespace TermPoint
{
    /// <summary>Records and echoes log lines written by the behaviours under test.</summary>
    public sealed class ProbeLogger
    {
        /// <summary>Every line logged so far, in order.</summary>
        public readonly List<string> Lines = new();

        /// <summary>Same signature as the app's logger, so the behaviour source compiles unchanged.</summary>
        /// <param name="message">The message to record.</param>
        /// <param name="context">Unused.</param>
        public void LogInfo(string message, string? context = null)
        {
            Lines.Add(message);
            Console.WriteLine("    [LOG] " + message);
        }
    }

    /// <summary>Stand-in for TermPoint.App, exposing only the logger.</summary>
    public static class App
    {
        /// <summary>The probe's logger.</summary>
        public static ProbeLogger Logger { get; } = new();
    }
}
