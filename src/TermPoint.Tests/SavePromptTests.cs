using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SavePrompt"/>, the wording of the one banner a failed save shows (spec item 12). The texts
/// are pinned verbatim, together with the UX rule (rule 4 of the four rules for the shared database): every text is in
/// plain words, says the user's work is safe, and carries no "working copy", "local copy", raw exception text or path.
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
            "TermPoint couldn't save because the database location isn't responding. Your changes are kept.",
            SavePrompt.UnreachableMessage);
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
    /// "exception"), or shows a path.
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
        });

        foreach (var text in all)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Failed to", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
        }
    }

    /// <summary>The texts raised through <c>ForTransientFailure</c>.</summary>
    private static readonly string[] TransientTexts =
    {
        SavePrompt.InUseMessage, SavePrompt.UnreachableMessage, SavePrompt.PrepareFailedMessage,
        SavePrompt.WriteFailedMessage, SavePrompt.VerifyFailedMessage, SavePrompt.UnexpectedMessage
    };
}
