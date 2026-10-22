using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="RecoveryConflictPrompt"/>, the wording of the "can't restore unsaved changes" flow (spec
/// item 16, step 16a). The first two paragraphs of the conflict message are the text the prompt has always had and
/// are pinned verbatim; the third paragraph (how a kept version can replace the database) is the new, user-approved
/// text. Also pins the UX rule that the user is never told about a "working copy" or a "local copy".
/// </summary>
public sealed class RecoveryConflictPromptTests
{
    /// <summary>The prompt's title and both buttons are the wording the prompt has always had.</summary>
    [Fact]
    public void ConflictPrompt_TitleAndButtons_AreTheExistingWording()
    {
        Assert.Equal("Can't Restore Unsaved Changes", RecoveryConflictPrompt.ConflictTitle);
        Assert.Equal("Save a Copy…", RecoveryConflictPrompt.SaveCopyButton);
        Assert.Equal("Discard", RecoveryConflictPrompt.DiscardButton);
    }

    /// <summary>The save dialog's title is the one the dialog has always had.</summary>
    [Fact]
    public void SaveDialogTitle_IsTheExistingWording()
        => Assert.Equal("Save Recovered Copy", RecoveryConflictPrompt.SaveDialogTitle);

    /// <summary>
    /// The first two paragraphs of the conflict message are today's text, unchanged — the test spells them out
    /// rather than reading them from the constant, so an accidental edit is caught.
    /// </summary>
    [Fact]
    public void ConflictMessage_StartsWithTheExistingTwoParagraphs()
    {
        const string existing =
            "The database has been changed since your unsaved changes were made, " +
            "so they can't be restored automatically.\n\n" +
            "Would you like to save your version as a separate file instead? " +
            "You can open it later from Files → Open Database.";

        Assert.StartsWith(existing + "\n\n", RecoveryConflictPrompt.ConflictMessage);
    }

    /// <summary>
    /// The new third paragraph points to "Restore from a backup" in the database chooser and tells the user how to
    /// see the chooser at startup, naming the Preferences option exactly as it is labelled.
    /// </summary>
    [Fact]
    public void ConflictMessage_ExplainsHowAKeptVersionCanReplaceTheDatabase()
    {
        Assert.Equal(
            "The database has been changed since your unsaved changes were made, so they can't be restored automatically.\n\n" +
            "Would you like to save your version as a separate file instead? You can open it later from Files → Open Database.\n\n" +
            "If you and your colleagues decide to keep your version, it can replace the database: choose \"Restore from a backup\" " +
            "in the database chooser when TermPoint starts. (To see the chooser at startup, turn off " +
            "\"Always open the most recent database at startup\" in Preferences.)",
            RecoveryConflictPrompt.ConflictMessage);

        Assert.Contains("Restore from a backup", RecoveryConflictPrompt.ConflictMessage);
        Assert.Contains("Always open the most recent database at startup", RecoveryConflictPrompt.ConflictMessage);
    }

    /// <summary>
    /// The "Discard Unsaved Changes?" confirmation (data-persistence review, F1) is the approved wording, verbatim: one title and
    /// two buttons for both recovery questions, and a message that adds the way out ("Save a Copy…") only after the conflict question.
    /// The title is not the title of another dialog.
    /// </summary>
    [Fact]
    public void DiscardConfirmation_IsTheApprovedWording()
    {
        Assert.Equal("Discard Unsaved Changes?", RecoveryConflictPrompt.DiscardConfirmTitle);
        Assert.Equal("Discard", RecoveryConflictPrompt.DiscardConfirmButton);
        Assert.Equal("Go Back", RecoveryConflictPrompt.GoBackButton);
        Assert.Equal(
            "Your unsaved changes from the previous session will be deleted. This can't be undone.",
            RecoveryConflictPrompt.DiscardConfirmMessage);
        Assert.Equal(
            "Your unsaved changes from the previous session will be deleted. This can't be undone.\n\n" +
            "To keep them, go back and choose Save a Copy…",
            RecoveryConflictPrompt.DiscardConfirmWithCopyMessage);

        Assert.NotEqual(SaveConflictPrompt.DiscardConfirmTitle, RecoveryConflictPrompt.DiscardConfirmTitle);
        Assert.NotEqual(RecoveryConflictPrompt.ConflictTitle, RecoveryConflictPrompt.DiscardConfirmTitle);
    }

    /// <summary>The "choose a new name" message and its title are the approved wording, verbatim.</summary>
    [Fact]
    public void AlreadyExists_IsTheApprovedWording()
    {
        Assert.Equal("Choose a New Name", RecoveryConflictPrompt.AlreadyExistsTitle);
        Assert.Equal(
            "A file with that name already exists. Choose a new name — the recovered copy is never saved over an existing file.",
            RecoveryConflictPrompt.AlreadyExistsMessage);
    }

    /// <summary>The "export failed" message quotes the error when there is one and stays generic when there is not (today's wording).</summary>
    [Fact]
    public void ExportFailed_QuotesTheErrorWhenThereIsOne()
    {
        Assert.Equal("Export Failed", RecoveryConflictPrompt.ExportFailedTitle);
        Assert.Equal("Could not save the copy.", RecoveryConflictPrompt.ExportFailedMessage(null));
        Assert.Equal(
            "Could not save the copy: The disk is full.",
            RecoveryConflictPrompt.ExportFailedMessage(new IOException("The disk is full.")));
    }

    /// <summary>The suggested file name is the database's own name, no folder or extension, plus " (recovered).db".</summary>
    [Theory]
    [InlineData("BIOL-TT.db", "BIOL-TT (recovered).db")]
    [InlineData("Dept.v2.db", "Dept.v2 (recovered).db")]
    [InlineData("NoExtension", "NoExtension (recovered).db")]
    public void SuggestedFileName_IsTheDatabaseNamePlusRecovered(string fileName, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "shared folder", fileName);

        Assert.Equal(expected, RecoveryConflictPrompt.SuggestedFileName(path));
    }

    /// <summary>
    /// The UX rule: no text the user can see mentions a "working copy" or a "local copy", and none shows a path or
    /// uses the word "corrupt".
    /// </summary>
    [Fact]
    public void NoText_MentionsWorkingCopies_Paths_OrCorruption()
    {
        var texts = new[]
        {
            RecoveryConflictPrompt.ConflictTitle, RecoveryConflictPrompt.ConflictMessage,
            RecoveryConflictPrompt.SaveCopyButton, RecoveryConflictPrompt.DiscardButton,
            RecoveryConflictPrompt.DiscardConfirmTitle, RecoveryConflictPrompt.DiscardConfirmMessage,
            RecoveryConflictPrompt.DiscardConfirmWithCopyMessage, RecoveryConflictPrompt.DiscardConfirmButton,
            RecoveryConflictPrompt.GoBackButton,
            RecoveryConflictPrompt.SaveDialogTitle,
            RecoveryConflictPrompt.SuggestedFileName(Path.Combine(Path.GetTempPath(), "BIOL-TT.db")),
            RecoveryConflictPrompt.AlreadyExistsTitle, RecoveryConflictPrompt.AlreadyExistsMessage,
            RecoveryConflictPrompt.ExportFailedTitle,
            RecoveryConflictPrompt.ExportFailedMessage(null),
            RecoveryConflictPrompt.ExportFailedMessage(new IOException("The disk is full."))
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corrupt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
        }
    }
}
