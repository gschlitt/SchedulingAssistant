using TermPoint.Models;
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

    /// <summary>
    /// The set of outcomes that leave the destination alone is everything except the three that change it — the two
    /// that may have replaced D and the one that created a new database (guards the theory above).
    /// </summary>
    [Fact]
    public void TargetUnchangedOutcomes_AreAllButRestoredRenameUnconfirmedAndCreated()
    {
        var unchanged = TargetUnchangedOutcomes().Select(o => (RestoreOutcome)o[0]).ToHashSet();

        Assert.DoesNotContain(RestoreOutcome.Restored, unchanged);
        Assert.DoesNotContain(RestoreOutcome.RenameUnconfirmed, unchanged);
        Assert.DoesNotContain(RestoreOutcome.Created, unchanged);
        Assert.Equal(Enum.GetValues<RestoreOutcome>().Length - 3, unchanged.Count);
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

    // ═════════════════════════════════════════════════════════════════════════
    // The database chooser (spec item 5, step 5c)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A holder as read from a lock file.</summary>
    private static LockFileData Holder(string user = "jsmith", string machine = "LAPTOP-42")
        => new() { Username = user, Machine = machine };

    /// <summary>The chooser's fixed texts are the agreed wording.</summary>
    [Fact]
    public void ChooserConstants_AreTheAgreedWording()
    {
        Assert.Equal("Restore from Backup", RestorePrompt.ChooserConfirmTitle);
        Assert.Equal("Your backup file will not be modified.", RestorePrompt.BackupNotModifiedNote);
        Assert.Equal("Checking this location…", RestorePrompt.DestinationCheckingText);
        Assert.Equal("A new database will be created here from the backup.", RestorePrompt.DestinationMissingText);
        Assert.Equal("This location isn't responding right now.", RestorePrompt.DestinationUnreachableText);
        Assert.Equal("That's the backup file itself — choose a different folder.", RestorePrompt.DestinationIsBackupFileText);
        Assert.Equal("Restoring the backup…", RestorePrompt.RestoringText);
    }

    /// <summary>The status for an existing destination says what is replaced and what is kept, naming the kept file's start (no extension).</summary>
    [Fact]
    public void BuildDestinationExistsText_IsTheAgreedWording()
    {
        var text = RestorePrompt.BuildDestinationExistsText(Path.Combine(Path.GetTempPath(), "share", "History Dept.db"));

        Assert.Equal(
            "This replaces the database at this location for everyone who uses it. " +
            "The current file is kept beside it as \"History Dept (before restore …)\".",
            text);
    }

    /// <summary>The chooser's confirmation names the backup, the database and its folder, and what is kept; it appears only for a replacement.</summary>
    [Fact]
    public void BuildChooserConfirmMessage_IsTheAgreedWording()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fileserver", "departments");
        var text = RestorePrompt.BuildChooserConfirmMessage(
            "History Dept_2026-10-13_13-00-00.db", Path.Combine(folder, "History Dept.db"));

        Assert.Equal(
            "Restore from backup:\n\nHistory Dept_2026-10-13_13-00-00.db\n\n" +
            $"This replaces \"History Dept.db\" in {folder} for everyone who uses it. " +
            "TermPoint keeps a copy of the database as it is now beside it, " +
            "named \"History Dept (before restore …)\".\n\nContinue?",
            text);
    }

    /// <summary>A confirmed restore into an existing database says what was kept (by name only) and that the database opens now.</summary>
    [Fact]
    public void Describe_Chooser_Restored_NamesThePreRestoreFile_AndSaysItOpensNow()
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(RestoreOutcome.Restored), RestoreContext.Chooser);

        Assert.Equal("Backup Restored", title);
        Assert.Equal(
            "The backup was restored. The database as it was just before the restore is kept beside it as " +
            $"\"{PreRestoreName}\". It will open now.",
            message);
        Assert.DoesNotContain("fileserver", message);
        Assert.DoesNotContain("restart", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A created database is announced plainly (the chooser shows no dialog for it, but the text exists).</summary>
    [Fact]
    public void Describe_Chooser_Created_SaysANewDatabaseWasCreated()
    {
        var (title, message) = RestorePrompt.Describe(new RestoreResult(RestoreOutcome.Created, CreatePath: true), RestoreContext.Chooser);

        Assert.Equal("Backup Restored", title);
        Assert.Equal("A new database was created from the backup. It will open now.", message);
    }

    /// <summary>A refused restore names the person editing and tells the user what to do, in the agreed words.</summary>
    [Fact]
    public void Describe_Chooser_LockHeld_NamesTheHolder_AndExplainsWhatToDo()
    {
        var (title, message) = RestorePrompt.Describe(new RestoreResult(RestoreOutcome.LockHeld, Holder: Holder()), RestoreContext.Chooser);

        Assert.Equal(RestorePrompt.NotStartedTitle, title);
        Assert.Equal(
            "jsmith on LAPTOP-42 is editing this database, so it can't be restored right now. " +
            "Restore it after they've closed it. If they're no longer using it, open the database normally " +
            "(TermPoint will offer to take over), then restore from Configuration → Save & Backup.",
            message);
    }

    /// <summary>The holder is named gracefully when the lock file gave little: just a name, just a machine, or nothing at all.</summary>
    [Theory]
    [InlineData("jsmith", "", "jsmith is editing this database")]
    [InlineData("", "LAPTOP-42", "Someone on LAPTOP-42 is editing this database")]
    [InlineData("", "", "An unknown user is editing this database")]
    [InlineData("(corrupted)", "(corrupted)", "An unknown user is editing this database")]
    public void Describe_Chooser_LockHeld_FallsBackWhenTheHolderIsPartlyUnknown(string user, string machine, string expectedStart)
    {
        var (_, message) = RestorePrompt.Describe(
            new RestoreResult(RestoreOutcome.LockHeld, Holder: Holder(user, machine)), RestoreContext.Chooser);

        Assert.StartsWith(expectedStart, message);
    }

    /// <summary>A missing holder (the lock file could not be read) does not throw and still gives a whole sentence.</summary>
    [Fact]
    public void Describe_LockHeld_WithoutAHolder_StillReadsWell_InBothContexts()
    {
        foreach (var context in Enum.GetValues<RestoreContext>())
        {
            var (_, message) = RestorePrompt.Describe(new RestoreResult(RestoreOutcome.LockHeld), context);

            Assert.StartsWith("An unknown user is editing this database", message);
        }
    }

    /// <summary>A file that appeared at the destination is explained, in the agreed words.</summary>
    [Fact]
    public void Describe_Chooser_AlreadyExists_IsTheAgreedWording()
    {
        var (title, message) = RestorePrompt.Describe(new RestoreResult(RestoreOutcome.AlreadyExists, CreatePath: true), RestoreContext.Chooser);

        Assert.Equal("Restore Stopped", title);
        Assert.Equal(
            "A file appeared at this location while the backup was being prepared, so nothing was created. " +
            "Choose the location again.",
            message);
    }

    /// <summary>An unconfirmed rename stays in the chooser: the kept copy is named, and the user is told to open the database to see which version is in place.</summary>
    [Fact]
    public void Describe_Chooser_RenameUnconfirmed_IsTheAgreedWording()
    {
        var (title, message) = RestorePrompt.Describe(ResultFor(RestoreOutcome.RenameUnconfirmed), RestoreContext.Chooser);

        Assert.Equal("Restore Not Confirmed", title);
        Assert.Equal(
            "The connection was lost at the last step, so TermPoint can't tell whether the restore finished. " +
            $"Nothing is lost: a copy of the database as it was is kept as \"{PreRestoreName}\". " +
            "Open the database once the connection is back to see which version is in place.",
            message);
        Assert.DoesNotContain("fileserver", message);
        Assert.DoesNotContain("TermPoint will now close", message);
    }

    /// <summary>On the create path a refusal says "Nothing was created." instead of "Your database is unchanged."; on the replace path it is unchanged.</summary>
    [Theory]
    [MemberData(nameof(TargetUnchangedOutcomes))]
    public void Describe_Chooser_UnchangedOutcomes_SayNothingWasCreated_OnTheCreatePath_AndUnchangedOtherwise(RestoreOutcome outcome)
    {
        // These have their own chooser wording, covered by the pinned tests above.
        if (outcome is RestoreOutcome.AlreadyExists or RestoreOutcome.LockHeld)
            return;

        var (_, onCreate) = RestorePrompt.Describe(new RestoreResult(outcome, CreatePath: true), RestoreContext.Chooser);
        var (_, onReplace) = RestorePrompt.Describe(new RestoreResult(outcome, CreatePath: false), RestoreContext.Chooser);

        Assert.Contains("Nothing was created.", onCreate);
        Assert.DoesNotContain("Your database is unchanged.", onCreate);
        Assert.Contains("Your database is unchanged.", onReplace);
        Assert.DoesNotContain("Nothing was created.", onReplace);
    }

    /// <summary>The create-path wording is only used in the chooser: Save &amp; Backup never says "Nothing was created.".</summary>
    [Theory]
    [MemberData(nameof(TargetUnchangedOutcomes))]
    public void Describe_SaveAndBackup_NeverSaysNothingWasCreated(RestoreOutcome outcome)
    {
        var (_, message) = RestorePrompt.Describe(new RestoreResult(outcome, CreatePath: true));

        Assert.DoesNotContain("Nothing was created.", message);
    }

    /// <summary>
    /// A restore that failed for a reason TermPoint can word precisely (the lock file could not be created) shows that
    /// reason, then that nothing changed. The lock-write texts carry the read-only banner's "Read-only — " prefix,
    /// which is dropped inside a restore message.
    /// </summary>
    [Fact]
    public void Describe_FailedWithADetail_ShowsTheDetailWithoutTheBannerPrefix()
    {
        var result = new RestoreResult(RestoreOutcome.Failed, Detail: "Read-only — this folder's security settings prevented TermPoint from opening the database for editing.");

        var (title, message) = RestorePrompt.Describe(result, RestoreContext.Chooser);

        Assert.Equal(RestorePrompt.NotStartedTitle, title);
        Assert.Equal(
            "The restore wasn't started. This folder's security settings prevented TermPoint from opening the database for editing. " +
            "Your database is unchanged.",
            message);
    }

    /// <summary><see cref="RestorePrompt.WithoutBannerPrefix"/>: strips and capitalises only a leading "Read-only — ".</summary>
    [Theory]
    [InlineData("Read-only — this folder is blocked.", "This folder is blocked.")]
    [InlineData("  Read-only — disk is full.  ", "Disk is full.")]
    [InlineData("The network location is not responding.", "The network location is not responding.")]
    [InlineData("Not Read-only — here.", "Not Read-only — here.")]
    [InlineData("Read-only — ", "")]
    public void WithoutBannerPrefix_RemovesOnlyALeadingPrefix(string detail, string expected)
        => Assert.Equal(expected, RestorePrompt.WithoutBannerPrefix(detail));

    /// <summary>A plain Failed (no detail) is still the generic sentence.</summary>
    [Fact]
    public void Describe_FailedWithoutADetail_IsTheGenericSentence()
    {
        Assert.Equal(
            ("Restore Stopped", "Something went wrong, so the restore was stopped. Your database is unchanged."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.Failed), RestoreContext.Chooser));
        Assert.Equal(
            ("Restore Stopped", "Something went wrong, so the restore was stopped. Nothing was created."),
            RestorePrompt.Describe(new RestoreResult(RestoreOutcome.Failed, CreatePath: true), RestoreContext.Chooser));
    }

    /// <summary>Every outcome has a non-empty title and message in BOTH contexts — the guard for an outcome added later.</summary>
    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Describe_EveryOutcome_HasATitleAndAMessage_InEveryContext(RestoreOutcome outcome)
    {
        foreach (var context in Enum.GetValues<RestoreContext>())
        {
            foreach (var createPath in new[] { false, true })
            {
                var (title, message) = RestorePrompt.Describe(
                    new RestoreResult(outcome, PreRestorePath, Holder: Holder(), CreatePath: createPath), context);

                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.False(string.IsNullOrWhiteSpace(message));
            }
        }
    }

    /// <summary>The Save &amp; Backup context is the default, so every existing caller keeps its approved wording.</summary>
    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Describe_DefaultContext_IsSaveAndBackup(RestoreOutcome outcome)
    {
        var result = new RestoreResult(outcome, PreRestorePath, Holder: Holder());

        Assert.Equal(RestorePrompt.Describe(result, RestoreContext.SaveAndBackup), RestorePrompt.Describe(result));
    }

    /// <summary>Only Restored, RenameUnconfirmed and Created change the destination; Succeeded is Restored or Created.</summary>
    [Fact]
    public void Outcomes_TargetUnchangedAndSucceeded_AreAsDocumented()
    {
        foreach (var outcome in Enum.GetValues<RestoreOutcome>())
        {
            var result = new RestoreResult(outcome);
            var changes = outcome is RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed or RestoreOutcome.Created;

            Assert.Equal(!changes, result.TargetUnchanged);
            Assert.Equal(outcome is RestoreOutcome.Restored or RestoreOutcome.Created, result.Succeeded);
        }
    }
}
