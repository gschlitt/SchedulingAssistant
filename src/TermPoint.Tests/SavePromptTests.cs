using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SavePrompt"/>, the wording of the one banner a failed save shows (spec item 12) and, since
/// spec item 28, of the one sticky "can't reach the database location" notice (finding F15). The texts
/// are pinned verbatim, together with the UX rule (rule 4 of the four rules for the shared database): every text is in
/// plain words, says the user's work is kept, and carries no "working copy", "local copy", raw exception text or path.
/// </summary>
public sealed class SavePromptTests
{
    /// <summary>Every constant has exactly the approved wording.</summary>
    [Fact]
    public void EveryText_IsTheApprovedWording()
    {
        Assert.Equal(
            "TermPoint couldn't save because the database file is in use by another program or can't be accessed. Your changes are kept.",
            SavePrompt.InUseMessage);
        Assert.Equal(
            "TermPoint couldn't prepare the save on this computer. Your changes are kept.",
            SavePrompt.PrepareFailedMessage);
        Assert.Equal(
            "TermPoint couldn't save because writing to the database location failed. Your changes are kept.",
            SavePrompt.WriteFailedMessage);
        Assert.Equal(
            "TermPoint couldn't save because the file it wrote to the database location didn't pass its check. " +
            "The database wasn't changed, and your changes are kept.",
            SavePrompt.VerifyFailedMessage);
        Assert.Equal(
            "TermPoint couldn't save because of an unexpected problem. Your changes are kept.",
            SavePrompt.UnexpectedMessage);
        Assert.Equal(
            "TermPoint couldn't save because one of its files for this session is missing, so changes since your " +
            "last save can't be saved. Close TermPoint and open the database again.",
            SavePrompt.SessionFileMissingMessage);
        Assert.Equal(
            "TermPoint couldn't create its crash-protection file for this session. Saving still works normally, " +
            "but unsaved changes may not be recoverable after a crash — save your work frequently.",
            SavePrompt.CrashProtectionMessage);
        Assert.Equal(" TermPoint will try again automatically.", SavePrompt.AutoSaveRetrySuffix);
        Assert.Equal(" Try saving again in a moment.", SavePrompt.ManualRetrySuffix);
    }

