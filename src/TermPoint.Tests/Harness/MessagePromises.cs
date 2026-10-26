using System.Text.RegularExpressions;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>What a message promises (invariant I5, "messages tell the truth").</summary>
internal enum PromiseKind
{
    /// <summary>
    /// "TermPoint will offer to add them to the database the next time you open it for editing. If someone else changes the
    /// database first, they can't be added, but you'll be able to save them as a separate copy" (the wording of spec item 28,
    /// the four kept-changes messages; the production flow behind it is "Restore Unsaved Changes?" and, when the database
    /// changed, "Can't Restore Unsaved Changes" with Save a Copy…). At the computer's next edit-access open of the same
    /// database the work must come back through Restore, OR the database must really have changed (someone else's DATA is in
    /// it now) so that the can't-restore question, with its copy, is the promised alternative. A refusal when nobody changed
    /// any data (finding F5's shape) breaks the promise. The name is kept from the old wording ("will offer to restore").
    /// </summary>
    WillOfferRestore,

    /// <summary>"Your version was saved as X": the file X must hold the computer's unsaved edits.</summary>
    SavedAs,

    /// <summary>"The database wasn't changed": the shared database's edits are the same before and after the step.</summary>
    DatabaseUnchanged,

    /// <summary>"Your changes are kept": the computer's unsaved edits are still somewhere that survives a crash after the step.</summary>
    ChangesKept,

    /// <summary>Informational: it states a fact (for example "You have no unsaved changes") and promises nothing the harness judges.</summary>
    Info
}

/// <summary>
/// The catalogue of message texts the harness knows (spec item 9.3, step 9.3b; invariant I5). Each entry says "a message
/// with this title (or any title) and this distinctive phrase makes this promise". Matching is by title plus phrase,
/// not by the whole text, so wording tweaks that keep the phrase do not break the harness, while a NEW message that
/// mentions kept / keep / restore / saved / safe but matches nothing is itself a violation (an "unclassified
/// promise"): new wording cannot slip past unexamined.
///
/// <para>The titles are read from the real prompt classes where those hold them as constants; the texts are matched by
/// their distinctive phrase. Since spec item 28 the kept-changes messages ("Unsaved Changes Kept", both "Changes Not
/// Saved", the lock-lost banner) live in <see cref="KeptChangesPrompt"/> and <see cref="LockLossPrompt"/>, and the
/// outage notice in <see cref="SavePrompt"/>; the catalogue's phrases describe THEIR promises, not the old ones.</para>
/// </summary>
internal static class MessagePromises
{
    /// <summary>One catalogue entry.</summary>
    /// <param name="Title">The exact title (case-insensitive), or null for any title (banners have none).</param>
    /// <param name="Phrase">A phrase the text contains (case-insensitive); empty to match on the title alone.</param>
    /// <param name="Kind">The promise.</param>
    private sealed record Entry(string? Title, string Phrase, PromiseKind Kind);

