using System.Text.RegularExpressions;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>What a message promises (invariant I5, "messages tell the truth").</summary>
internal enum PromiseKind
{
    /// <summary>"TermPoint will offer to restore them the next time ...": at the computer's next edit-access open, Restore must really restore the work.</summary>
    WillOfferRestore,

    /// <summary>"Your version was saved as X": the file X must hold the computer's unsaved edits.</summary>
    SavedAs,

    /// <summary>"The database wasn't changed": the shared database's edits are the same before and after the step.</summary>
    DatabaseUnchanged,

    /// <summary>"Your changes are kept": the computer's unsaved edits are still somewhere that survives a crash after the step.</summary>
    ChangesKept,

    /// <summary>Informational: it states a fact and promises nothing the harness can check.</summary>
    Info
}

/// <summary>
/// The catalogue of message texts the harness knows (spec item 9.3, step 9.3b; invariant I5). Each entry says "a message
/// with this title (or any title) and this distinctive phrase makes this promise". Matching is by title plus phrase,
/// not by the whole text, so wording tweaks that keep the phrase do not break the harness, while a NEW message that
/// mentions kept / keep / restore / saved / safe but matches nothing is itself a violation (an "unclassified
/// promise"): new wording cannot slip past unexamined.
///
/// <para>The texts are read from the real prompt classes where those hold them as constants; the others (inline
/// strings in <c>SessionLifecycle</c>, such as "Unsaved Changes Kept" and "Changes Not Saved") are matched by their
/// distinctive phrase.</para>
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
        // (a) A restore will be offered later. Both "Changes Not Saved" messages, "Unsaved Changes Kept" and the lock-lost banner.
        new(null, "will offer to restore", PromiseKind.WillOfferRestore),
        new(null, "will ask about them",   PromiseKind.WillOfferRestore),

        // (b) The user's version was saved as a file.
        new(SaveConflictPrompt.SavedTitle, "was saved as", PromiseKind.SavedAs),

        // (c) The shared database is unchanged (save verification failure, restore refusals).
        new(null, "database wasn't changed", PromiseKind.DatabaseUnchanged),
        new(null, "database is unchanged",   PromiseKind.DatabaseUnchanged),

        // The save failures all say "Your changes are kept." (SavePrompt).
        new(null, "your changes are kept", PromiseKind.ChangesKept),

        // (d) Informational: facts, or statements about the database rather than about unsaved work.
        new(LocalCopyCheckPrompt.UnsavedChangesLostTitle, "", PromiseKind.Info),   // "... could not be restored": a loss admitted, nothing promised
        new(LocalCopyCheckPrompt.CouldntOpenTitle,        "", PromiseKind.Info),
        new("Backup Restored",       "", PromiseKind.Info),
        new("Restore Not Confirmed", "", PromiseKind.Info),
        new("Restore Stopped",       "", PromiseKind.Info),
        new("Restore Not Started",   "", PromiseKind.Info),

        // The banner after an externally removed lock file was put back ("... TermPoint has restored it and your
        // session continues normally"): it states a fact about the lock file, and promises nothing about unsaved work.
        new(null, "has restored it and your session continues", PromiseKind.Info),
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
