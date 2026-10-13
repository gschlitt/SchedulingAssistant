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

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the body of the takeover prompt: who the inactive holder is, for how long they have been
    /// quiet, and the warning that their unsaved changes cannot go into the database if the user takes over.
    /// Missing fields fall back sensibly: no name and no machine reads "An unknown user"; a missing or
    /// unreadable lock (no real heartbeat) reads "for a while" instead of a number of minutes.
    /// </summary>
    /// <param name="holder">
    /// The lock holder as read from the lock file when it was judged inactive; null when unknown.
    /// </param>
    /// <param name="nowUtc">The current UTC time (a parameter so tests can fix it).</param>
    /// <returns>The prompt body; paragraphs are separated by a blank line.</returns>
    public static string BuildTakeOverMessage(LockFileData? holder, DateTime nowUtc)
    {
        var who = DescribeHolder(holder);

        // A holder with no heartbeat on record (null, an unreadable file's stand-in, or a file with the
        // field missing) has no age to report.
        var quiet = holder is null || holder.Heartbeat == DateTime.MinValue
            ? "for a while"
            : "for " + FormatMinutes((long)Math.Max(0, (nowUtc - holder.Heartbeat).TotalMinutes));

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Names the holder for the takeover prompt: "name on machine", just the name, "Someone on machine",
    /// or "An unknown user" when nothing usable is known.
    /// </summary>
    /// <param name="holder">The holder, or null.</param>
    /// <returns>A phrase that can start a sentence.</returns>
    private static string DescribeHolder(LockFileData? holder)
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
