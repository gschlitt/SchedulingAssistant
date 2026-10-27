using System.Collections;
using System.Diagnostics;
using Bugsnag;
using Bugsnag.Payload;

namespace TermPoint.Services;

/// <summary>
/// The one gate every error report and session batch passes on its way to BugSnag (spec item 6, step 6b). It wraps the
/// real delivery and, before forwarding a payload, removes the private details from a fixed list of fields with
/// <see cref="PrivacyScrubber.ScrubText"/>, takes out the computer's name, and stamps the anonymous install ID.
///
/// <para><b>What it edits.</b> On an error report (<see cref="Report"/>): the event's context and grouping key; each
/// exception's message (the error class and stack frames are left alone, because BugSnag groups by them); every
/// breadcrumb's name and metadata; every metadata tab, at any depth; the device's <c>hostname</c>, which is removed; and
/// the user, which becomes just the install ID (<see cref="AppSettings.InstallId"/>), so every report, even one sent
/// before startup finishes, can be grouped by install. On a session batch (<see cref="BatchedSessions"/>): the device's
/// <c>hostname</c>, which is removed. It is deliberately not a blind walk over every string: that would mangle the
/// notifier's address and the stack-frame fields used for grouping.</para>
///
/// <para><b>Copies, not edits in place.</b> Breadcrumbs, metadata tabs and the device can be objects the BugSnag client
/// shares between reports (its breadcrumb buffer, global metadata), so they are replaced with scrubbed copies; only
/// objects made for this one report (the exceptions) are edited directly.</para>
///
/// <para><b>Fails closed.</b> If scrubbing throws (for example a pattern timed out) or takes longer than
/// <see cref="TotalDeadline"/> in all, the payload is dropped: nothing is sent, and one local log line says so. The
/// caller's own local log line about the error already stands. The deadline also caps what a report costs the calling
/// thread, which can be the UI thread.</para>
///
/// <para>Desktop only (BugSnag is not part of the browser build).</para>
/// </summary>
public sealed class ScrubbingDelivery : IDelivery
{
    /// <summary>The most time scrubbing one payload may take before the payload is dropped.</summary>
    internal static readonly TimeSpan TotalDeadline = TimeSpan.FromSeconds(1);

    /// <summary>The text of the local log line written when a payload is dropped.</summary>
    internal const string DroppedMessage =
        "An error report was not sent to the developers: it could not be checked for private details in time.";

    /// <summary>The payload key BugSnag uses for a device's host (computer) name.</summary>
    private const string HostnameKey = "hostname";

    private readonly IDelivery _inner;
    private readonly Func<string?> _installId;
    private readonly Action<string>? _logLocally;

    /// <summary>Creates the gate in front of <paramref name="inner"/>.</summary>
    /// <param name="inner">The real delivery (in the app, <see cref="DefaultDelivery.Instance"/>), which receives only scrubbed payloads.</param>
    /// <param name="installId">Reads the anonymous install ID at send time; may return null or throw, in which case the report goes without a user.</param>
    /// <param name="logLocally">Writes one line to the local log when a payload is dropped; null writes nothing. Must not send anything to BugSnag.</param>
    public ScrubbingDelivery(IDelivery inner, Func<string?> installId, Action<string>? logLocally = null)
    {
        _inner      = inner ?? throw new ArgumentNullException(nameof(inner));
        _installId  = installId ?? throw new ArgumentNullException(nameof(installId));
        _logLocally = logLocally;
    }

