using System;
using System.Collections.Generic;

namespace TermPoint.Services;

/// <summary>
/// Test-only crash points: named step boundaries in every multi-step operation that touches the shared database,
/// the local working copy or the lock file. Production code calls <see cref="Hit"/> at each boundary, exactly
/// AFTER the named step has completed and BEFORE the next one starts. When <see cref="Hook"/> is null (always,
/// in the app) a hit costs one null check and changes nothing.
///
/// <para><b>What "a crash here" means.</b> A harness sets <see cref="Hook"/> to a function that freezes the disk
/// at that instant (copies the share folder and the computer's local folder) and then stops the operation. It
/// restores the frozen copy and starts a fresh TermPoint on it, exactly as after a kill at that point. The hook
/// itself must stop the operation (typically by throwing); <see cref="Hit"/> never catches, and no production code
/// is added around a hit to cope with the hook throwing. The existing <c>catch</c> and <c>finally</c> blocks that
/// the exception then runs through are NOT part of the simulated crash, which is why the harness freezes the disk
/// before it throws.</para>
///
/// <para><b>Naming.</b> Dotted, stable, PascalCase: <c>Group.AfterStep</c>. The names are the contract with the
/// harness: do not rename one without updating <see cref="All"/> and every test that mentions it. A name may be
/// hit from more than one method when the state at the boundary is the same (for example
/// <c>Checkout.AfterLockAcquired</c> in both the ordinary checkout and the takeover).</para>
///
/// <para>Static state: tests that set <see cref="Hook"/> must reset it to null when they finish.</para>
/// </summary>
internal static class CrashPoints
{
    /// <summary>
    /// Called with the name of the point being hit. Null (the default) means crash points are off. May be called
    /// from any thread (the heartbeat and the lock release run on thread-pool threads).
    /// </summary>
    internal static Action<string>? Hook { get; set; }

    /// <summary>
    /// Marks that execution has just passed the named step boundary. Does nothing when <see cref="Hook"/> is null;
    /// otherwise calls the hook, whose exception (if any) propagates to the caller unchanged.
    /// </summary>
    /// <param name="name">The point's name; must be one of <see cref="All"/>.</param>
    internal static void Hit(string name)
    {
        if (Hook is { } hook) hook(name);
    }

    /// <summary>
    /// Every point name used in production code, grouped by operation, in the order an operation reaches them.
    /// The harness enumerates this for its crash sweep, and a test checks it matches the <c>Hit</c> calls in the
    /// source exactly.
    /// <list type="bullet">
    /// <item><b>Save.*</b> (<c>CheckoutService.SaveAsyncCore</c>): a crash leaves the shared database D either as
    /// it was (every point before <c>Save.AfterRename</c>, with a possible D.tmp and local .savetmp left behind)
    /// or already replaced (<c>Save.AfterRename</c>, <c>Save.AfterMarkerDelete</c>) with the dirty marker still
    /// present at <c>Save.AfterRename</c>.</item>
    /// <item><b>Dirty.*</b> (<c>CheckoutService.MarkDirty</c>): the dirty marker has just been written.</item>
    /// <item><b>Checkout.*, Recovery.*</b> (<c>CheckoutAsync</c>, <c>ForceCheckoutAsync</c>,
    /// <c>FinishRecoveryCheckoutAsync</c>): the lock file is ours on the share and the process dies, leaving a
    /// lock only the stale-lock rules can clear, with D' absent, freshly copied, or adopted.</item>
    /// <item><b>Release.*, Demote.*</b>: the end of a write session; a crash leaves the lock file released or not,
    /// D' present or deleted, and the dirty marker as it was.</item>
    /// <item><b>Discard.*</b> (<c>DiscardCrash</c>): the user's discarded recovery data is partly deleted; the
    /// marker is deleted last, so a crash leaves a marker without its working copy, which the startup sweep clears.</item>
    /// <item><b>Export.*, Place.*</b> (recovered-copy export, new-file placement): a crash leaves the local
    /// snapshot, a destination .tmp, or the finished destination; D' is never touched.</item>
    /// <item><b>Restore.*</b> (<c>DatabaseRestore</c>): stages of a restore from a backup; before
    /// <c>Restore.AfterRename</c> D is unchanged (a pre-restore copy and a D.tmp may exist).</item>
    /// <item><b>Refresh.*</b> (read-only snapshot refresh): D'' is being replaced; D' and D are not involved.</item>
    /// <item><b>Close.*, Switch.*</b> (<c>SessionLifecycle</c>): the steps of leaving a database; a crash leaves
    /// the final save done or not, the lock released or not, the services closed or not, and the working copy
    /// kept or deleted.</item>
    /// <item><b>Lock.*, Heartbeat.*</b> (<c>WriteLockService</c>): the lock file has just been created, its
    /// heartbeat renewal temp file written (the rename onto the lock file not yet done), or the lock file deleted.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> All { get; } =
    [
        // CheckoutService.SaveAsyncCore
        "Save.AfterLockCheck",
        "Save.AfterSourceCheck",
        "Save.AfterLocalSnapshot",
        "Save.AfterTmpCopy",
        "Save.AfterTmpVerify",
        "Save.AfterRename",
        "Save.AfterMarkerDelete",

        // CheckoutService.MarkDirty
        "Dirty.AfterMarkerWritten",

        // CheckoutService.CheckoutAsync / ForceCheckoutAsync / FinishRecoveryCheckoutAsync
        "Checkout.AfterLockAcquired",
        "Checkout.AfterCopyVerified",
        "Checkout.AfterVersionGate",
        "Recovery.AfterSourceVerified",

        // CheckoutService.ReleaseAsync
        "Release.AfterFinalSave",
        "Release.AfterLockReleased",

        // CheckoutService.DemoteToReadOnlyAsync
        "Demote.AfterLockReleased",
        "Demote.AfterConnectionClosed",
        "Demote.AfterWorkingCopyHandled",
        "Demote.AfterSnapshotCreated",

        // CheckoutService.DiscardCrash
        "Discard.AfterWorkingFileDeleted",
        "Discard.AfterSidecarsDeleted",

        // CheckoutService.SnapshotAndPlaceRecoveredCopyAsync, NewFilePlacement.PlaceAsync
        "Export.AfterLocalSnapshot",
        "Place.AfterTmpWritten",
        "Place.AfterRename",

        // DatabaseRestore (replace and create paths)
        "Restore.AfterStaged",
        "Restore.AfterPreRestoreCopy",
        "Restore.AfterTmpWritten",
        "Restore.AfterTmpVerified",
        "Restore.AfterRename",

        // CheckoutService.RefreshReadOnlySnapshotAsync
        "Refresh.AfterConnectionClosed",
        "Refresh.AfterSwap",

        // SessionLifecycle.TeardownForCloseAsync / SwitchCoreAsync
        "Close.AfterRelease",
        "Close.AfterServicesDisposed",
        "Close.AfterWorkingCopyCleanup",
        "Switch.AfterRelease",
        "Switch.AfterServicesReleased",
        "Switch.AfterOldCopyHandled",

        // WriteLockService
        "Lock.AfterCreated",
        "Heartbeat.AfterTmpWritten",
        "Lock.AfterDeleted",
    ];
}
