using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SaveConflictPrompt"/>, the wording of the "your changes can't be saved" banner and the
/// choices that follow it (spec item 10, step 10a). Every text is pinned verbatim, and the UX rule that the user is
/// never told about a "working copy" or a "local copy" (and never shown a path) is enforced.
/// </summary>
public sealed class SaveConflictPromptTests
{
    /// <summary>The banner text and both buttons are the user-approved wording, verbatim.</summary>
    [Fact]
    public void Banner_IsTheApprovedWording()
    {
        Assert.Equal(
            "TermPoint can't save your changes: the database was changed elsewhere since you opened it. " +
            "Save your version as a separate file, or discard your changes, " +
            "and TermPoint will reopen the current database.",
            SaveConflictPrompt.BannerMessage);
        Assert.Equal("Save a Copy…", SaveConflictPrompt.SaveCopyButton);
        Assert.Equal("Discard Changes", SaveConflictPrompt.DiscardChangesButton);
    }

    /// <summary>The wait dialog's title and message are pinned.</summary>
    [Fact]
    public void WaitDialog_IsTheApprovedWording()
    {
        Assert.Equal("Saving Your Version", SaveConflictPrompt.SavingTitle);
        Assert.Equal("Saving your version…", SaveConflictPrompt.SavingMessage);
    }

    /// <summary>The "saved" message names the file it is given, points to Files → Open Database, and says what happens next.</summary>
    [Fact]
    public void SavedMessage_NamesTheFile_AndSaysWhatHappensNext()
    {
        Assert.Equal("Your Version Was Saved", SaveConflictPrompt.SavedTitle);
        Assert.Equal(
            "Your version was saved as \"BIOL-TT (recovered).db\". You can open it any time from Files → Open Database. " +
            "TermPoint will now reopen the current database.",
            SaveConflictPrompt.SavedMessage("BIOL-TT (recovered).db"));
        Assert.Contains("mine.db", SaveConflictPrompt.SavedMessage("mine.db"));
    }

    /// <summary>The Discard confirmation is pinned: it says what is lost, that it cannot be undone, and has Discard / Cancel buttons.</summary>
    [Fact]
    public void DiscardConfirmation_IsTheApprovedWording()
    {
        Assert.Equal("Discard Changes?", SaveConflictPrompt.DiscardConfirmTitle);
        Assert.Equal(
            "Discard the changes you made since your last save? This can't be undone.",
            SaveConflictPrompt.DiscardConfirmMessage);
        Assert.Equal("Discard", SaveConflictPrompt.DiscardConfirmButton);
        Assert.Equal("Cancel", SaveConflictPrompt.DiscardCancelButton);
    }

    /// <summary>The editor-open refusal is pinned.</summary>
    [Fact]
    public void EditorOpenRefusal_IsTheApprovedWording()
    {
        Assert.Equal("Finish Editing First", SaveConflictPrompt.EditorOpenTitle);
        Assert.Equal("Apply or cancel the open edit first.", SaveConflictPrompt.EditorOpenMessage);
    }

    /// <summary>
    /// The UX rule: no text the user can see mentions a "working copy" or a "local copy", and none shows a path
    /// (a drive letter, or a folder separator).
    /// </summary>
    [Fact]
    public void NoText_MentionsWorkingCopies_OrPaths()
    {
        var texts = new[]
        {
            SaveConflictPrompt.BannerMessage, SaveConflictPrompt.SaveCopyButton, SaveConflictPrompt.DiscardChangesButton,
            SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage,
            SaveConflictPrompt.SavedTitle, SaveConflictPrompt.SavedMessage("BIOL-TT (recovered).db"),
            SaveConflictPrompt.DiscardConfirmTitle, SaveConflictPrompt.DiscardConfirmMessage,
            SaveConflictPrompt.DiscardConfirmButton, SaveConflictPrompt.DiscardCancelButton,
            SaveConflictPrompt.EditorOpenTitle, SaveConflictPrompt.EditorOpenMessage
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
            Assert.DoesNotContain("\\", text);
            Assert.DoesNotContain("/", text);
        }
    }
}
