using System;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// The wording of the stale-lock takeover prompt (spec item 9a) and of the follow-up shown when the
/// holder turns out to be active after all. Kept out of <c>MainWindow</c> so the text, including the
/// fallbacks for missing fields, can be unit-tested without any UI.
///
/// <para><b>Why the warning is there.</b> A lock that has not been renewed for a few minutes usually
/// belongs to a laptop that is asleep or off the network, and that laptop may be holding unsaved work.
/// If the user takes the lock over, the sleeper's changes can no longer go into the database: when it
/// wakes it finds the lock gone, drops to read-only, and keeps its unsaved changes aside. They are
/// offered back the next time that person opens the database with write access — restored if the
/// database has not changed in the meantime, otherwise as a separate copy to save
/// (<c>MainWindow.HandleRecoveryConflictAsync</c>). The prompt says so, so nobody takes over casually.</para>
/// </summary>
public static class StaleLockPrompt
{
    // ── Shared user-facing text ───────────────────────────────────────────────

    /// <summary>Title of the takeover prompt shown when the lock holder looks inactive.</summary>
    public const string TakeOverTitle = "Database Lock Is Inactive";

    /// <summary>Label of the takeover prompt's confirming button.</summary>
    public const string TakeOverButton = "Take over";

    /// <summary>Label of the takeover prompt's declining button (also what closing the dialog means).</summary>
    public const string OpenReadOnlyButton = "Open read-only";

    /// <summary>Title of the message shown when, at click time, the holder proved to be active again.</summary>
    public const string ActiveAgainTitle = "Database Is In Use Again";

    /// <summary>
    /// Title of the short wait dialog shown after "Take over" when the holder's heartbeat has not yet been
    /// seen standing still long enough (spec item 9b).
    /// </summary>
    public const string WaitTitle = "Checking the Lock";

    /// <summary>Label of the wait dialog's button; cancelling is the same as choosing "Open read-only".</summary>
    public const string WaitCancelButton = "Cancel";

    /// <summary>
    /// The longest quiet time, in minutes, the takeover prompt will state. Beyond this (a year) the figure
    /// can only come from a wrong clock, so the prompt says "for a while" instead.
    /// </summary>
    private const double MaxPlausibleQuietMinutes = 365d * 24 * 60;

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the body of the takeover prompt: who the inactive holder is, for how long they have been
    /// quiet, and the warning that their unsaved changes cannot go into the database if the user takes over.
    /// Missing fields fall back sensibly: no name and no machine reads "An unknown user"; a missing or
    /// unreadable lock (no real heartbeat) reads "for a while" instead of a number of minutes.
    ///
    /// <para>The quiet time is only descriptive: it compares the heartbeat timestamp in the lock file with
    /// this computer's clock (<paramref name="nowUtc"/>), and the two computers' clocks can disagree. It
    /// is never what decides whether the lock may be taken over (spec item 9b). When the difference is
    /// negative (the holder's clock is ahead), under a minute (the holder's clock is slightly ahead, so
    /// "0 minutes" would be nonsense for a lock judged inactive), or beyond a year, it reads "for a while".</para>
    /// </summary>
    /// <param name="holder">
    /// The lock holder as read from the lock file when it was judged inactive; null when unknown.
    /// </param>
    /// <param name="nowUtc">The current UTC time (a parameter so tests can fix it).</param>
    /// <returns>The prompt body; paragraphs are separated by a blank line.</returns>
    public static string BuildTakeOverMessage(LockFileData? holder, DateTime nowUtc)
    {
        var who = DescribeHolder(holder);

        var quiet = DescribeQuietTime(holder, nowUtc);

        return $"{who} hasn't been active in this database {quiet}. " +
               "Their computer may be asleep or disconnected.\n\n" +
               "If you take over, changes they haven't saved yet can't go into the database " +
               "— they'll only be able to save them as a separate copy.";
    }