    /// <summary>
    /// The outage notice (spec item 28, F15): the approved wording, pinned verbatim for every variant: unsaved changes
    /// with autosave on, unsaved changes with autosave off, and no unsaved changes (the autosave setting then makes no
    /// difference).
    /// </summary>
    [Fact]
    public void OutageNotice_IsTheApprovedWording_ForEveryVariant()
    {
        Assert.Equal(
            "TermPoint can't reach the database location. Your changes are kept on this computer.",
            SavePrompt.OutageWithChangesMessage);
        Assert.Equal(" TermPoint will try again automatically.", SavePrompt.OutageAutoSaveSuffix);
        Assert.Equal(" Press Save once the connection is back.", SavePrompt.OutageManualSuffix);
        Assert.Equal(
            "TermPoint can't reach the database location right now. You have no unsaved changes.",
            SavePrompt.OutageNoChangesMessage);

        Assert.Equal(
            "TermPoint can't reach the database location. Your changes are kept on this computer. " +
            "TermPoint will try again automatically.",
            SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: true));
        Assert.Equal(
            "TermPoint can't reach the database location. Your changes are kept on this computer. " +
            "Press Save once the connection is back.",
            SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false));
        Assert.Equal(
            "TermPoint can't reach the database location right now. You have no unsaved changes.",
            SavePrompt.OutageNotice(hasUnsavedChanges: false, autoSaveEnabled: true));
        Assert.Equal(
            "TermPoint can't reach the database location right now. You have no unsaved changes.",
            SavePrompt.OutageNotice(hasUnsavedChanges: false, autoSaveEnabled: false));
    }

    /// <summary>
    /// <see cref="SavePrompt.IsOutageNotice"/> recognises exactly the three outage texts: every other save-failure
    /// text, the lock-lost banner, a refresh line, an empty string and null are not outage notices, so "the location
    /// responds again" can never clear them.
    /// </summary>
    [Fact]
    public void IsOutageNotice_RecognisesTheThreeOutageTexts_AndNothingElse()
    {
        Assert.True(SavePrompt.IsOutageNotice(SavePrompt.OutageNotice(true, true)));
        Assert.True(SavePrompt.IsOutageNotice(SavePrompt.OutageNotice(true, false)));
        Assert.True(SavePrompt.IsOutageNotice(SavePrompt.OutageNotice(false, false)));

        Assert.False(SavePrompt.IsOutageNotice(null));
        Assert.False(SavePrompt.IsOutageNotice(""));
        Assert.False(SavePrompt.IsOutageNotice(SavePrompt.InUseMessage));
        Assert.False(SavePrompt.IsOutageNotice(SavePrompt.ForTransientFailure(SavePrompt.WriteFailedMessage, isAutoSave: true)));
        Assert.False(SavePrompt.IsOutageNotice(SavePrompt.SessionFileMissingMessage));
        Assert.False(SavePrompt.IsOutageNotice(SavePrompt.CrashProtectionMessage));
        Assert.False(SavePrompt.IsOutageNotice(LockLossPrompt.Banner(true, "alice", "ALICE-PC", hadUnsavedChanges: true)));
        Assert.False(SavePrompt.IsOutageNotice(RefreshPrompt.NotResponding(null)));
        // A text that is only the first part of an outage notice is not one.
        Assert.False(SavePrompt.IsOutageNotice(SavePrompt.OutageWithChangesMessage));
    }

    /// <summary>ForTransientFailure appends the retry note that fits how the save started.</summary>
    [Theory]
    [InlineData(true,  " TermPoint will try again automatically.")]
    [InlineData(false, " Try saving again in a moment.")]
    public void ForTransientFailure_AppendsTheRightSuffix(bool isAutoSave, string expectedSuffix)
        => Assert.Equal(SavePrompt.InUseMessage + expectedSuffix, SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave));

    /// <summary>
    /// The texts that are retried (they take a suffix) all end with the promise "Your changes are kept." The
    /// verification-failed text, by approved wording, says it mid-sentence ("…, and your changes are kept."), so the
    /// comparison ignores case.
    /// </summary>
    [Fact]
    public void EveryTransientText_SaysTheChangesAreKept()
    {
        foreach (var text in TransientTexts)
            Assert.Contains("your changes are kept.", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The UX rule: no text mentions a "working copy" or a "local copy", quotes a raw exception ("Failed to…",
    /// "exception"), or shows a path. The outage texts are included.
    /// </summary>
    [Fact]
    public void NoText_MentionsWorkingCopies_RawExceptions_OrPaths()
    {
        var all = TransientTexts.Concat(new[]
        {
            SavePrompt.SessionFileMissingMessage, SavePrompt.CrashProtectionMessage,
            SavePrompt.AutoSaveRetrySuffix, SavePrompt.ManualRetrySuffix,
            SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, true),
            SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, false)
        }).Concat(OutageTexts);

        foreach (var text in all)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Failed to", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
        }
    }

    /// <summary>
    /// The outage notice's own rules (spec item 28, "Rules for every text"): no "safe", "lock file", "D'" or
    /// "write access", and the variants that have unsaved changes say where they are kept.
    /// </summary>
    [Fact]
    public void OutageTexts_KeepTheRulesOfRule4()
    {
        foreach (var text in OutageTexts)
        {
            Assert.DoesNotContain("safe", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lock file", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("D'", text);
            Assert.DoesNotContain("write access", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SQLite", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("kept on this computer", SavePrompt.OutageNotice(true, true));
        Assert.Contains("kept on this computer", SavePrompt.OutageNotice(true, false));
        Assert.Contains("no unsaved changes", SavePrompt.OutageNotice(false, true));
    }

    /// <summary>The texts raised through <c>ForTransientFailure</c>.</summary>
    private static readonly string[] TransientTexts =
    {
        SavePrompt.InUseMessage, SavePrompt.PrepareFailedMessage,
        SavePrompt.WriteFailedMessage, SavePrompt.VerifyFailedMessage, SavePrompt.UnexpectedMessage
    };

    /// <summary>Every text the outage notice can show: the three whole notices and their parts.</summary>
    private static readonly string[] OutageTexts =
    {
        SavePrompt.OutageNotice(true, true), SavePrompt.OutageNotice(true, false), SavePrompt.OutageNotice(false, true),
        SavePrompt.OutageWithChangesMessage, SavePrompt.OutageAutoSaveSuffix, SavePrompt.OutageManualSuffix,
        SavePrompt.OutageNoChangesMessage
    };
}
