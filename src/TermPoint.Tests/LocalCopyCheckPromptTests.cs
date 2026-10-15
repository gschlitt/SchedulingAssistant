using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="LocalCopyCheckPrompt"/>, the wording of the handler for a failed open-time integrity check
/// of the local copy (spec item 5, step 5d). The texts are the user-approved wording of the 5d plan, pinned
/// verbatim, together with the UX rule that the user is never told about a "working copy" or a "local copy".
/// </summary>
public sealed class LocalCopyCheckPromptTests
{
    /// <summary>The "in use" prompt: title, body and both buttons are the approved wording.</summary>
    [Fact]
    public void InUsePrompt_IsTheApprovedWording()
    {
        Assert.Equal("Couldn't Open the Database", LocalCopyCheckPrompt.CouldntOpenTitle);
        Assert.Equal(
            "TermPoint couldn't finish opening the database because another program on this computer is using its " +
            "files, often antivirus or backup software checking a file that was just copied. This usually clears in " +
            "a few seconds.",
            LocalCopyCheckPrompt.InUseMessage);
        Assert.Equal("Try again", LocalCopyCheckPrompt.TryAgainButton);
        Assert.Equal("Close", LocalCopyCheckPrompt.CloseButton);
    }

    /// <summary>The read-only open failure message (spec item 12) is the approved wording, verbatim.</summary>
    [Fact]
    public void CouldntMakeCopyMessage_IsTheApprovedWording()
    {
        Assert.Equal(
            "TermPoint couldn't open this database. Check the network connection, then try again.",
            LocalCopyCheckPrompt.CouldntMakeCopyMessage);
    }

    /// <summary>The "damaged" message and its title are the approved wording; the title is the front door's own damaged title.</summary>
    [Fact]
    public void DamagedMessage_IsTheApprovedWording()
    {
        Assert.Equal("The database failed its integrity check and may be damaged.", LocalCopyCheckPrompt.DamagedSharedMessage);
        Assert.Equal(DatabaseSwitchPrecheck.DamagedTitle, LocalCopyCheckPrompt.DamagedSharedTitle);
        Assert.Equal("Database Integrity Check Failed", LocalCopyCheckPrompt.DamagedSharedTitle);
    }

    /// <summary>The "keeps failing" message is the approved wording, verbatim.</summary>
    [Fact]
    public void KeepsFailingMessage_IsTheApprovedWording()
    {
        Assert.Equal(
            "The shared database is fine, but TermPoint can't open it on this computer: the files it prepares here " +
            "keep failing their safety check. Nothing has been changed, and your colleagues aren't affected. This " +
            "usually points to a problem with this computer, such as a failing disk or security software interfering " +
            "with TermPoint's files. Try restarting the computer. If it keeps happening, contact your IT support and " +
            "mention this message. Meanwhile, you can open the database from another computer.",
            LocalCopyCheckPrompt.KeepsFailingMessage);
    }

    /// <summary>The "Unsaved Changes Lost" message is the one crash recovery has always shown, word for word.</summary>
    [Fact]
    public void UnsavedChangesLost_IsTheExistingCrashRecoveryWording()
    {
        Assert.Equal("Unsaved Changes Lost", LocalCopyCheckPrompt.UnsavedChangesLostTitle);
        Assert.Equal(
            "Unsaved changes from a previous session with this database were found, but they were damaged and could not be restored.",
            LocalCopyCheckPrompt.UnsavedChangesLostMessage);
    }

    /// <summary>
    /// The UX rule: no text the user can see mentions a "working copy" or a "local copy", and none shows a path or
    /// uses the word "corrupt" (the approved word is "damaged").
    /// </summary>
    [Fact]
    public void NoText_MentionsWorkingCopies_Paths_OrCorruption()
    {
        var texts = new[]
        {
            LocalCopyCheckPrompt.CouldntOpenTitle, LocalCopyCheckPrompt.InUseMessage,
            LocalCopyCheckPrompt.TryAgainButton, LocalCopyCheckPrompt.CloseButton,
            LocalCopyCheckPrompt.UnsavedChangesLostTitle, LocalCopyCheckPrompt.UnsavedChangesLostMessage,
            LocalCopyCheckPrompt.DamagedSharedTitle, LocalCopyCheckPrompt.DamagedSharedMessage,
            LocalCopyCheckPrompt.KeepsFailingMessage, LocalCopyCheckPrompt.CouldntMakeCopyMessage
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain("working copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("local copy", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corrupt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\", text);
        }
    }

    /// <summary>
    /// The front-door messages are exactly what the front door says for the same cases (same titles, same bodies),
    /// so the user hears one story whichever door they met.
    /// </summary>
    [Fact]
    public void FrontDoorMessages_AreTheFrontDoorsOwn()
    {
        var path = Path.Combine(Path.GetTempPath(), "shared", "dept.db");

        Assert.Equal(
            (DatabaseSwitchPrecheck.UnreadableTitle, DatabaseChooserViewModel.UnreadableDatabaseMessage),
            LocalCopyCheckPrompt.FrontDoorMessage(SharedDatabaseVerdict.Unreadable, path));
        Assert.Equal(
            (DatabaseSwitchPrecheck.UnreachableTitle, NetworkFileOps.UnreachableMessage),
            LocalCopyCheckPrompt.FrontDoorMessage(SharedDatabaseVerdict.Unreachable, path));
        Assert.Equal(
            (DatabaseSwitchPrecheck.MissingTitle, DatabaseSwitchPrecheck.MissingMessage(path)),
            LocalCopyCheckPrompt.FrontDoorMessage(SharedDatabaseVerdict.Missing, path));
    }

    /// <summary>The front-door mapping is defined for exactly the three verdicts the decision table routes to it; anything else is a programming error.</summary>
    [Theory]
    [InlineData(SharedDatabaseVerdict.Ok)]
    [InlineData(SharedDatabaseVerdict.Damaged)]
    [InlineData(SharedDatabaseVerdict.NotChecked)]
    public void FrontDoorMessage_ForAnyOtherVerdict_Throws(SharedDatabaseVerdict verdict)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LocalCopyCheckPrompt.FrontDoorMessage(verdict, "x.db"));

    /// <summary>Every action the table can route to a front-door message has a mapping (the two stay in step).</summary>
    [Theory]
    [InlineData(SharedDatabaseVerdict.Unreadable,  LocalCopyAction.ShowUnreadableMessage)]
    [InlineData(SharedDatabaseVerdict.Unreachable, LocalCopyAction.ShowUnreachableMessage)]
    [InlineData(SharedDatabaseVerdict.Missing,     LocalCopyAction.ShowMissingMessage)]
    public void EveryFrontDoorAction_HasAMessage(SharedDatabaseVerdict shared, LocalCopyAction expectedAction)
    {
        var action = LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, shared, FreshCopyState.NotTried);

        Assert.Equal(expectedAction, action);
        var (title, message) = LocalCopyCheckPrompt.FrontDoorMessage(shared, "x.db");
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(string.IsNullOrWhiteSpace(message));
    }
}
