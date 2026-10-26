using System.Runtime.CompilerServices;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the yellow save-error row of <see cref="MainWindowViewModel"/> (spec item 28): the sticky "can't reach
/// the database location" notice is cleared by a successful save (<c>ClearSaveError</c>, called by the window on
/// <c>SaveCompleted</c>) or by the heartbeat's "location reachable again" signal (<c>ClearOutageNotice</c>, called on
/// <c>LocationReachableAgain</c>), and that signal never clears any other banner; a refresh-failure line is cleared
/// only by a refresh that works, and only while it is still the line on screen.
///
/// <para>The view model's constructor needs the whole window's services, so these tests build it WITHOUT running the
/// constructor (<see cref="RuntimeHelpers.GetUninitializedObject"/>). That is safe for what is exercised here: the
/// save-error row is one observable property and two methods that use no constructor-assigned field, and the sticky
/// path (<c>autoDismiss: false</c>) never creates the dispatcher timer the auto-dismissing path needs.</para>
/// </summary>
public sealed class SaveErrorBannerTests
{
    /// <summary>Creates a view model without running its constructor (see the class remarks).</summary>
    private static MainWindowViewModel NewViewModel()
        => (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));

    /// <summary>The outage notice is shown as given, and is not auto-dismissing (the row is set sticky).</summary>
    [Fact]
    public void TheOutageNotice_IsShownInTheRow()
    {
        var vm = NewViewModel();
        var notice = SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: true);

        vm.SetSaveError(notice, autoDismiss: false);

        Assert.Equal(notice, vm.SaveErrorMessage);
        Assert.True(vm.ShowSaveError);
    }

    /// <summary>"The location responds again" clears the outage notice, whichever of the three variants is up.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ClearOutageNotice_ClearsTheOutageNotice(bool hasUnsavedChanges, bool autoSaveEnabled)
    {
        var vm = NewViewModel();
        vm.SetSaveError(SavePrompt.OutageNotice(hasUnsavedChanges, autoSaveEnabled), autoDismiss: false);

        vm.ClearOutageNotice();

        Assert.Null(vm.SaveErrorMessage);
        Assert.False(vm.ShowSaveError);
    }

    /// <summary>A later save that succeeds clears the outage notice (the window calls <c>ClearSaveError</c> on <c>SaveCompleted</c>).</summary>
    [Fact]
    public void ASuccessfulSave_ClearsTheOutageNotice()
    {
        var vm = NewViewModel();
        vm.SetSaveError(SavePrompt.OutageNotice(true, false), autoDismiss: false);

        vm.ClearSaveError();

        Assert.Null(vm.SaveErrorMessage);
    }

    /// <summary>
    /// The heartbeat's signal clears ONLY the outage notice: a lost-lock banner, a failed save's text, the
    /// session-file-missing text, the crash-protection warning and a refresh line all stay exactly as they are.
    /// </summary>
    [Fact]
    public void ClearOutageNotice_NeverClearsAnotherBanner()
    {
        var others = new[]
        {
            LockLossPrompt.Banner(takenOver: true, "alice", "ALICE-PC", hadUnsavedChanges: true),
            LockLossPrompt.Banner(takenOver: false, null, null, hadUnsavedChanges: false),
            SavePrompt.ForTransientFailure(SavePrompt.WriteFailedMessage, isAutoSave: false),
            SavePrompt.SessionFileMissingMessage,
            SavePrompt.CrashProtectionMessage,
            RefreshPrompt.NotResponding(null),
            RefreshPrompt.ConnectionLost,
        };

        foreach (var other in others)
        {
            var vm = NewViewModel();
            vm.SetSaveError(other, autoDismiss: false);

            vm.ClearOutageNotice();

            Assert.Equal(other, vm.SaveErrorMessage);
        }
    }

    /// <summary>
    /// An outage notice replaced by another banner (a different failure raised after it) is gone from the row, so the
    /// signal that arrives afterwards finds nothing of its own to clear and leaves the newer banner alone.
    /// </summary>
    [Fact]
    public void ClearOutageNotice_AfterAnotherBannerReplacedTheNotice_LeavesTheNewerBanner()
    {
        var vm = NewViewModel();
        vm.SetSaveError(SavePrompt.OutageNotice(true, true), autoDismiss: false);
        var newer = SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false);
        vm.SetSaveError(newer, autoDismiss: false);

        vm.ClearOutageNotice();

        Assert.Equal(newer, vm.SaveErrorMessage);
    }

    /// <summary>With nothing in the row, the signal does nothing (it fires on every successful lock check).</summary>
    [Fact]
    public void ClearOutageNotice_WithNothingShown_DoesNothing()
    {
        var vm = NewViewModel();

        vm.ClearOutageNotice();

        Assert.Null(vm.SaveErrorMessage);
    }

    /// <summary>A refresh failure is shown in the row; the next refresh that works clears it.</summary>
    [Fact]
    public void ARefreshFailureLine_IsClearedByTheNextRefreshThatWorks()
    {
        var vm = NewViewModel();
        var line = RefreshPrompt.NotResponding(null);

        vm.ShowRefreshFailure(line);
        Assert.Equal(line, vm.SaveErrorMessage);

        vm.ClearRefreshFailure();
        Assert.Null(vm.SaveErrorMessage);
    }

    /// <summary>
    /// A refresh that works clears only its OWN line: when another banner (the outage notice, a lost-lock banner) has
    /// replaced the refresh line, a later refresh leaves that banner alone.
    /// </summary>
    [Fact]
    public void ARefreshThatWorks_NeverClearsAnotherBanner()
    {
        var vm = NewViewModel();
        vm.ShowRefreshFailure(RefreshPrompt.OtherFailure(null));
        var other = SavePrompt.OutageNotice(false, false);
        vm.SetSaveError(other, autoDismiss: false);

        vm.ClearRefreshFailure();

        Assert.Equal(other, vm.SaveErrorMessage);
    }

    /// <summary>With no refresh line ever shown, a refresh that works leaves any banner alone (it would be another kind).</summary>
    [Fact]
    public void ARefreshThatWorks_WithNoRefreshLineShown_LeavesTheRowAlone()
    {
        var vm = NewViewModel();
        vm.SetSaveError(SavePrompt.CrashProtectionMessage, autoDismiss: false);

        vm.ClearRefreshFailure();

        Assert.Equal(SavePrompt.CrashProtectionMessage, vm.SaveErrorMessage);
    }
}