    /// <summary>Words that make a message a promise-bearer: it talks about work being kept, restored, saved or safe.</summary>
    private static readonly Regex PromiseWords = new(
        @"\b(kept|keep|keeps|restore|restored|restoring|saved|safe)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The known messages.</summary>
    private static readonly Entry[] Entries =
    {
        // (a) The kept changes will be offered back later (spec item 28's wording): both "Changes Not Saved" messages,
        // "Unsaved Changes Kept" and the lock-lost banner all say "TermPoint will offer to add them to the database [or to "name"]
        // the next time you open it for editing. If someone else changes the database first, they can't be added, but you'll be
        // able to save them as a separate copy." The old phrases ("will offer to restore", "will ask about them") are
        // deliberately GONE from the catalogue: if the old wording ever comes back it mentions kept / restore and matches nothing,
        // so the harness reports it as an unclassified promise instead of judging it against the new promise.
        new(null, "will offer to add them", PromiseKind.WillOfferRestore),

        // (b) The user's version was saved as a file.
        new(SaveConflictPrompt.SavedTitle, "was saved as", PromiseKind.SavedAs),

        // (c) The shared database is unchanged (save verification failure, restore refusals).
        new(null, "database wasn't changed", PromiseKind.DatabaseUnchanged),
        new(null, "database is unchanged",   PromiseKind.DatabaseUnchanged),

        // The save failures all say "Your changes are kept." (SavePrompt).
        new(null, "your changes are kept", PromiseKind.ChangesKept),

        // The ONE outage notice (SavePrompt.OutageNotice, spec item 28, finding F15), raised for any save that cannot reach the share
        // and by the heartbeat's lock check ("Unreachable"). With unsaved changes it reads "TermPoint can't reach the database location.
        // Your changes are kept on this computer." plus, with autosave on, " TermPoint will try again automatically." or, with autosave
        // off, " Press Save once the connection is back." (step 9.3d: the first deep run met the OLD heartbeat banner, "Network connection
        // interrupted — your changes are safe locally...", as an unclassified promise; that wording and its second message are gone.)
        // What it promises: the changes are kept on this computer (kept; retried automatically when autosave is on). I5 judges the
        // first half at the end of the step (the unsaved edits are still somewhere that survives a crash) and I2 judges it again after
        // every later step; the retry ("will try again automatically") depends on a later autosave tick and is not separately judged.
        new(null, "can't reach the database location. your changes are kept on this computer", PromiseKind.ChangesKept),

        // The same notice when the session has NO unsaved changes (possible from the heartbeat): "TermPoint can't reach the database
        // location right now. You have no unsaved changes." It states a fact computed from the same marker the ledger reads, and
        // promises nothing about work, so it is Info. (That it clears when a save works or the lock check reaches the location again
        // is behaviour of the window, pinned by SaveErrorBannerTests and CheckoutServiceTests, not a promise about edits.)
        new(null, "you have no unsaved changes", PromiseKind.Info),

        // (d) Informational: facts, or statements about the database rather than about unsaved work.
        new(LocalCopyCheckPrompt.UnsavedChangesLostTitle, "", PromiseKind.Info),   // "... could not be restored": a loss admitted, nothing promised
        new(LocalCopyCheckPrompt.CouldntOpenTitle,        "", PromiseKind.Info),
        new("Backup Restored",       "", PromiseKind.Info),
        new("Restore Not Confirmed", "", PromiseKind.Info),
        new("Restore Stopped",       "", PromiseKind.Info),
        new("Restore Not Started",   "", PromiseKind.Info),

        // Found by the step 9.3d sweep of every user-facing text in the save, lock, heartbeat, exit, switch, restore and recovery paths
        // (a message dialog or a banner that mentions kept / restored / saved work but promises nothing the harness can check):
        // the save-failure banner for a missing session file ("... changes since your last save can't be saved"), which admits a loss;
        new(null, "changes since your last save can't be saved", PromiseKind.Info),
        // the banner after a lost-lock demotion could not reconnect ("The database connection could not be restored after losing write access");
        new(null, "could not be restored after losing write access", PromiseKind.Info),
        // and "Choose a New Name" (the recovered copy is never saved over an existing file), a rule about file names.
        new(RecoveryConflictPrompt.AlreadyExistsTitle, "", PromiseKind.Info),

        // The banner after an externally removed lock file was put back (LockLossPrompt.LockFilePutBack, "... TermPoint has put it
        // back, and you can carry on as normal"): it states a fact about the file TermPoint uses to mark the database as in use, and
        // promises nothing about unsaved work. (It has no promise word, so it needs no entry to avoid "unclassified"; the entry
        // documents that it was looked at.)
        new(null, "termpoint has put it back", PromiseKind.Info),
    };

    /// <summary>The result of looking a message up in the catalogue.</summary>
    /// <param name="Kinds">The promises it makes (several entries can match one message); empty when none matched.</param>
    /// <param name="MentionsPromiseWords">True when the text mentions kept / keep / restore / saved / safe.</param>
    internal sealed record MessageClass(IReadOnlyList<PromiseKind> Kinds, bool MentionsPromiseWords)
    {
        /// <summary>True when the message talks about kept / restored / saved / safe work but matches no catalogue entry.</summary>
        public bool IsUnclassifiedPromise => Kinds.Count == 0 && MentionsPromiseWords;
    }

    /// <summary>Looks a message up in the catalogue.</summary>
    /// <param name="title">The message's title (empty for a banner).</param>
    /// <param name="text">The message's text.</param>
    /// <returns>The promises it makes, and whether it needs classifying.</returns>
    public static MessageClass Classify(string title, string text)
    {
        var kinds = Entries
            .Where(e => (e.Title is null || string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase))
                        && text.Contains(e.Phrase, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Kind)
            .Distinct()
            .ToList();
        return new MessageClass(kinds, PromiseWords.IsMatch(text));
    }

    /// <summary>
    /// Pulls the file name out of a "... was saved as "X" ..." message.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <returns>The quoted file name, or null when the text has no <c>was saved as "..."</c>.</returns>
    public static string? SavedFileName(string text)
    {
        var match = Regex.Match(text, "was saved as \"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The first sentence of a text (for the story): up to the first full stop followed by a space, or the first line break.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The first sentence, without its closing full stop.</returns>
    public static string FirstSentence(string text)
    {
        var trimmed = text.Trim();
        var cut = trimmed.Length;
        var newline = trimmed.IndexOf('\n');
        if (newline >= 0) cut = Math.Min(cut, newline);
        var stop = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (stop >= 0) cut = Math.Min(cut, stop);
        return trimmed[..cut].TrimEnd('.', ' ');
    }
}