    /// <summary>
    /// Builds the body of the message shown when the takeover was refused at click time because the
    /// holder has become active again (its heartbeat advanced, or another session took the lock).
    /// </summary>
    /// <param name="holder">
    /// The lock holder as re-read at click time; null (or without a name) when it could not be read,
    /// in which case the message says "Someone".
    /// </param>
    /// <returns>The message body.</returns>
    public static string BuildActiveAgainMessage(LockFileData? holder)
        => $"{NameOrNull(holder) ?? "Someone"} is active in this database again, so it will open read-only.";

    /// <summary>
    /// Builds the body of the wait dialog shown when the user chose "Take over" before the holder's
    /// heartbeat had stood still long enough to be sure (spec item 9b): "Checking that jsmith isn't still
    /// active in this database… (up to 45 seconds)". Without a usable name it says "the other user".
    /// </summary>
    /// <param name="holder">The lock holder the prompt was about; null when unknown.</param>
    /// <param name="remaining">The longest the check can still take; shown rounded UP to whole seconds.</param>
    /// <returns>The dialog body.</returns>
    public static string BuildWaitMessage(LockFileData? holder, TimeSpan remaining)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"Checking that {NameOrNull(holder) ?? "the other user"} isn't still active in this database… " +
               $"(up to {seconds} {(seconds == 1 ? "second" : "seconds")})";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The phrase for how long the holder has been quiet: "for N minute(s)" from the heartbeat timestamp,
    /// or "for a while" when there is no usable timestamp or the difference cannot be trusted (see
    /// <see cref="BuildTakeOverMessage"/>).
    /// </summary>
    /// <param name="holder">The holder, or null.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    private static string DescribeQuietTime(LockFileData? holder, DateTime nowUtc)
    {
        // A holder with no heartbeat on record (null, an unreadable file's stand-in, or a file with the
        // field missing) has no age to report.
        if (holder is null || holder.Heartbeat == DateTime.MinValue)
            return "for a while";

        var minutes = (nowUtc - holder.Heartbeat).TotalMinutes;
        return minutes < 1 || minutes > MaxPlausibleQuietMinutes
            ? "for a while"
            : "for " + FormatMinutes((long)minutes);
    }

    /// <summary>
    /// Names the holder for the takeover prompt: "name on machine", just the name, "Someone on machine",
    /// or "An unknown user" when nothing usable is known. Shared with <see cref="RestorePrompt"/>, which names the
    /// holder who blocked a restore the same way.
    /// </summary>
    /// <param name="holder">The holder, or null.</param>
    /// <returns>A phrase that can start a sentence.</returns>
    internal static string DescribeHolder(LockFileData? holder)
    {
        var name    = NameOrNull(holder);
        var machine = UsableOrNull(holder?.Machine);

        return (name, machine) switch
        {
            (not null, not null) => $"{name} on {machine}",
            (not null, null)     => name,
            (null, not null)     => $"Someone on {machine}",
            _                    => "An unknown user"
        };
    }

    /// <summary>The holder's user name, or null when blank or the placeholder of an unreadable lock file.</summary>
    /// <param name="holder">The holder, or null.</param>
    private static string? NameOrNull(LockFileData? holder) => UsableOrNull(holder?.Username);

    /// <summary>
    /// Returns <paramref name="text"/> trimmed, or null when it is blank or is the placeholder
    /// (<see cref="WriteLockService.CorruptedHolderLabel"/>) written for an unreadable lock file.
    /// </summary>
    /// <param name="text">A user or machine name from a lock file; may be null.</param>
    private static string? UsableOrNull(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed == WriteLockService.CorruptedHolderLabel
            ? null
            : trimmed;
    }

    /// <summary>Formats a minute count with the right singular or plural: "1 minute", "5 minutes".</summary>
    /// <param name="minutes">Whole minutes; zero or more.</param>
    private static string FormatMinutes(long minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";
}
