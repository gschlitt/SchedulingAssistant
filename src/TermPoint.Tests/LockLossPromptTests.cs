using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="LockLossPrompt"/>, the wording of the lock-lost banner (spec item 28; findings F6 and F13) and
/// of the informational notice shown when TermPoint put a removed lock file back. Every text is pinned VERBATIM (the
/// wording was approved by Greg on 2026-10-26), with the rules of rule 4 of the four rules for the shared database.
/// </summary>
public sealed class LockLossPromptTests
{
    /// <summary>The approved kept-changes tail (changes kept, then what happens next), shared by the cases below.</summary>
    private const string KeptTail =
        " Your changes since your last save are kept on this computer. " +
        "TermPoint will offer to add them to the database the next time you open it for editing. " +
        "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.";

    /// <summary>Taken over, holder known, unsaved changes: the full approved banner.</summary>
    [Fact]
    public void Banner_TakenOver_HolderKnown_WithUnsavedChanges()
        => Assert.Equal(
            "alice on ALICE-PC has taken over editing this database, so it's read-only for you now." + KeptTail +
            " To edit again once they've finished, close TermPoint and open it again.",
            LockLossPrompt.Banner(takenOver: true, "alice", "ALICE-PC", hadUnsavedChanges: true));

    /// <summary>Taken over, holder unknown (no name), no unsaved changes.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Banner_TakenOver_HolderUnknown_WithoutUnsavedChanges(string? username)
        => Assert.Equal(
            "Someone else has taken over editing this database, so it's read-only for you now. " +
            "There were no unsaved changes at the time. " +
            "To edit again once they've finished, close TermPoint and open it again.",
            LockLossPrompt.Banner(takenOver: true, username, "ALICE-PC", hadUnsavedChanges: false));

    /// <summary>Taken over, holder unknown, WITH unsaved changes: the unknown-holder sentence, the kept tail, the takeover ending.</summary>
    [Fact]
    public void Banner_TakenOver_HolderUnknown_WithUnsavedChanges()
        => Assert.Equal(
            "Someone else has taken over editing this database, so it's read-only for you now." + KeptTail +
            " To edit again once they've finished, close TermPoint and open it again.",
            LockLossPrompt.Banner(takenOver: true, null, null, hadUnsavedChanges: true));

    /// <summary>A holder with a name but no machine name is named without " on …" (no "alice on  has…").</summary>
    [Fact]
    public void Banner_TakenOver_HolderWithoutMachine_NamesJustTheUser()
        => Assert.StartsWith("alice has taken over editing this database, so it's read-only for you now.",
            LockLossPrompt.Banner(takenOver: true, "alice", "  ", hadUnsavedChanges: false));

    /// <summary>The lock file was deleted or is unreadable (F13): "removed or changed", with unsaved changes.</summary>
    [Fact]
    public void Banner_FileRemovedOrChanged_WithUnsavedChanges()
        => Assert.Equal(
            "Another program removed or changed the file TermPoint uses to mark this database as in use " +
            "(antivirus or cloud-sync software can do this), so it's read-only for you now." + KeptTail +
            " To edit again, close TermPoint and open it again.",
            LockLossPrompt.Banner(takenOver: false, null, null, hadUnsavedChanges: true));

    /// <summary>The lock file was deleted or is unreadable, no unsaved changes; a holder passed anyway is ignored.</summary>
    [Fact]
    public void Banner_FileRemovedOrChanged_WithoutUnsavedChanges_IgnoresAnyHolder()
        => Assert.Equal(
            "Another program removed or changed the file TermPoint uses to mark this database as in use " +
            "(antivirus or cloud-sync software can do this), so it's read-only for you now. " +
            "There were no unsaved changes at the time. " +
            "To edit again, close TermPoint and open it again.",
            LockLossPrompt.Banner(takenOver: false, "alice", "ALICE-PC", hadUnsavedChanges: false));

    /// <summary>The "lock file put back" notice is exactly the approved wording.</summary>
    [Fact]
    public void LockFilePutBack_IsTheApprovedWording()
        => Assert.Equal(
            "Another program removed the file TermPoint uses to mark this database as in use " +
            "(antivirus or cloud-sync software can do this). TermPoint has put it back, and you can carry on as normal.",
            LockLossPrompt.LockFilePutBack);

    /// <summary>
    /// The rules for every text: no "lock file", "write access", "safe", "working copy", "local copy", "D'", no path, no
    /// doubled space, no leftover of the old promise; the banner always ends by saying how to edit again.
    /// </summary>
    [Fact]
    public void NoText_BreaksTheRulesOfRule4()
    {
        var texts = new List<string> { LockLossPrompt.LockFilePutBack };
        foreach (var takenOver in new[] { true, false })
        foreach (var changes in new[] { true, false })
        foreach (var user in new[] { "alice", null })
            texts.Add(LockLossPrompt.Banner(takenOver, user, "ALICE-PC", changes));

        foreach (var text in texts)
        {
            Assert.DoesNotContain("lock file", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("write access", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("safe", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("D'", text);
            Assert.DoesNotContain(":\\", text);
            Assert.DoesNotContain("  ", text);
            Assert.DoesNotContain("offer to restore", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exiting and restarting", text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var banner in texts.Skip(1))
            Assert.EndsWith("close TermPoint and open it again.", banner);
    }
}