    /// <summary>
    /// Scrubs <paramref name="payload"/> (see the class summary) and forwards it to the real delivery, or drops it if
    /// scrubbing fails or runs out of time. A payload of a kind it does not know is dropped too: it can't be checked.
    /// Never throws.
    /// </summary>
    /// <param name="payload">The report or session batch the BugSnag client wants to send.</param>
    public void Send(IPayload payload)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            switch (payload)
            {
                case Report report:
                    ScrubReport(report, clock);
                    break;
                case BatchedSessions sessions:
                    sessions["device"] = WithoutHostname(sessions.TryGetValue("device", out var device) ? device : null);
                    break;
                default:
                    Drop();
                    return;
            }
            CheckDeadline(clock);
        }
        catch (System.Exception)
        {
            Drop();
            return;
        }

        _inner.Send(payload);
    }

    /// <summary>Scrubs the fields of an error report's event, checking the deadline as it goes.</summary>
    /// <param name="report">The report.</param>
    /// <param name="clock">Started when the payload arrived.</param>
    /// <exception cref="TimeoutException">Scrubbing passed <see cref="TotalDeadline"/>.</exception>
    private void ScrubReport(Report report, Stopwatch clock)
    {
        var ev = report.Event;

        ev.Context      = PrivacyScrubber.ScrubText(ev.Context);
        ev.GroupingHash = PrivacyScrubber.ScrubText(ev.GroupingHash);
        ev.User         = new User { Id = ReadInstallId() };
        ev["device"]    = WithoutHostname(ev.Device);
        CheckDeadline(clock);

        // The exceptions are built for this report alone, so their messages are edited in place. ErrorMessage has no
        // setter; "message" is the key it reads.
        foreach (var exception in ev.Exceptions ?? Array.Empty<Bugsnag.Payload.Exception>())
        {
            if (exception.TryGetValue("message", out var message) && message is string text)
                exception["message"] = PrivacyScrubber.ScrubText(text);
            CheckDeadline(clock);
        }

        // Breadcrumbs may be the client's shared buffer objects: replace them with scrubbed copies.
        if (ev.Breadcrumbs is { } breadcrumbs)
        {
            var copies = new List<Breadcrumb>();
            foreach (var crumb in breadcrumbs)
            {
                copies.Add(ScrubbedCopy(crumb));
                CheckDeadline(clock);
            }
            ev["breadcrumbs"] = copies;
        }

        // Metadata tabs may be shared (global metadata): replace each value with a scrubbed copy.
        if (ev.Metadata is { } metadata)
        {
            foreach (var tab in metadata.Keys.ToList())
            {
                metadata[tab] = ScrubValue(metadata[tab]);
                CheckDeadline(clock);
            }
        }
    }

    /// <summary>
    /// A copy of <paramref name="crumb"/> with its name and metadata scrubbed and every other field (type, timestamp)
    /// kept as it was.
    /// </summary>
    /// <param name="crumb">The breadcrumb to copy; not changed.</param>
    /// <returns>The scrubbed copy.</returns>
    private static Breadcrumb ScrubbedCopy(Breadcrumb crumb)
    {
        var copy = new Breadcrumb(string.Empty, BreadcrumbType.Manual);
        copy.Clear();
        foreach (var (key, value) in crumb)
            copy[key] = value;

        copy["name"] = PrivacyScrubber.ScrubText(crumb.Name);
        if (crumb.Metadata is { } metadata)
            copy["metaData"] = metadata.ToDictionary(kv => kv.Key, kv => PrivacyScrubber.ScrubText(kv.Value));
        return copy;
    }

    /// <summary>
    /// A scrubbed copy of a metadata value: a string is scrubbed; a dictionary or a list is copied with its values
    /// scrubbed the same way (keys are kept: they are TermPoint's own names); numbers, booleans, dates and enums are kept;
    /// any other object is replaced by its scrubbed text, so nothing unchecked can reach the payload.
    /// </summary>
    /// <param name="value">The value, or null.</param>
    /// <returns>The scrubbed copy, or null.</returns>
    private static object? ScrubValue(object? value) => value switch
    {
        null                       => null,
        string text                => PrivacyScrubber.ScrubText(text),
        bool or char or Enum       => value,
        DateTime or DateTimeOffset => value,
        _ when value.GetType().IsPrimitive || value is decimal => value,
        IDictionary dictionary     => dictionary.Keys.Cast<object>().ToDictionary(
                                          key => key.ToString() ?? string.Empty,
                                          key => ScrubValue(dictionary[key])),
        IEnumerable items          => items.Cast<object?>().Select(ScrubValue).ToList(),
        _                          => PrivacyScrubber.ScrubText(value.ToString()),
    };

    /// <summary>
    /// A copy of a device (an error report's or a session batch's) without its host name; null when there is none.
    /// </summary>
    /// <param name="device">The device, or null.</param>
    /// <returns>A new <see cref="Device"/> with every field but the host name.</returns>
    private static Device? WithoutHostname(object? device)
    {
        if (device is not IDictionary<string, object> fields)
            return null;

        // Device's constructor fills in this computer's host name (and locale, time zone, OS) by itself: start empty.
        var copy = new Device();
        copy.Clear();
        foreach (var (key, value) in fields)
            if (!string.Equals(key, HostnameKey, StringComparison.OrdinalIgnoreCase))
                copy[key] = value;
        return copy;
    }

    /// <summary>Reads the install ID; null (no user on the report) when it can't be read.</summary>
    /// <returns>The install ID, or null.</returns>
    private string? ReadInstallId()
    {
        try
        {
            var id = _installId();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    /// <summary>Gives up on the payload once scrubbing has taken longer than <see cref="TotalDeadline"/>.</summary>
    /// <param name="clock">Started when the payload arrived.</param>
    /// <exception cref="TimeoutException">The deadline has passed.</exception>
    private static void CheckDeadline(Stopwatch clock)
    {
        if (clock.Elapsed > TotalDeadline)
            throw new TimeoutException("Scrubbing the error report took too long.");
    }

    /// <summary>Records that a payload was dropped, in the local log only. Never throws.</summary>
    private void Drop()
    {
        try { _logLocally?.Invoke(DroppedMessage); }
        catch (System.Exception) { /* nothing more can be done */ }
    }
}
