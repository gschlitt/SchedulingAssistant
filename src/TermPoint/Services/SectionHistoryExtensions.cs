namespace TermPoint.Services;

/// <summary>
/// Null-tolerant shortcuts for classes that take an optional <see cref="SectionHistory"/> (so tests and the demo can
/// construct them without one): with no history the write just runs unrecorded.
/// </summary>
public static class SectionHistoryExtensions
{
    /// <summary>
    /// Runs <paramref name="write"/> as one undo step when there is a history, otherwise just runs it.
    /// </summary>
    /// <param name="history">The history, or null.</param>
    /// <param name="label">The approved step label (see <see cref="HistoryText"/>).</param>
    /// <param name="write">The user action's repository writes (never inside an open transaction).</param>
    /// <exception cref="Exception">Whatever <paramref name="write"/> throws.</exception>
    public static void RecordOrRun(this SectionHistory? history, string label, Action write)
    {
        if (history is null) write();
        else history.Record(label, write);
    }

    /// <summary>
    /// Pauses recording for section writes that are not steps when there is a history; otherwise returns an object whose
    /// disposal does nothing. Begin it before any transaction and dispose it after the transaction is disposed.
    /// </summary>
    /// <param name="history">The history, or null.</param>
    /// <returns>An object whose disposal ends the pause.</returns>
    public static IDisposable PauseOrNothing(this SectionHistory? history)
        => history?.PauseRecording() ?? NoPause.Instance;

    /// <summary>The handle returned when there is no history to pause.</summary>
    private sealed class NoPause : IDisposable
    {
        /// <summary>The shared instance.</summary>
        public static readonly NoPause Instance = new();

        /// <summary>Does nothing.</summary>
        public void Dispose() { }
    }
}
