using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="KeptChangesPrompt"/>, the wording of the four messages that tell the user their unsaved
/// changes were kept (spec item 28; data-persistence review finding F6): the lock-lost banner's kept-changes
/// sentences, "Unsaved Changes Kept", and both "Changes Not Saved" messages. Every text is pinned VERBATIM (the wording
/// was approved by Greg on 2026-10-26), with the UX rule of rule 4 of the four rules for the shared database: plain
/// words, no path, no log text, and none of "working copy", "local copy", "safe", "D'", "lock file" or "write access".
/// </summary>
public sealed class KeptChangesPromptTests
{
    /// <summary>The shared sentences are exactly the approved wording.</summary>
    [Fact]
    public void SharedSentences_AreTheApprovedWording()
    {
        Assert.Equal("They're kept on this computer.", KeptChangesPrompt.KeptSentence);
        Assert.Equal("Your changes since your last save are kept on this computer.", KeptChangesPrompt.ChangesSinceLastSaveKept);
        Assert.Equal(
            "TermPoint will offer to add them to the database the next time you open it for editing. " +
            "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.",
            KeptChangesPrompt.OfferToAdd);
    }

    /// <summary>The named version of the promise puts the database's name, in straight double quotes, where "the database" was.</summary>
    [Fact]
    public void OfferToAddTo_NamesTheDatabase()
    {
        Assert.Equal(
            "TermPoint will offer to add them to \"BIOL-TT\" the next time you open it for editing. " +
            "If someone else changes it first, they can't be added, but you'll be able to save them as a separate copy.",
            KeptChangesPrompt.OfferToAddTo("BIOL-TT"));
    }

    /// <summary>"Unsaved Changes Kept": the title and the body are exactly the approved wording.</summary>
    [Fact]
    public void UnsavedChangesKept_IsTheApprovedWording()
    {
        Assert.Equal("Unsaved Changes Kept", KeptChangesPrompt.UnsavedChangesKeptTitle);
        Assert.Equal(
            "Someone else is editing this database right now, so your unsaved changes from last time can't be added yet. " +
            "They're kept on this computer. " +
            "TermPoint will offer to add them to the database the next time you open it for editing. " +
            "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.",
            KeptChangesPrompt.UnsavedChangesKeptMessage);
    }

    /// <summary>"Changes Not Saved" when switching database: the title, and the body naming the database being left.</summary>
    [Fact]
    public void ChangesNotSavedOnSwitch_IsTheApprovedWording()
    {
        Assert.Equal("Changes Not Saved", KeptChangesPrompt.ChangesNotSavedTitle);
        Assert.Equal(
            "Your latest changes to \"BIOL-TT\" couldn't be saved. They're kept on this computer. " +
            "TermPoint will offer to add them to \"BIOL-TT\" the next time you open it for editing. " +
            "If someone else changes it first, they can't be added, but you'll be able to save them as a separate copy.",
            KeptChangesPrompt.ChangesNotSavedOnSwitch("BIOL-TT"));
    }

    /// <summary>"Changes Not Saved" when closing: the body is exactly the approved wording.</summary>
    [Fact]
    public void ChangesNotSavedOnClose_IsTheApprovedWording()
        => Assert.Equal(
            "Your latest changes couldn't be saved to the database. They're kept on this computer. " +
            "TermPoint will offer to add them to the database the next time you open it for editing. " +
            "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.",
            KeptChangesPrompt.ChangesNotSavedOnClose);

    /// <summary>
    /// The rules for every new text: no path, no raw exception or log text, no "working copy", "local copy", "safe",
    /// "D'", "lock file" or "write access", no doubled spaces, and no leftover of the old promise ("restore them").
    /// </summary>
    [Fact]
    public void NoText_BreaksTheRulesOfRule4()
    {
        foreach (var text in AllTexts)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("safe", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("D'", text);
            Assert.DoesNotContain("lock file", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("write access", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
            Assert.DoesNotContain("  ", text);
            Assert.DoesNotContain("offer to restore", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Each of the four messages says all three things rule 4 asks for: what happened, that the work is kept and where
    /// (on this computer), and what happens next (offered back, or saved as a separate copy).
    /// </summary>
    [Fact]
    public void EveryMessage_SaysWhatHappened_WhereTheWorkIs_AndWhatHappensNext()
    {
        foreach (var message in new[]
                 {
                     KeptChangesPrompt.UnsavedChangesKeptMessage,
                     KeptChangesPrompt.ChangesNotSavedOnSwitch("a"),
                     KeptChangesPrompt.ChangesNotSavedOnClose
                 })
        {
            Assert.Contains("kept on this computer", message);
            Assert.Contains("will offer to add them", message);
            Assert.Contains("save them as a separate copy", message);
        }
    }

    /// <summary>Every text the class holds, for the rule tests.</summary>
    private static readonly string[] AllTexts =
    {
        KeptChangesPrompt.KeptSentence, KeptChangesPrompt.ChangesSinceLastSaveKept, KeptChangesPrompt.OfferToAdd,
        KeptChangesPrompt.OfferToAddTo("BIOL-TT"),
        KeptChangesPrompt.UnsavedChangesKeptMessage,
        KeptChangesPrompt.ChangesNotSavedOnSwitch("BIOL-TT"), KeptChangesPrompt.ChangesNotSavedOnClose
    };
}
