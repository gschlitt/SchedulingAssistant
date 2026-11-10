using System.Runtime.CompilerServices;
using TermPoint.Exceptions;

namespace TermPoint.Services;

/// <summary>
/// Sends handled events to the developers (spec item 7): problems TermPoint dealt with but whose frequency in the field
/// matters (a refused save, a lost lock, a refused restore, a failed backup, an unexpected end of the previous session).
///
/// <para><b>Once per run per kind</b> (or up to a caller-chosen few, <c>maxPerRun</c>). The first report of a kind goes through
/// <see cref="IAppLogger.ReportHandled"/> (a local log line, and a handled BugSnag event of warning severity, with no
/// banner); later ones of the same kind are left to the caller's own local log line. A persistent fault (a dead backup
/// folder every 30 minutes, an outage) would otherwise send one report per attempt. The memory of what was sent lives
/// with the logger that received it (in the app there is one, <c>App.Logger</c>, so that is once per run), which also
/// keeps tests that pass their own logger apart.</para>
///
/// <para><b>Privacy.</b> The exception is a <see cref="HandledEventException"/> with fixed text per kind; callers pass
/// metadata with no paths or names (and every report also passes the BugSnag scrubber, spec item 6).</para>
///
/// <para><b>Never throws.</b> Reporting must never turn a handled problem into a crash.</para>
/// </summary>
public static class HandledEventReporter
{
    /// <summary>How many reports of each kind each logger has been sent. Entries die with their logger.</summary>
    private static readonly ConditionalWeakTable<IAppLogger, Dictionary<HandledEvent, int>> Sent = new();

    /// <summary>
    /// Reports <paramref name="kind"/> to <paramref name="logger"/>, unless that kind was already reported to it
    /// <paramref name="maxPerRun"/> times.
    /// </summary>
    /// <param name="kind">The kind of event.</param>
    /// <param name="logger">The logger that sends the report (normally the caller's own).</param>
    /// <param name="metadata">Optional details for the report's "event" tab; no paths, file names or user names.</param>
    /// <param name="maxPerRun">How many reports of this kind may be sent per logger (default 1: once per run). A kind that can legitimately repeat (a recovery after each of several outages) allows a few.</param>
    /// <param name="informational">True for a report that is good news (sent with BugSnag severity Info, see <see cref="IAppLogger.ReportHandled"/>).</param>
    /// <returns>True when this call sent the report; false when the kind was already reported <paramref name="maxPerRun"/> times, or sending failed.</returns>
    public static bool Report(HandledEvent kind, IAppLogger logger, IReadOnlyDictionary<string, string>? metadata = null,
        int maxPerRun = 1, bool informational = false)
    {
        try
        {
            var counts = Sent.GetValue(logger, static _ => new Dictionary<HandledEvent, int>());
            lock (counts)
            {
                counts.TryGetValue(kind, out var sent);
                if (sent >= maxPerRun)
                    return false;
                counts[kind] = sent + 1;
            }

            logger.ReportHandled(
                CreateException(kind),
                context:     HandledEventException.MessageFor(kind),
                metadata:    metadata,
                metadataTab: "event",
                groupingKey: $"handled-event:{kind}",
                informational: informational);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the report's exception with a real stack trace (it is thrown and caught on the spot): an exception that was
    /// never thrown has none, and the report would show where nothing happened.
    /// </summary>
    /// <param name="kind">The kind of event.</param>
    /// <returns>The exception, carrying a stack trace of the reporting call.</returns>
    private static HandledEventException CreateException(HandledEvent kind)
    {
        try
        {
            throw new HandledEventException(kind);
        }
        catch (HandledEventException ex)
        {
            return ex;
        }
    }
}
