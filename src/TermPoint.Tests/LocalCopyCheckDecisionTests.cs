using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="LocalCopyCheckDecision"/>, the pure decision table of the shared handler for a failed
/// open-time integrity check of the local copy (spec item 5, step 5d): the copy's verdict × whether it holds
/// recovered unsaved changes × the shared database's verdict × the state of the one fresh copy → the next action.
/// Every row of the plan is pinned, and the invariants the plan relies on are checked over the WHOLE input space.
/// </summary>
public sealed class LocalCopyCheckDecisionTests
{
    // ── The rows of the plan ──────────────────────────────────────────────────

    /// <summary>A copy that passes opens normally, whatever else is known.</summary>
    [Theory]
    [InlineData(false, SharedDatabaseVerdict.NotChecked, FreshCopyState.NotTried)]
    [InlineData(true,  SharedDatabaseVerdict.NotChecked, FreshCopyState.NotTried)]
    [InlineData(false, SharedDatabaseVerdict.Ok,         FreshCopyState.Made)]
    [InlineData(false, SharedDatabaseVerdict.Damaged,    FreshCopyState.Made)]
    public void CopyOk_OpensNormally(bool recovered, SharedDatabaseVerdict shared, FreshCopyState fresh)
        => Assert.Equal(LocalCopyAction.OpenNormally,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Ok, recovered, shared, fresh));

    /// <summary>
    /// A locked copy always gets the "in use" prompt: nothing else about it matters, because a locked copy is never
    /// touched — not archived, not replaced — and the shared database is not even asked about. This includes the
    /// open that follows a fresh copy.
    /// </summary>
    [Theory]
    [InlineData(false, SharedDatabaseVerdict.NotChecked, FreshCopyState.NotTried)]
    [InlineData(true,  SharedDatabaseVerdict.NotChecked, FreshCopyState.NotTried)]
    [InlineData(false, SharedDatabaseVerdict.Ok,         FreshCopyState.Made)]
    [InlineData(true,  SharedDatabaseVerdict.Damaged,    FreshCopyState.NotTried)]
    public void CopyLocked_ShowsTheInUsePrompt(bool recovered, SharedDatabaseVerdict shared, FreshCopyState fresh)
        => Assert.Equal(LocalCopyAction.ShowInUseDialog,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Locked, recovered, shared, fresh));

    /// <summary>
    /// A damaged copy that holds recovered unsaved changes is set aside FIRST — before the shared database is even
    /// asked about and before any fresh copy could replace it — whatever the shared database's verdict is.
    /// </summary>
    [Theory]
    [InlineData(SharedDatabaseVerdict.NotChecked)]
    [InlineData(SharedDatabaseVerdict.Ok)]
    [InlineData(SharedDatabaseVerdict.Damaged)]
    [InlineData(SharedDatabaseVerdict.Unreadable)]
    [InlineData(SharedDatabaseVerdict.Unreachable)]
    [InlineData(SharedDatabaseVerdict.Missing)]
    public void CopyDamaged_WithRecoveredChanges_ArchivesThemBeforeAnythingElse(SharedDatabaseVerdict shared)
        => Assert.Equal(LocalCopyAction.ArchiveRecoveredChangesThenContinue,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, holdsRecoveredChanges: true, shared, FreshCopyState.NotTried));

    /// <summary>A damaged copy with nothing to protect: the shared database has not been looked at, so look at it.</summary>
    [Fact]
    public void CopyDamaged_SharedNotChecked_ChecksTheSharedDatabase()
        => Assert.Equal(LocalCopyAction.CheckSharedDatabase,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.NotChecked, FreshCopyState.NotTried));

    /// <summary>The shared database is damaged too: no fresh copy can help; the user is told and sent to the recovery chooser.</summary>
    [Fact]
    public void CopyDamaged_SharedDamaged_ShowsDamagedThenTheRecoveryChooser()
        => Assert.Equal(LocalCopyAction.ShowDamagedThenRecoveryChooser,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.Damaged, FreshCopyState.NotTried));

    /// <summary>The shared database cannot be read right now: the front-door "can't read it" message, not a fresh copy.</summary>
    [Fact]
    public void CopyDamaged_SharedUnreadable_ShowsTheUnreadableMessage()
        => Assert.Equal(LocalCopyAction.ShowUnreadableMessage,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.Unreadable, FreshCopyState.NotTried));

    /// <summary>The shared database cannot be reached: the network message, not a fresh copy.</summary>
    [Fact]
    public void CopyDamaged_SharedUnreachable_ShowsTheUnreachableMessage()
        => Assert.Equal(LocalCopyAction.ShowUnreachableMessage,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.Unreachable, FreshCopyState.NotTried));

    /// <summary>The shared database has gone: the "not found" message.</summary>
    [Fact]
    public void CopyDamaged_SharedMissing_ShowsTheMissingMessage()
        => Assert.Equal(LocalCopyAction.ShowMissingMessage,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.Missing, FreshCopyState.NotTried));

    /// <summary>THE case this step exists for: the shared database is fine and only this computer's copy is damaged — make one fresh copy.</summary>
    [Fact]
    public void CopyDamaged_SharedOk_MakesAFreshCopy()
        => Assert.Equal(LocalCopyAction.MakeFreshCopy,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, false, SharedDatabaseVerdict.Ok, FreshCopyState.NotTried));

    /// <summary>The fresh copy was made and the open failed its check AGAIN: the copy keeps failing; there is only ever one fresh copy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyDamagedAgain_AfterTheFreshCopy_ShowsKeepsFailing(bool recovered)
        => Assert.Equal(LocalCopyAction.ShowKeepsFailing,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, recovered, SharedDatabaseVerdict.Ok, FreshCopyState.Made));

    /// <summary>The fresh copy could not even be made: the same "keeps failing" end, whatever else is known.</summary>
    [Theory]
    [InlineData(LocalCopyVerdict.Damaged)]
    [InlineData(LocalCopyVerdict.Locked)]
    public void FreshCopyFailed_ShowsKeepsFailing(LocalCopyVerdict copy)
        => Assert.Equal(LocalCopyAction.ShowKeepsFailing,
            LocalCopyCheckDecision.Decide(copy, false, SharedDatabaseVerdict.Ok, FreshCopyState.Failed));

    // ── The whole input space ─────────────────────────────────────────────────

    /// <summary>Every combination of the four inputs.</summary>
    public static IEnumerable<object[]> AllInputs()
    {
        foreach (var copy in Enum.GetValues<LocalCopyVerdict>())
        foreach (var recovered in new[] { false, true })
        foreach (var shared in Enum.GetValues<SharedDatabaseVerdict>())
        foreach (var fresh in Enum.GetValues<FreshCopyState>())
            yield return new object[] { copy, recovered, shared, fresh };
    }

    /// <summary>
    /// For EVERY input: the table never loses unsaved work, never makes a second fresh copy, never replaces or
    /// archives a copy it has not been told is damaged, never treats a locked copy as damaged, and never opens a
    /// copy that failed its check.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllInputs))]
    public void Invariants_HoldForEveryInput(
        LocalCopyVerdict copy, bool recovered, SharedDatabaseVerdict shared, FreshCopyState fresh)
    {
        var action = LocalCopyCheckDecision.Decide(copy, recovered, shared, fresh);

        // A fresh copy is made at most once, and never over a copy that holds unsaved work.
        if (action == LocalCopyAction.MakeFreshCopy)
        {
            Assert.Equal(FreshCopyState.NotTried, fresh);
            Assert.Equal(LocalCopyVerdict.Damaged, copy);
            Assert.False(recovered);
            Assert.Equal(SharedDatabaseVerdict.Ok, shared);
        }

        // Only a damaged copy with recovered changes is archived.
        if (action == LocalCopyAction.ArchiveRecoveredChangesThenContinue)
        {
            Assert.Equal(LocalCopyVerdict.Damaged, copy);
            Assert.True(recovered);
        }

        // The shared database is consulted only for a damaged copy, and only once.
        if (action == LocalCopyAction.CheckSharedDatabase)
        {
            Assert.Equal(LocalCopyVerdict.Damaged, copy);
            Assert.Equal(SharedDatabaseVerdict.NotChecked, shared);
        }

        // A locked copy is never called damaged: the only answers are the in-use prompt (or the end of a failed fresh copy).
        if (copy == LocalCopyVerdict.Locked)
            Assert.True(action is LocalCopyAction.ShowInUseDialog or LocalCopyAction.ShowKeepsFailing);

        // Opening normally is only ever the answer for a copy that passed — or never when the fresh copy failed.
        if (action == LocalCopyAction.OpenNormally)
        {
            Assert.Equal(LocalCopyVerdict.Ok, copy);
            Assert.NotEqual(FreshCopyState.Failed, fresh);
        }

        // Unsaved work is never discarded by a give-up that skipped the archive step: whenever a damaged copy
        // holds recovered changes and no fresh copy has been tried, the archive step comes first.
        if (copy == LocalCopyVerdict.Damaged && recovered && fresh == FreshCopyState.NotTried)
            Assert.Equal(LocalCopyAction.ArchiveRecoveredChangesThenContinue, action);
    }

    /// <summary>
    /// Walking the table the way the handler does (asking again after each step) always terminates, and for a
    /// damaged copy with recovered changes and a healthy shared database it visits exactly: archive, check the
    /// shared database, make a fresh copy.
    /// </summary>
    [Fact]
    public void WalkingTheTable_ForADamagedRecoveredCopyWithAHealthySharedDatabase_ArchivesThenChecksThenRefreshes()
    {
        var steps = new List<LocalCopyAction>();
        var recovered = true;
        var shared = SharedDatabaseVerdict.NotChecked;
        var fresh = FreshCopyState.NotTried;

        for (var guard = 0; guard < 10; guard++)
        {
            var action = LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, recovered, shared, fresh);
            steps.Add(action);

            if (action == LocalCopyAction.ArchiveRecoveredChangesThenContinue) recovered = false;
            else if (action == LocalCopyAction.CheckSharedDatabase) shared = SharedDatabaseVerdict.Ok;
            else if (action == LocalCopyAction.MakeFreshCopy) break;   // the handler now opens again
            else Assert.Fail($"Unexpected step {action}.");
        }

        Assert.Equal(
            new[]
            {
                LocalCopyAction.ArchiveRecoveredChangesThenContinue,
                LocalCopyAction.CheckSharedDatabase,
                LocalCopyAction.MakeFreshCopy
            },
            steps);
    }
}
