using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="RestorePrompt"/>, the wording of "Restore from backup" (spec item 5, step 5b): the
/// confirmation, the fixed texts, and one message per <see cref="RestoreOutcome"/>. The recurring promises
/// proved here: every outcome has a title and a message; every outcome that leaves the database alone says
/// "Your database is unchanged."; and the two outcomes that may have replaced it name the kept pre-restore file
/// by its NAME only, never by its folder.
/// </summary>
public sealed class RestorePromptTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The file-name part of <see cref="PreRestorePath"/>.</summary>
    private const string PreRestoreName = "dept (before restore 2026-10-13 14-05).db";

    /// <summary>
    /// A pre-restore path whose folders are distinctive ("fileserver", "departments"), so a test can prove the
    /// folder is never shown. Built with <see cref="Path.Combine(string[])"/> so it is valid on any OS.
    /// </summary>
    private static readonly string PreRestorePath =
        Path.Combine(Path.GetTempPath(), "fileserver", "departments", "history", PreRestoreName);

    /// <summary>Builds a result for <paramref name="outcome"/> carrying <see cref="PreRestorePath"/>.</summary>
    /// <param name="outcome">The outcome.</param>
    private static RestoreResult ResultFor(RestoreOutcome outcome) => new(outcome, PreRestorePath);

    /// <summary>Builds a result for <paramref name="outcome"/> carrying an explicit pre-restore path (which may be null).</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="preRestorePath">The pre-restore path to carry.</param>
    private static RestoreResult ResultFor(RestoreOutcome outcome, string? preRestorePath) => new(outcome, preRestorePath);

    /// <summary>Every defined outcome, for the "for EVERY outcome" tests.</summary>
    public static IEnumerable<object[]> AllOutcomes()
        => Enum.GetValues<RestoreOutcome>().Select(o => new object[] { o });

    /// <summary>Every outcome that leaves D exactly as it was.</summary>
    public static IEnumerable<object[]> TargetUnchangedOutcomes()
        => Enum.GetValues<RestoreOutcome>()
            .Where(o => new RestoreResult(o).TargetUnchanged)
            .Select(o => new object[] { o });

    // ── Fixed texts ───────────────────────────────────────────────────────────

    /// <summary>The fixed texts are the agreed wording.</summary>
    [Fact]
    public void Constants_AreTheAgreedWording()
    {
        Assert.Equal("Restore", RestorePrompt.ConfirmLabel);
        Assert.Equal("Restoring the Backup", RestorePrompt.WaitTitle);
        Assert.Equal(
            "Saving your changes and restoring the backup. This can take a minute on a slow network.",
            RestorePrompt.WaitMessage);
        Assert.Equal("Restore Not Started", RestorePrompt.NotStartedTitle);
        Assert.Equal(
            "Your latest changes couldn't be saved, so the restore wasn't started. Your database is unchanged.",
            RestorePrompt.SaveFailedMessage);
        Assert.Equal(
            "Restoring replaces the database for everyone with the selected backup, then restarts TermPoint. " +
            "The database as it was before the restore is kept beside it.",
            RestorePrompt.PanelDescription);
        Assert.Equal("Only the person editing this database can restore it.", RestorePrompt.ReadOnlyNote);
    }

    // ── Confirmation ──────────────────────────────────────────────────────────

    /// <summary>The confirmation names the backup, the database, and what happens, exactly as agreed.</summary>
    [Fact]
    public void BuildConfirmMessage_IsTheAgreedWording()
    {
        var text = RestorePrompt.BuildConfirmMessage("Oct 13, 2026 1:00 PM", "History Dept");

        Assert.Equal(
            "Restore from backup:\n\nOct 13, 2026 1:00 PM\n\n" +
            "This replaces the database for everyone who uses it. " +
            "TermPoint first saves your current changes, then keeps a copy of the database as it is now beside it, " +
            "named \"History Dept (before restore …)\". TermPoint then restarts.\n\nContinue?",
            text);
    }

    /// <summary>The backup's timestamp and the database's name are in the confirmation.</summary>
    [Fact]
    public void BuildConfirmMessage_ContainsTheTimestampAndTheDatabaseName()
    {
        var text = RestorePrompt.BuildConfirmMessage("Oct 13, 2026 1:00 PM", "BIOL-TT");

        Assert.Contains("Oct 13, 2026 1:00 PM", text);
        Assert.Contains("\"BIOL-TT (before restore …)\"", text);
    }

    // ── Describe: every outcome ───────────────────────────────────────────────

    /// <summary>Every outcome, even one added later, produces a non-empty title and message.</summary>
    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Describe_EveryOutcome_HasATitleAndAMessage(RestoreOutcome outcome)
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(outcome));

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    /// <summary>Every outcome that leaves the database alone says so, in the same words.</summary>
    [Theory]
    [MemberData(nameof(TargetUnchangedOutcomes))]
    public void Describe_EveryTargetUnchangedOutcome_SaysTheDatabaseIsUnchanged(RestoreOutcome outcome)
    {
        var (_, message) = RestorePrompt.Describe(new RestoreResult(outcome));

        Assert.Contains("Your database is unchanged.", message);
    }

    /// <summary>The set of outcomes that leave D alone is everything except the two that may have replaced it (guards the theory above).</summary>
    [Fact]
    public void TargetUnchangedOutcomes_AreAllButRestoredAndRenameUnconfirmed()
    {
        var unchanged = TargetUnchangedOutcomes().Select(o => (RestoreOutcome)o[0]).ToHashSet();

        Assert.DoesNotContain(RestoreOutcome.Restored, unchanged);
        Assert.DoesNotContain(RestoreOutcome.RenameUnconfirmed, unchanged);
        Assert.Equal(Enum.GetValues<RestoreOutcome>().Length - 2, unchanged.Count);
    }

    /// <summary>An outcome value that does not exist still yields a true, safe sentence (nothing changed).</summary>
    [Fact]
    public void Describe_UndefinedOutcome_FallsBackToTheGenericFailure()
    {
        var (title, message) = RestorePrompt.Describe(new RestoreResult((RestoreOutcome)9999));

        Assert.Equal("Restore Stopped", title);
        Assert.Equal("Something went wrong, so the restore was stopped. Your database is unchanged.", message);
    }

    /// <summary>The two refusals that happen before anything starts share the "not started" title.</summary>
    [Theory]
    [InlineData(RestoreOutcome.NotWriter)]
    [InlineData(RestoreOutcome.Busy)]
    public void Describe_NotStartedOutcomes_UseTheNotStartedTitle(RestoreOutcome outcome)
    {
        Assert.Equal(RestorePrompt.NotStartedTitle, RestorePrompt.Describe(new RestoreResult(outcome)).Title);
    }

    /// <summary>Pins the exact wording of the agreed refusals.</summary>
    [Fact]
    public void Describe_PinsTheAgreedWordingOfTheRefusals()
    {
        Assert.Equal(
            ("Restore Not Started", "Only the person editing this database can restore it. Your database is unchanged."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.NotWriter)));

        Assert.Equal(
            ("Restore Not Started",
             "A save is in progress, so the restore wasn't started. Your database is unchanged. Try again in a moment."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.Busy)));

        Assert.Equal(
            ("Restore Stopped", "That backup is damaged, so it can't be restored. Your database is unchanged."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.BackupDamaged)));

        Assert.Equal(
            ("Restore Stopped", "That file isn't a TermPoint database, so it can't be restored. Your database is unchanged."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.NotTermPointDatabase)));

        Assert.Equal(
            ("Restore Stopped",
             "The database's location stopped responding, so the restore was stopped. Your database is unchanged. " +
             "Check the network connection and try again."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.Unreachable)));
    }

    // ── Describe: the two outcomes that may have replaced D ───────────────────

    /// <summary>A confirmed restore says what was kept (by file name), that TermPoint restarts, and nothing about a folder.</summary>
    [Fact]
    public void Describe_Restored_NamesThePreRestoreFileButNotItsFolder()
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(RestoreOutcome.Restored));

        Assert.Equal("Backup Restored", title);
        Assert.Equal(
            "The backup was restored. The database as it was just before the restore is kept beside it as " +
            $"\"{PreRestoreName}\". TermPoint will now restart.",
            message);
        Assert.DoesNotContain("fileserver", message);
        Assert.DoesNotContain("departments", message);
    }

    /// <summary>An unconfirmed restore says nothing is lost, names the kept file by name only, and that TermPoint closes.</summary>
    [Fact]
    public void Describe_RenameUnconfirmed_NamesThePreRestoreFileButNotItsFolder()
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(RestoreOutcome.RenameUnconfirmed));

        Assert.Equal("Restore Not Confirmed", title);
        Assert.Equal(
            "The connection was lost at the last step, so TermPoint can't tell whether the restore finished. " +
            "Nothing is lost: your changes were saved first, and a copy of the database as it was then is kept as " +
            $"\"{PreRestoreName}\". TermPoint will now close. " +
            "Reopen it once the connection is back to see which version is in place.",
            message);
        Assert.DoesNotContain("fileserver", message);
        Assert.DoesNotContain("departments", message);
    }

    /// <summary>The file name is taken correctly from a local Windows-style path too.</summary>
    [Fact]
    public void Describe_Restored_WithALocalPath_ShowsOnlyTheFileName()
    {
        var path = Path.Combine(Path.GetTempPath(), "private-folder", "dept (before restore 2026-10-13 14-05).db");

        var (_, message) = RestorePrompt.Describe(ResultFor(RestoreOutcome.Restored, path));

        Assert.Contains("\"dept (before restore 2026-10-13 14-05).db\"", message);
        Assert.DoesNotContain("private-folder", message);
    }

    /// <summary>A missing pre-restore path (which the routine never produces here) does not throw and keeps the sentences whole.</summary>
    [Theory]
    [InlineData(RestoreOutcome.Restored)]
    [InlineData(RestoreOutcome.RenameUnconfirmed)]
    public void Describe_NullPreRestorePath_DoesNotThrow_AndUsesTheFallbackPhrase(RestoreOutcome outcome)
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(outcome, preRestorePath: null));

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.Contains("the \"(before restore …)\" file", message);
        Assert.DoesNotContain("\"\"", message);
    }
}
