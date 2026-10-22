using TermPoint.Exceptions;
using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// The database-session lifecycle, moved out of the main window so it can be driven without any UI (spec item
/// 9.1, step 9.1b): opening a database at startup, switching to another one, and the recovery at open (crash
/// recovery and the export of kept changes). Everything that asks or tells the user goes through
/// <see cref="ILifecyclePrompts"/>; everything that touches the window or the service container goes through
/// <see cref="ISessionHost"/>; both are implemented by <see cref="MainWindow"/>.
///
/// <para><b>This is a pure move.</b> Every method here is the body it had in <see cref="MainWindow"/>, with only
/// its call targets changed (<c>App.Checkout</c> became <see cref="_checkout"/>, <c>Prompts.</c> became
/// <see cref="_prompts"/>, and so on). Behaviour, wording, ordering, error handling and threading are unchanged:
/// it runs on the UI thread exactly as the code did, with no <c>ConfigureAwait</c> changes and no new background
/// work.</para>
///
/// <para>Desktop only, like <see cref="CheckoutService"/>: excluded from the browser build.</para>
/// </summary>
public sealed class SessionLifecycle
{
    /// <summary>The checkout service (was <c>App.Checkout</c>; created once and never replaced).</summary>
    private readonly CheckoutService _checkout;

    /// <summary>The single-instance guard (was <c>App.InstanceGuard</c>).</summary>
    private readonly DatabaseInstanceGuard _instanceGuard;

    /// <summary>
    /// Getter for the logger (was <c>App.Logger</c>). The logger is REPLACED at runtime by
    /// <c>App.InitializeServices</c>, so it is read through this getter at each use, never captured.
    /// </summary>
    private readonly Func<IAppLogger> _logger;

    /// <summary>
    /// Getter for the application settings (was <c>AppSettings.Current</c>). The settings are replaced by
    /// <c>AppSettings.Load()</c> after the startup wizard, so they are read through this getter at each use.
    /// </summary>
    private readonly Func<AppSettings> _settings;

    /// <summary>Evaluates the licence for the share directory of a database path (was <c>App.EvaluateLicense</c>).</summary>
    private readonly Action<string> _evaluateLicense;

    /// <summary>
    /// Getter for the integrity fault plan used by the open-time integrity check (was
    /// <c>App.IntegrityFaults</c>; null outside a debug fault-injection run).
    /// </summary>
    private readonly Func<IntegrityFaultPlan?> _integrityFaults;

    /// <summary>Everything that asks or tells the user (modal dialogs, pickers, the database chooser).</summary>
    private readonly ILifecyclePrompts _prompts;

    /// <summary>Everything that touches the window or the service container.</summary>
    private readonly ISessionHost _host;

    /// <summary>
    /// Creates the lifecycle over the application's long-lived services and the two seams.
    /// </summary>
    /// <param name="checkout">The checkout service (was <c>App.Checkout</c>).</param>
    /// <param name="instanceGuard">The single-instance guard (was <c>App.InstanceGuard</c>).</param>
    /// <param name="logger">Getter for the current logger (was <c>App.Logger</c>); called at each use because the logger is replaced at runtime.</param>
    /// <param name="settings">Getter for the current application settings (was <c>AppSettings.Current</c>); called at each use because the settings are replaced after the wizard.</param>
    /// <param name="evaluateLicense">Evaluates the licence for a database path (was <c>App.EvaluateLicense</c>).</param>
    /// <param name="integrityFaults">Getter for the debug integrity fault plan (was <c>App.IntegrityFaults</c>); may return null.</param>
    /// <param name="prompts">The dialog seam.</param>
    /// <param name="host">The window and container seam.</param>
    public SessionLifecycle(
        CheckoutService checkout,
        DatabaseInstanceGuard instanceGuard,
        Func<IAppLogger> logger,
        Func<AppSettings> settings,
        Action<string> evaluateLicense,
        Func<IntegrityFaultPlan?> integrityFaults,
        ILifecyclePrompts prompts,
        ISessionHost host)
    {
        _checkout        = checkout;
        _instanceGuard   = instanceGuard;
        _logger          = logger;
        _settings        = settings;
        _evaluateLicense = evaluateLicense;
        _integrityFaults = integrityFaults;
        _prompts         = prompts;
        _host            = host;
    }

    /// <summary>
    /// Why the database chooser is shown the next time a switch leaves NO database open (see
    /// <see cref="ShowSwitchFailedChooserAsync"/>): <see cref="RecoveryReason.SwitchFailed"/> for every
    /// failure except one refused by the single-instance guard, which sets
    /// <see cref="RecoveryReason.AlreadyOpen"/> so the banner doesn't claim a database was closed or failed.
    /// Reset at the start of every <see cref="SwitchCoreAsync"/> attempt, so a stale value from an earlier
    /// attempt (the chooser loop runs the next pick through the same method) can never be shown.
    /// </summary>
    private RecoveryReason _noDatabaseReason = RecoveryReason.SwitchFailed;

    /// <summary>
    /// Steps (1)–(5) of the returning-user startup, for one database (see <see cref="MainWindow.RunReturningUserStartupAsync"/>
    /// for their order): the single-instance guard, the recent list and the licence, crash recovery and checkout,
    /// then the services and the window. Extracted so the recovery chooser that follows a damaged shared database
    /// (spec item 5, step 5d) can open its pick through the same steps.
    /// </summary>
    /// <param name="dbPath">The shared database (D) to open.</param>
    /// <param name="settings">The application settings (the recent list is updated).</param>
    /// <returns>
    /// Null when the startup is finished: the database is open, or the app is closing (the user declined,
    /// something could not be opened and the user has been told why, or the user left the chooser). Otherwise
    /// the database the user picked in the recovery chooser, to be opened next.
    /// </returns>
    public async Task<string?> OpenStartupDatabaseAsync(string dbPath, AppSettings settings)
    {
        // ── Single-instance guard (spec item 4b) ──────────────────────────────
        // Taken BEFORE anything below touches shared or local state (the stale artifact sweep, crash
        // detection, the checkout). If another LIVE TermPoint of this user already
        // has this database open on this computer, none of that may run: its dirty marker would look
        // like a crash, "Discard" would delete the live session's marker, and a later sweep could delete
        // its working copy. The user is told, then shown the chooser to pick a different database (or
        // exit); picking the same one again is refused again. Returns the path to open.
        var guardedPath = await _instanceGuard.AcquireOrChooseAgainAsync(
            dbPath, ShowAlreadyOpenMessageAsync, ShowAlreadyOpenChooserAsync);
        if (guardedPath is null) return null; // user exited or the wizard took over; nothing was acquired
        dbPath = guardedPath;

        settings.AddRecentDatabase(dbPath);

        // ── License evaluation (must precede checkout so the license gate can
        //    block lock acquisition when access is read-only) ──────────────────
#if !BROWSER
        _evaluateLicense(dbPath);
#endif

        // ── Checkout flow ─────────────────────────────────────────────────────
        // Sweep stale local artifacts and check for an orphaned working copy (crash recovery), then
        // acquire the write lock and copy D → D' (or D → D'' for read-only mode). An orphaned D.tmp on
        // the share is deleted by the checkout itself, and only once write access is confirmed.
        // The result determines which path is passed to InitializeServices.
        var recoverUnsaved = await CleanUpAndOfferRecoveryAsync(dbPath, ignoreObserverMode: false);

        var checkoutResult = await RunCheckoutAsync(dbPath, recoverUnsaved);
        if (checkoutResult is null)
        {
            // Checkout cannot proceed (network unreachable, database missing, or no local
            // copy could be made) — dialog already shown. Close the app since this is the
            // initial startup path; there is no previous DB to fall back to.
            //
            // Forget the failed attempt's working-copy bookkeeping first: a stale-lock checkout that
            // was declined leaves WorkingPath naming this database's D', which may hold crash-recovery
            // data kept for the next launch — and the shutdown that Close() starts deletes whatever
            // WorkingPath names. (Keeps any copy that has a dirty marker; see ResetAfterFailedOpenAsync.)
            await _checkout.ResetAfterFailedOpenAsync();
            _host.CloseWindow();
            return null;
        }
        // ─────────────────────────────────────────────────────────────────────

        // Resolve the canonical source path D for the title bar and backup service.
        // RunCheckoutAsync returns D' (write mode) or D'' (read-only mode), neither of
        // which should be shown to the user. CheckoutService.SourcePath is always D.
        var canonicalPath = _checkout.SourcePath;

        // Initialize DI and DB, wire up the view model, then reveal the window. The open-time integrity check of
        // the local copy (spec item 5, step 5d) can stop it: a copy another program is using, or a damaged one.
        // OpenWithLocalCopyCheckAsync handles both — Try again, one fresh copy, or the recovery chooser — and
        // returns no view model only after it has ended the half-opened session WITHOUT saving.
        //
        // The local file to open is read from the checkout at the moment of each call, NOT captured above
        // (spec item 3, step 3c): after a fresh copy the checkout may now hold a different file — a new D', or,
        // when the version gate ran again on it and found the database needs a newer TermPoint, a read-only D''
        // (RequiredAppVersion set, so the usual "needs TermPoint X" banner appears).
        IOpenedDatabase vm;
        try
        {
            var opened = await OpenWithLocalCopyCheckAsync(() => _host.OpenServices(_checkout.WorkingPath));
            if (opened.ViewModel is null)
            {
                // Nothing is open and nothing may be saved: the session has been ended and the user told why.
                // Close() runs OnClosing, which saves nothing (Mode is no longer WriteAccess and WorkingPath
                // is forgotten — see ResetAfterFailedOpenAsync).
                if (!opened.SharedDatabaseDamaged)
                {
                    _host.CloseWindow();
                    return null;
                }

                // The shared database itself is damaged: offer the recovery chooser (Restore from a backup is
                // there). The guard taken for this database is given back first, so the pick may be this same
                // database after a restore, or another one. The caller opens the pick through the same steps.
                _instanceGuard.ReleaseAll();
                return await _prompts.ChooseDatabaseAsync(ChooserMode.Recovery, RecoveryReason.Corrupt, canonicalPath);
            }

            vm = opened.ViewModel;
        }
        catch (DatabaseFolderNotWritableException blockedEx)
        {
            // The database's folder refused writes (most often Windows Controlled Folder Access on a
            // protected known folder such as Documents). Show a neutral, actionable message, offer
            // the technical detail for IT on request, then close — the user can reopen and pick a
            // different location.
            await _prompts.ShowMessageAsync("Can't Save to This Folder", blockedEx.UserMessage);

            if (!string.IsNullOrEmpty(blockedEx.ItDetail))
            {
                var wantDetails = await _prompts.ShowChoiceAsync(
                    "Technical Details",
                    "Would you like to see technical details for your IT support?", "Yes", "No");
                if (wantDetails)
                    await _prompts.ShowMessageAsync("Technical Details (for IT)", blockedEx.ItDetail);
            }

            _host.ShutDown();
            return null;
        }

        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);
        await _host.SetupWindowAsync(canonicalPath, vm);

        // Restored unsaved changes are still only in the working copy — make them
        // durable right away rather than leaving them one crash away from a second
        // recovery. Failure is non-fatal: the standard save-error banner (wired by
        // SetupMainWindowAsync) surfaces it and autosave/manual save will retry.
        if (recoverUnsaved && _checkout.Mode == CheckoutMode.WriteAccess)
            await _checkout.SaveAsync();

        return null;
    }

    /// <summary>
    /// The housekeeping that precedes every checkout, in its one fixed order — shared by startup and by
    /// <see cref="SwitchCoreAsync"/> so the two can never drift apart again:
    /// <list type="number">
    /// <item><description>sweep stale working-copy artifacts that hold no user data
    /// (<see cref="CheckoutService.CleanupStaleCrashArtifacts"/>);</description></item>
    /// <item><description>if a previous session's unsaved changes remain
    /// (<see cref="CheckoutService.DetectCrashRecovery"/>), ask whether to restore them
    /// (<see cref="OfferCrashRecoveryAsync"/>).</description></item>
    /// </list>
    /// Both steps touch only this user's LOCAL files. The orphaned <c>D.tmp</c> on the share is NOT
    /// deleted here: it may be a colleague's in-flight save, so <see cref="CheckoutService"/> removes it
    /// itself, and only once this process holds the write lock (spec item 4).
    ///
    /// <para><b>Call this ONLY while this process holds the single-instance guard for
    /// <paramref name="dbPath"/></b> (<see cref="DatabaseInstanceGuard"/>): every step assumes no other
    /// live TermPoint of this user is working on the same local files — which is exactly what the guard
    /// guarantees, and what a second copy launched beside a hung first one would violate.</para>
    ///
    /// <para><b>Observer mode skips step 2.</b> Crash recovery means taking over a prior <i>write</i>
    /// session, which is wrong for an observer who never holds the lock. The test mirrors
    /// <see cref="CheckoutService.CheckoutAsync"/>'s own: the "open as observer" setting applies unless
    /// <paramref name="ignoreObserverMode"/> (a database the user has just created). Startup and switching
    /// previously disagreed here (switching offered recovery to observers).</para>
    /// </summary>
    /// <param name="dbPath">The database (D) about to be checked out.</param>
    /// <param name="ignoreObserverMode">True only for a database the user has just created, which opens for editing.</param>
    /// <returns>True when the user chose to restore unsaved changes (the checkout then runs in recovery mode).</returns>
    private async Task<bool> CleanUpAndOfferRecoveryAsync(string dbPath, bool ignoreObserverMode)
    {
        _checkout.CleanupStaleCrashArtifacts(dbPath);

        var isObserver = _settings().OpenInReaderMode && !ignoreObserverMode;
        if (!isObserver && _checkout.DetectCrashRecovery(dbPath))
            return await OfferCrashRecoveryAsync(dbPath);

        return false;
    }

    /// <summary>
    /// Tells the user another TermPoint of theirs is already open on this database
    /// (<see cref="DatabaseInstanceGuard.AlreadyOpenTitle"/> / <see cref="DatabaseInstanceGuard.AlreadyOpenMessage"/>).
    /// A Topmost dialog like every other message here, so it can never open behind the main window.
    /// </summary>
    private Task ShowAlreadyOpenMessageAsync()
        => _prompts.ShowMessageAsync(DatabaseInstanceGuard.AlreadyOpenTitle, DatabaseInstanceGuard.AlreadyOpenMessage);

    /// <summary>
    /// Startup only: shows the database chooser in recovery mode with <see cref="RecoveryReason.AlreadyOpen"/>
    /// after the guard refused a database, so the user can pick a different one or exit. Goes through the
    /// same <see cref="MainWindow.ShowDatabaseChooserAsync"/> as every other startup chooser (non-modal, hidden and
    /// never closed — the Avalonia 12 compositor deadlock), so Exit and Create New behave exactly as there.
    /// </summary>
    /// <param name="refusedPath">The database that is already open elsewhere; shown beneath the banner.</param>
    /// <returns>The database the user picked, or null when they exited or started the wizard.</returns>
    private Task<string?> ShowAlreadyOpenChooserAsync(string refusedPath)
        => _prompts.ChooseDatabaseAsync(ChooserMode.Recovery, RecoveryReason.AlreadyOpen, refusedPath);

    /// <summary>
    /// Runs the full checkout sequence for <paramref name="sourcePath"/> and returns
    /// the path that should be passed to <c>App.InitializeServices</c>.
    /// Handles stale-lock prompting inline.
    /// </summary>
    /// <param name="sourcePath">The database path D chosen by the user.</param>
    /// <param name="recoverUnsaved">
    /// True when the user chose to restore unsaved changes from a crashed session
    /// (see <see cref="OfferCrashRecoveryAsync"/>). Passed through to
    /// <see cref="CheckoutService.CheckoutAsync"/> / <see cref="CheckoutService.ForceCheckoutAsync"/>;
    /// when the checkout cannot deliver write access, the crash artifacts are kept on
    /// disk so recovery can be offered again at the next opportunity.
    /// </param>
    /// <param name="ignoreObserverMode">
    /// True only when the user has just created this database (File → New, or the wizard's new
    /// database): passed through to <see cref="CheckoutService.CheckoutAsync"/> so the "open as
    /// observer" setting does not make the brand-new database read-only. Everything else (license
    /// gate, lock, version gate) is unaffected. False for every ordinary open.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description><b>Write access:</b> D' (the local working copy).</description></item>
    /// <item><description><b>Read-only:</b> D'' (the local read-only snapshot).</description></item>
    /// <item><description><b>Stale lock — user declined takeover (or cancelled the short confirmation wait,
    /// <see cref="ShowStaleLockWaitAsync"/>), the holder proved to be active again at click
    /// time (<see cref="CheckoutOutcome.HolderActiveAgain"/>), force-checkout lost the race, or checkout failed:</b>
    /// D'' via <see cref="SetupSnapshotOrAskAsync"/>, with the lock file then watched
    /// (<see cref="CheckoutService.StartReaderPollingAsync"/>) so a later release offers "Take over as writer".
    /// D itself is NEVER returned: opening D with SQLite
    /// flips its header (breaking the active editor's next save) and holds it open.</description></item>
    /// <item><description><b><c>null</c>:</b> the checkout cannot proceed and the user has already been told
    /// why (network unreachable, database file missing, no local copy could be made and the user chose
    /// Close, or the version gate could not check the database and the user chose Close in the "in use"
    /// prompt — <see cref="AskTryAgainAfterVersionCheckAsync"/>; Try again repeats the whole checkout).
    /// Both callers treat <c>null</c> as "dialog shown, give up": startup closes the app, and
    /// <see cref="SwitchDatabaseAsync"/> — whose previous database is already closed by then — cleans up
    /// (<see cref="AbandonFailedSwitchAsync"/>) and shows the database chooser. Either way the caller must
    /// reset the working-copy bookkeeping (<see cref="CheckoutService.ResetAfterFailedOpenAsync"/>), because
    /// a failed checkout leaves <see cref="CheckoutService.WorkingPath"/> naming whatever was there before.</description></item>
    /// </list>
    /// </returns>
    private async Task<string?> RunCheckoutAsync(
        string sourcePath, bool recoverUnsaved = false, bool ignoreObserverMode = false)
    {
        var outcome = await _checkout.CheckoutAsync(sourcePath, recoverUnsaved, ignoreObserverMode);

        switch (outcome)
        {
            case CheckoutOutcome.WriteAccess:
                return _checkout.WorkingPath;

            case CheckoutOutcome.ReadOnly:
                // The "someone else is editing" explanation is wrong when the version gate
                // tripped (RequiredAppVersion set): nobody holds the lock, this app is simply too
                // old. The read-only banner explains that case, and the kept recovery data will be
                // offered again after the user updates.
                if (recoverUnsaved && _checkout.RequiredAppVersion is null)
                    await ShowRecoveryDeferredMessageAsync();
                return _checkout.WorkingPath;

            case CheckoutOutcome.RecoveryConflict:
                // The unsaved changes can't be applied automatically (D changed since
                // the crash, or the marker is unverifiable). Offer an exported copy,
                // then discard the artifacts and re-run a normal checkout.
                await HandleRecoveryConflictAsync(sourcePath);
                return await RunCheckoutAsync(sourcePath, recoverUnsaved: false, ignoreObserverMode);

            case CheckoutOutcome.StaleHolder:
                {
                    // The wording (who, how long, and the warning that the holder's unsaved changes
                    // can't go into the database) lives in StaleLockPrompt so it can be unit-tested.
                    // Closing the dialog counts as "Open read-only": never take over by accident.
                    var take = await _prompts.ShowChoiceAsync(
                        StaleLockPrompt.TakeOverTitle,
                        StaleLockPrompt.BuildTakeOverMessage(_checkout.CurrentHolder, DateTime.UtcNow),
                        StaleLockPrompt.TakeOverButton,
                        StaleLockPrompt.OpenReadOnlyButton);

                    // Spec item 9b: the takeover also needs the holder's heartbeat to have stood still for
                    // a full confirmation window on THIS computer's clock. Usually reading the prompt took
                    // long enough and there is nothing to wait for; otherwise show a short cancellable
                    // wait. Cancelling (or closing it) is exactly "Open read-only".
                    var wait = take ? _checkout.GetTakeOverWait() : TimeSpan.Zero;
                    if (wait > TimeSpan.Zero)
                        take = await _prompts.ShowStaleLockWaitAsync(wait);

                    if (take)
                    {
                        // ForceCheckoutAsync re-reads the lock file first and only breaks it if it is
                        // still the same lock the prompt was about (spec item 9a).
                        var forceOutcome = await _checkout.ForceCheckoutAsync(recoverUnsaved);
                        if (forceOutcome == CheckoutOutcome.WriteAccess)
                            return _checkout.WorkingPath;
                        if (forceOutcome == CheckoutOutcome.ReadOnly)
                        {
                            // Version gate tripped during the takeover: D'' is already set up and
                            // the banner explains why. Do not fall through to the "lost the race"
                            // path below, whose recovery message would be misleading.
                            return _checkout.WorkingPath;
                        }
                        if (forceOutcome == CheckoutOutcome.RecoveryConflict)
                        {
                            await HandleRecoveryConflictAsync(sourcePath);
                            return await RunCheckoutAsync(sourcePath, recoverUnsaved: false, ignoreObserverMode);
                        }
                        if (forceOutcome == CheckoutOutcome.NetworkUnreachable)
                        {
                            await _prompts.ShowMessageAsync("Network Unreachable",
                                "Cannot reach the database — check your network connection and try again.");
                            return null;
                        }
                        if (forceOutcome == CheckoutOutcome.SourceUnreadable)
                        {
                            // D is held open by another program (or access denied). The lock was
                            // released again; no read-only fallback is possible (it must read D too).
                            await _prompts.ShowMessageAsync(DatabaseSwitchPrecheck.UnreadableTitle,
                                DatabaseChooserViewModel.UnreadableDatabaseMessage);
                            return null;
                        }
                        if (forceOutcome == CheckoutOutcome.VersionCheckUnavailable)
                        {
                            // The version gate could not read (or raise) the stamp on the takeover's local copy.
                            // The forced lock was released again, so Try again is a whole new checkout.
                            return await AskTryAgainAfterVersionCheckAsync(sourcePath, recoverUnsaved, ignoreObserverMode);
                        }
                        if (forceOutcome == CheckoutOutcome.HolderActiveAgain)
                        {
                            // The holder woke up (or someone else took the lock) while the prompt was
                            // open; nothing was deleted. Say so, then open read-only below.
                            await _prompts.ShowMessageAsync(StaleLockPrompt.ActiveAgainTitle,
                                StaleLockPrompt.BuildActiveAgainMessage(_checkout.CurrentHolder));
                        }
                        // Otherwise lost the race to another instance — fall through to read-only setup.
                    }

                    // User declined, the holder is active again, or force-checkout lost the race.
                    // Set up D'' so we never hold D open directly. Crash artifacts (if
                    // any) stay on disk so recovery can be offered at the next launch.
                    // If D'' cannot be made, ask the user — never fall back to D itself.
                    if (recoverUnsaved)
                        await ShowRecoveryDeferredMessageAsync();
                    var readOnlyPath = await SetupSnapshotOrAskAsync();

                    // Watch the lock file, so that if the holder later releases cleanly (or goes
                    // stale again) this read-only session is offered "Take over as writer" instead
                    // of staying read-only until restart.
                    if (readOnlyPath is not null)
                        await _checkout.StartReaderPollingAsync();
                    return readOnlyPath;
                }

            case CheckoutOutcome.NetworkUnreachable:
                await _prompts.ShowMessageAsync("Network Unreachable",
                    "Cannot reach the database — check your network connection and try again.");
                return null;

            case CheckoutOutcome.SourceMissing:
                // D vanished between the caller's checks (or its creation) and checkout. Nothing
                // was locked or created, and there is no safe fallback — tell the user and stop.
                await _prompts.ShowMessageAsync(DatabaseSwitchPrecheck.MissingTitle,
                    DatabaseSwitchPrecheck.MissingMessage(sourcePath));
                return null;

            case CheckoutOutcome.SourceUnreadable:
                // D exists but another program holds it open (or access is denied). The checkout
                // already released any lock it took and left no working copy behind (crash-recovery
                // data, if any, is kept). A retry will usually succeed — and this is NOT damage.
                await _prompts.ShowMessageAsync(DatabaseSwitchPrecheck.UnreadableTitle,
                    DatabaseChooserViewModel.UnreadableDatabaseMessage);
                return null;

            case CheckoutOutcome.VersionCheckUnavailable:
                // The local copy was made, but the version gate could not read its MinimumVersionToWrite stamp
                // (or raise one) even after its quiet retries — typically antivirus still holding the file that
                // was just copied. Write access was refused, the lock released and (outside recovery) the copy
                // removed; recovery data, if any, is kept. Same prompt as the open-time integrity check.
                return await AskTryAgainAfterVersionCheckAsync(sourcePath, recoverUnsaved, ignoreObserverMode);

            default: // Failed
                await _prompts.ShowMessageAsync("Checkout Failed",
                    "Could not open the database for editing. " +
                    "The application will open in read-only mode.");
                // Open read-only on D''. Crash artifacts (if any) stay on disk for the next
                // launch. If D'' cannot be made, ask the user — never fall back to D itself.
                return await SetupSnapshotOrAskAsync();
        }
    }

    /// <summary>
    /// Handles <see cref="CheckoutOutcome.VersionCheckUnavailable"/> (spec item 3, step 3c): the version gate could
    /// not read, or raise, the database's stamp even after its quiet retries, so write access was refused. Shows the
    /// same "Couldn't Open the Database" prompt the open-time integrity check uses for a local copy that another
    /// program is holding (<see cref="LocalCopyCheckPrompt"/>; the cause is the same kind of thing and the wording
    /// is the user-approved one) with <b>Try again</b> / <b>Close</b>. Try again runs the whole checkout again with
    /// the same arguments (the previous attempt released its lock and, outside recovery, removed its copy, so it
    /// starts from scratch; a recovery open still finds its kept copy and marker). Close gives up exactly like
    /// the other failed checkouts: nothing is saved and no lock is left.
    /// </summary>
    /// <param name="sourcePath">The database path D chosen by the user.</param>
    /// <param name="recoverUnsaved">As for <see cref="RunCheckoutAsync"/>.</param>
    /// <param name="ignoreObserverMode">As for <see cref="RunCheckoutAsync"/>.</param>
    /// <returns>
    /// The result of the repeated <see cref="RunCheckoutAsync"/> when the user chose Try again; <c>null</c>
    /// (the prompt has been shown — the contract <see cref="RunCheckoutAsync"/> documents) when they chose Close.
    /// </returns>
    private async Task<string?> AskTryAgainAfterVersionCheckAsync(
        string sourcePath, bool recoverUnsaved, bool ignoreObserverMode)
    {
        var tryAgain = await _prompts.ShowChoiceAsync(
            LocalCopyCheckPrompt.CouldntOpenTitle,
            LocalCopyCheckPrompt.InUseMessage,
            LocalCopyCheckPrompt.TryAgainButton,
            LocalCopyCheckPrompt.CloseButton);

        return tryAgain
            ? await RunCheckoutAsync(sourcePath, recoverUnsaved, ignoreObserverMode)
            : null;
    }

    /// <summary>
    /// Sets up the local read-only snapshot D'' and, when that fails, asks the user whether to
    /// try again — instead of falling back to opening the shared database D itself (which changes
    /// D's header and holds it open; spec item 4). The retry loop is
    /// <see cref="CheckoutService.SetupReadOnlySnapshotWithRetryAsync"/>; this method only supplies
    /// the Topmost "Try again / Close" dialog.
    /// </summary>
    /// <returns>
    /// The path to D'' on success. <c>null</c> when the user chose Close; the dialog has already been
    /// shown, which is the contract <see cref="RunCheckoutAsync"/> documents for a <c>null</c> return.
    /// </returns>
    private Task<string?> SetupSnapshotOrAskAsync()
        => _checkout.SetupReadOnlySnapshotWithRetryAsync(() => _prompts.ShowChoiceAsync(
            LocalCopyCheckPrompt.CouldntOpenTitle,
            LocalCopyCheckPrompt.CouldntMakeCopyMessage,
            LocalCopyCheckPrompt.TryAgainButton, LocalCopyCheckPrompt.CloseButton));

    /// <summary>
    /// Tells the user their unsaved changes are kept but can't be restored right now
    /// because this session did not get write access. Shown when a recovery-mode
    /// checkout lands in read-only (another writer holds the lock, or the user
    /// declined a stale-lock takeover).
    /// </summary>
    private Task ShowRecoveryDeferredMessageAsync()
        => _prompts.ShowMessageAsync("Unsaved Changes Kept",
            "Someone else is currently editing this database, so your unsaved changes " +
            "can't be restored right now. They have been kept, and TermPoint will offer " +
            "to restore them the next time you open this database with write access.");

    // ── Open-time integrity check of the local copy (spec item 5, step 5d) ─────

    /// <summary>
    /// How <see cref="OpenWithLocalCopyCheckAsync"/> ended: the opened database, or null when the
    /// open was given up (the half-opened session has then been ended WITHOUT saving).
    /// </summary>
    /// <param name="ViewModel">The opened database, or null when the open was given up.</param>
    /// <param name="SharedDatabaseDamaged">
    /// True when the open was given up because the SHARED database itself is damaged (the user has been told); the
    /// caller then offers the recovery chooser with the damaged reason. False for every other give-up.
    /// </param>
    private sealed record LocalCopyOpenResult(IOpenedDatabase? ViewModel, bool SharedDatabaseDamaged);

    /// <summary>
    /// Runs <paramref name="initialize"/> (a call of <see cref="App.InitializeServices"/>) and handles what its
    /// open-time integrity check of the local copy can say (spec item 5, step 5d) — the ONE handler shared by
    /// startup and by database switching. The decisions are the pure table in
    /// <see cref="LocalCopyCheckDecision"/>; this method gathers the inputs, performs the action and asks again.
    ///
    /// <list type="bullet">
    /// <item><description><b>In use</b> (<see cref="DatabaseInUseException"/>: another program has the files, even
    /// after the quiet retries): "Couldn't Open the Database" with <b>Try again</b> / <b>Close</b>. Try again runs
    /// <paramref name="initialize"/> again (with its quiet retries); Close gives up.</description></item>
    /// <item><description><b>Damaged</b> (<see cref="DatabaseCorruptException"/>):
    /// <list type="number">
    /// <item><description>A copy holding recovered unsaved changes is set aside, never deleted
    /// (<see cref="CheckoutService.ArchiveCorruptWorkingCopy"/>), with the "Unsaved Changes Lost" message.</description></item>
    /// <item><description>The shared database is re-checked read-only (the front-door check). Damaged: say so, give
    /// up, and let the caller show the recovery chooser. Unreadable, unreachable or gone: say what the front door
    /// says for that case and give up. Fine: make ONE fresh copy
    /// (<see cref="CheckoutService.RefreshLocalCopyAfterFailedCheckAsync"/>) and run <paramref name="initialize"/>
    /// again — passing opens normally; failing again, or no fresh copy possible, sets the bad copy aside (kept) and
    /// shows the "keeps failing" message with a single Close.</description></item>
    /// </list></description></item>
    /// </list>
    ///
    /// <para><b>Giving up always ends the session WITHOUT saving</b> (<see cref="GiveUpLocalCopyOpenAsync"/>): the
    /// services are released and <see cref="CheckoutService.ResetAfterFailedOpenAsync"/> gives the lock back and
    /// forgets the working copy, so neither the caller's <c>Close()</c> nor the next switch can save or delete
    /// anything. A copy with a dirty marker is kept for the next start.</para>
    ///
    /// <para>Any other exception from <paramref name="initialize"/> (for example
    /// <see cref="DatabaseFolderNotWritableException"/>) propagates to the caller, unchanged.</para>
    /// </summary>
    /// <param name="initialize">
    /// Builds the services over the local copy and returns the opened database; may throw the two exceptions above.
    /// It is called again after a fresh copy and must therefore read the local file from
    /// <see cref="CheckoutService.WorkingPath"/> at the moment it is called, not from a path captured earlier: a
    /// fresh copy can change which file the checkout holds (spec item 3, step 3c) — a new D', or, if the version
    /// gate then found the database needs a newer TermPoint, a read-only D'' (<see cref="CheckoutService.RequiredAppVersion"/>
    /// set, so the read-only banner explains why).
    /// </param>
    /// <returns>The opened database, or a give-up (see <see cref="LocalCopyOpenResult"/>).</returns>
    private async Task<LocalCopyOpenResult> OpenWithLocalCopyCheckAsync(Func<IOpenedDatabase> initialize)
    {
        // Captured now: giving up forgets the session's paths (ResetAfterFailedOpenAsync).
        var sharedPath = _checkout.SourcePath;
        var shared     = SharedDatabaseVerdict.NotChecked;
        var fresh      = FreshCopyState.NotTried;

        // Set once the recovered-changes archive has been attempted, so a marker that somehow cannot be removed
        // can never make the table ask for the archive (and show its message) over and over.
        var recoveredChangesHandled = false;

        while (true)
        {
            LocalCopyVerdict copy;
            try
            {
                var vm = initialize();
                if (fresh == FreshCopyState.Made)
                    _logger().LogInfo(_checkout.RequiredAppVersion is null
                        ? "LocalCopyCheck: the database opened normally with a fresh copy."
                        : "LocalCopyCheck: the database opened read-only with a fresh copy (the version gate found it needs a newer TermPoint).");
                return new LocalCopyOpenResult(vm, SharedDatabaseDamaged: false);
            }
            catch (DatabaseInUseException ex)
            {
                copy = LocalCopyVerdict.Locked;
                _logger().LogInfo($"LocalCopyCheck: the local copy is in use by another program ({ex.Summary}).");
            }
            catch (DatabaseCorruptException ex)
            {
                copy = LocalCopyVerdict.Damaged;
                _logger().LogInfo($"LocalCopyCheck: the local copy is damaged ({ex.Summary}).");
            }

            // Ask the table what to do until it says to open again, or to give up.
            var openAgain = false;
            while (!openAgain)
            {
                var holdsRecoveredChanges = !recoveredChangesHandled && _checkout.HasDirtyMarker;
                var action = LocalCopyCheckDecision.Decide(copy, holdsRecoveredChanges, shared, fresh);
                _logger().LogInfo($"LocalCopyCheck: {copy} copy, shared database {shared}, fresh copy {fresh} -> {action}.");

                switch (action)
                {
                    case LocalCopyAction.ShowInUseDialog:
                        if (!await _prompts.ShowChoiceAsync(
                                LocalCopyCheckPrompt.CouldntOpenTitle,
                                LocalCopyCheckPrompt.InUseMessage,
                                LocalCopyCheckPrompt.TryAgainButton,
                                LocalCopyCheckPrompt.CloseButton))
                            return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);
                        openAgain = true;
                        break;

                    case LocalCopyAction.ArchiveRecoveredChangesThenContinue:
                        // The recovered unsaved changes are in a damaged file: set it aside (kept, not deleted)
                        // and say so honestly. Asking again then finds no marker.
                        recoveredChangesHandled = true;
                        _checkout.ArchiveCorruptWorkingCopy(sharedPath);
                        await _prompts.ShowMessageAsync(
                            LocalCopyCheckPrompt.UnsavedChangesLostTitle, LocalCopyCheckPrompt.UnsavedChangesLostMessage);
                        break;

                    case LocalCopyAction.CheckSharedDatabase:
                        shared = LocalCopyIntegrity.ToVerdict(
                            await LocalCopyIntegrity.RecheckSharedDatabaseAsync(sharedPath, _integrityFaults(), _logger()));
                        break;

                    case LocalCopyAction.MakeFreshCopy:
                        // Nothing has the local copy open (the gate throws before any connection is made), but
                        // release the half-built services anyway so nothing can.
                        _host.ReleaseServices();
                        // Made → open again. Otherwise ask the table again: a copy that couldn't be made because the
                        // shared location stopped responding or a file was held gets the front-door message for that;
                        // any other failure gets "keeps failing".
                        (shared, fresh) = LocalCopyIntegrity.AfterFreshCopy(
                            await _checkout.RefreshLocalCopyAfterFailedCheckAsync(), shared);
                        openAgain = fresh == FreshCopyState.Made;
                        break;

                    case LocalCopyAction.ShowKeepsFailing:
                        // Kept, not deleted: the bad file is evidence for IT. Then the session ends.
                        _checkout.SetAsideFailedLocalCopy();
                        await _prompts.ShowMessageAsync(
                            LocalCopyCheckPrompt.CouldntOpenTitle, LocalCopyCheckPrompt.KeepsFailingMessage,
                            LocalCopyCheckPrompt.CloseButton);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);

                    case LocalCopyAction.ShowDamagedThenRecoveryChooser:
                        await _prompts.ShowMessageAsync(
                            LocalCopyCheckPrompt.DamagedSharedTitle, LocalCopyCheckPrompt.DamagedSharedMessage);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: true);

                    case LocalCopyAction.ShowUnreadableMessage:
                    case LocalCopyAction.ShowUnreachableMessage:
                    case LocalCopyAction.ShowMissingMessage:
                        var (title, message) = LocalCopyCheckPrompt.FrontDoorMessage(shared, sharedPath);
                        await _prompts.ShowMessageAsync(title, message);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);

                    default:
                        // OpenNormally cannot happen here (the copy failed); treat any surprise as "open again".
                        openAgain = true;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Ends a half-opened session WITHOUT saving, so a give-up in <see cref="OpenWithLocalCopyCheckAsync"/> can
    /// never push a damaged or recovered local copy over the shared database, and nothing later can delete one:
    /// <list type="number">
    /// <item><description><see cref="App.ReleaseServices"/> disposes the container that
    /// <see cref="App.InitializeServices"/> built before its check threw.</description></item>
    /// <item><description><see cref="CheckoutService.ResetAfterFailedOpenAsync"/> stops autosave, gives back the write
    /// lock WITHOUT saving, parks <see cref="CheckoutService.Mode"/> at read-only (so the exit path of
    /// <see cref="MainWindow.OnClosing"/> sees no write session and saves nothing), keeps a working copy that has a dirty marker
    /// (unsaved work is never deleted) and forgets the session's paths (so <c>CleanupWorkingCopy</c> has nothing to
    /// delete).</description></item>
    /// </list>
    /// Each step is attempted even if the other failed; neither throws. The single-instance guard is left to the
    /// caller, as each path already handles it.
    /// </summary>
    /// <param name="sharedDatabaseDamaged">Whether the shared database itself was found damaged (passed through to the result).</param>
    /// <returns>A give-up result (no opened database).</returns>
    private async Task<LocalCopyOpenResult> GiveUpLocalCopyOpenAsync(bool sharedDatabaseDamaged)
    {
        _logger().LogInfo("LocalCopyCheck: giving up on this open — ending the session without saving.");

        try { _host.ReleaseServices(); }
        catch (Exception ex) { _logger().LogError(ex, "LocalCopyCheck: could not dispose the services of the failed open"); }

        try { await _checkout.ResetAfterFailedOpenAsync(); }
        catch (Exception ex) { _logger().LogError(ex, "LocalCopyCheck: could not reset the checkout state after the failed open"); }

        return new LocalCopyOpenResult(null, sharedDatabaseDamaged);
    }

    /// <summary>
    /// Switches to a different database without restarting the app. Handles the full
    /// release → checkout → re-initialize cycle. Called from the Files menu, from the
    /// startup wizard completion path, and from <c>AcquireWriteLock</c> in the ViewModel
    /// when upgrading from read-only to write mode.
    /// If the database file does not yet exist AND the caller passed <paramref name="createIfMissing"/>
    /// (File → New only), it is created with the schema before checkout — built locally and copied
    /// to the share by <see cref="NewDatabaseFile.CreateAsync"/>, never opened over the network — so
    /// that <see cref="CheckoutService.CheckoutAsync"/> takes the normal D → D' path and all writes go
    /// to the local working copy.
    ///
    /// <para><b>Pre-check before any teardown:</b> the first thing this does is validate the target with
    /// <see cref="DatabaseSwitchPrecheck"/> (the same validator the database chooser uses). If the
    /// target is missing (and may not be created), held open by another program, damaged or
    /// unreachable, the user is told why and this RETURNS with the current database still open and
    /// <see cref="AppSettings.DatabasePath"/> and the recent list unchanged. Previously every one of
    /// those cases was discovered only after the current database had been saved and disposed — and a
    /// missing target silently produced a new, empty database at the old path.</para>
    ///
    /// <para><b>Single-instance guard (spec item 4b), the very first step:</b> the guard for the TARGET
    /// database is taken before the pre-check and before anything is closed
    /// (<see cref="DatabaseInstanceGuard"/>). If another live TermPoint of this user already holds it, the
    /// user is told ("TermPoint Is Already Open") and the switch is refused exactly like a pre-check
    /// refusal: they stay on the current database, or — with none open — go to the chooser. Order of the
    /// guards through a switch from A to B: acquire B (refused → stay on A, nothing touched); pre-check
    /// refusal → release B, keep A; A torn down → release A, keep B (a no-op for A when B is the same
    /// database, e.g. "Take over as writer"); any failure after that
    /// (<see cref="AbandonFailedSwitchAsync"/>) → release B, so the no-database state holds no guard.</para>
    ///
    /// <para>May be called before any DI container exists (the wizard's existing-database path only
    /// validates D and saves settings), so every <see cref="App.Services"/> access before
    /// <see cref="App.InitializeServices"/> is null-tolerant.</para>
    ///
    /// <para><b>A database the user has just created opens for editing</b> even when the "open as
    /// observer" setting is on (they must be able to write into it straight away — File → New
    /// applies the transferred configuration right after this returns). That covers a file this
    /// call creates itself and, via <paramref name="isNewDatabase"/>, the wizard's new database.
    /// Only the observer setting is overridden, for this one checkout; the license gate, the lock
    /// and crash-recovery steps behave as for any other open, and the setting still applies to every
    /// later open of the database.</para>
    ///
    /// <para><b>When the switch fails (spec item 4d):</b> a failure BEFORE the previous database is touched
    /// (the pre-check's refusal) leaves the user on it, exactly as before — provided a database is actually
    /// open (<see cref="ISessionHost.IsDatabaseOpen"/>). Once it has been closed, or when no database is open to begin
    /// with (retrying from the chooser, the post-wizard open), a failure ends in the same clean "no database
    /// open" state as startup: the failed attempt is cleaned up (<see cref="AbandonFailedSwitchAsync"/>), the
    /// window is covered, and the startup database chooser is shown in recovery mode
    /// (<see cref="RecoveryReason.SwitchFailed"/>) — repeatedly, until a database opens or the user chooses
    /// Exit (or Create New, which hands over to the setup wizard). See <see cref="DatabaseSwitchRecovery"/>.</para>
    /// </summary>
    /// <param name="newDatabasePath">Full path of the database (D) to switch to.</param>
    /// <param name="isNewDatabase">
    /// True when the caller has already created this database during the current user action (the
    /// startup wizard's new-database path), so the observer setting must not make it read-only.
    /// False for every other caller; a file created by this very call is detected internally.
    /// </param>
    /// <param name="createIfMissing">
    /// True ONLY from File → New: a missing <paramref name="newDatabasePath"/> is then created. False
    /// (the default) for every other caller — Recent…, File → Open, "Switch to edit mode", the
    /// post-wizard open — for which a missing file is an error ("Database Not Found"), never an
    /// invitation to create an empty database at a path that has moved. With <c>true</c>, a target
    /// that already EXISTS is refused too (File → New never adopts an existing file).
    /// </param>
    /// <param name="resolveKeptChanges">
    /// Null for every caller except the save-conflict banner (<see cref="ResolveSaveConflictAsync"/>, spec item 10,
    /// step 10a). When given, the current session is ended WITHOUT saving (its save would be refused anyway, and must
    /// never write D), its unsaved changes are NOT cleaned up as a normal switch would, and this callback — handed
    /// the old database's path D — decides their fate (save them as a separate file, or discard them) once the
    /// services are closed. See <see cref="SwitchCoreAsync"/>. Never carried into the chooser's retries.
    /// </param>
    /// <returns>
    /// <c>true</c> ONLY when the REQUESTED database ended up open: the checkout succeeded (write access
    /// or the read-only snapshot) and the services and window were set up for it. <c>false</c> on every
    /// refusal or failure — pre-check refusal (current database untouched), creation failure
    /// (unreachable / already exists), a checkout that could not proceed (dialog shown), or any
    /// exception (reported in an error dialog).
    ///
    /// <para><b>A <c>false</c> after the failure recovery above does NOT mean nothing is open.</b> The
    /// chooser may have opened a DIFFERENT database before this returns — the original request still failed,
    /// so the result is still <c>false</c>. That is what keeps File → New's configuration transfer from ever
    /// being written into a database the user picked from the chooser afterwards. Callers that go on to
    /// write into "the open database" — File → New — MUST check it; others may ignore it.</para>
    /// </returns>
    public async Task<bool> SwitchDatabaseAsync(
        string newDatabasePath, bool isNewDatabase = false, bool createIfMissing = false,
        Func<string, Task>? resolveKeptChanges = null)
    {
        var result = await SwitchCoreAsync(newDatabasePath, isNewDatabase, createIfMissing, resolveKeptChanges);

        if (result == SwitchResult.NoDatabaseOpen)
            await RecoverToDatabaseChooserAsync(newDatabasePath);

        return result == SwitchResult.Opened;
    }

    /// <summary>
    /// One attempt to switch databases: the pre-check, the teardown of the current database, the checkout
    /// and the wiring of the new one — everything <see cref="SwitchDatabaseAsync"/> documents EXCEPT the
    /// recovery to the chooser. Reports how it ended and, for the one failure that needs it, cleans up
    /// after itself, so the caller (and the chooser loop, which calls this again for each pick) only has
    /// to look at the result.
    /// </summary>
    /// <param name="newDatabasePath">Full path of the database (D) to switch to.</param>
    /// <param name="isNewDatabase">See <see cref="SwitchDatabaseAsync"/>.</param>
    /// <param name="createIfMissing">See <see cref="SwitchDatabaseAsync"/>.</param>
    /// <param name="resolveKeptChanges">
    /// See <see cref="SwitchDatabaseAsync"/>. When non-null this is a save-conflict resolution, and three things
    /// differ from an ordinary switch: the old session is released with <c>saveFirst: false</c>; the services are
    /// closed as usual; and then this callback runs INSTEAD of the usual cleanup of the old working copy (which would
    /// delete the user's only copy of their changes). The pre-check and the single-instance guard still run first,
    /// so a refusal leaves the session untouched.
    /// </param>
    /// <returns>
    /// <see cref="SwitchResult.Opened"/>; <see cref="SwitchResult.StayedOnCurrentDatabase"/> after a refusal
    /// that left the open database untouched; or <see cref="SwitchResult.NoDatabaseOpen"/> (after a failure
    /// past the point of no return, <see cref="AbandonFailedSwitchAsync"/> has already run). A failure of the
    /// switch itself is reported in a dialog and returned, not thrown.
    /// </returns>
    private async Task<SwitchResult> SwitchCoreAsync(
        string newDatabasePath, bool isNewDatabase, bool createIfMissing,
        Func<string, Task>? resolveKeptChanges = null)
    {
        _logger().LogBreadcrumb("SwitchDatabase", new() { ["path"] = newDatabasePath });
        var phase = SwitchPhase.BeforeTeardown;
        _noDatabaseReason = RecoveryReason.SwitchFailed;
        try
        {
            // ── Single-instance guard for the TARGET (spec item 4b) ──────────────────────────────
            // Taken first, before the pre-check and before anything is closed or recorded, and before
            // any cleanup or crash detection below. If another live TermPoint of this user has the
            // target open, nothing at all happens to it (no cleanup, no recovery offer, no lock, no
            // copy) and nothing here changes: the open database stays open, and the settings and the
            // recent list are untouched. With no database open (the chooser retry, the post-wizard
            // open) the chooser follows instead, saying why.
            if (_instanceGuard.TryAcquire(newDatabasePath) == InstanceGuardResult.HeldByAnotherProcess)
            {
                _logger().LogInfo("SwitchDatabase: refused before teardown — the database is already open in another TermPoint process.");
                _noDatabaseReason = RecoveryReason.AlreadyOpen;
                await ShowAlreadyOpenMessageAsync();
                return DatabaseSwitchRecovery.Decide(phase, _host.IsDatabaseOpen);
            }

            // Refuse BEFORE touching anything — settings, recents, the current database's save and
            // teardown — when the target can't be opened. Only a target that passes updates
            // AppSettings.DatabasePath and the recent list (see DatabaseSwitchPrecheck).
            var precheck = await DatabaseSwitchPrecheck.CheckAndRecordAsync(
                newDatabasePath, createIfMissing, _settings());
            if (!precheck.CanProceed)
            {
                // The target will not be opened: give its guard back, keeping the open database's own.
                ReleaseSwitchTargetGuard();

                _logger().LogInfo($"SwitchDatabase: refused before teardown — {precheck.Title}");
                await _prompts.ShowMessageAsync(precheck.Title, precheck.Message);

                // The open database (if any) is untouched: stay on it. With NO database open (retrying
                // from the chooser, the post-wizard open) there is nothing to stay on — the chooser follows.
                return DatabaseSwitchRecovery.Decide(phase, _host.IsDatabaseOpen);
            }

            // ── Point of no return ───────────────────────────────────────────────────────────────
            // From here the previous database is taken apart. Whatever happens next, nothing is open
            // any more, so any failure must end in the no-database state (AbandonFailedSwitchAsync +
            // the chooser) — never in a window still bound to a database that has been closed.
            phase           = SwitchPhase.AfterTeardown;
            _host.IsDatabaseOpen = false;

            // Release the current checkout (saves if dirty) before opening the new DB.
            var oldSourcePath = _checkout.SourcePath;
            var oldDbName     = Path.GetFileNameWithoutExtension(oldSourcePath);
            var wasWriteMode  = _checkout.Mode == CheckoutMode.WriteAccess;
            _checkout.StopAutoSave();

            // A save-conflict resolution (resolveKeptChanges != null) NEVER saves: the save would be refused
            // (D changed elsewhere) and must never write D. The lock is simply given back.
            var saveOutcome = await _checkout.ReleaseAsync(saveFirst: wasWriteMode && resolveKeptChanges is null);
            CrashPoints.Hit("Switch.AfterRelease");

            // Capture the current semester selection before DI teardown so it can be
            // restored after reinit without touching AppSettings (avoids a disk round-trip
            // and preserves the live in-memory state, e.g. when upgrading read→write).
            string? savedYearId = null;
            IReadOnlySet<string>? savedSemesterIds = null;
            // App.Services is null when no database has been opened yet (wizard join path).
            _host.TryCaptureSemesterSelection(out savedYearId, out savedSemesterIds);

            // Stop the periodic backup timer BEFORE disposing the DI container.
            _host.StopBackupSession();

            // Close the old DatabaseContext (and all other DI singletons) NOW, before
            // CheckoutAsync copies D to D'. Also clears App.Services (the same "nothing open" value
            // as at startup), so the lines above — which this method runs again when the chooser
            // retries — never meet a disposed container.
            _host.ReleaseServices();
            CrashPoints.Hit("Switch.AfterServicesReleased");

            if (resolveKeptChanges is not null)
            {
                // ── SAVE-CONFLICT RESOLUTION (spec item 10, step 10a) ────────────────────────────
                // THE ORDINARY CLEANUP BELOW MUST NOT RUN HERE. With saveFirst:false the release
                // reports Success, so "saveOutcome == Success" would DELETE D' — and D' holds the
                // user's changes, which were never written anywhere else. They are the only copy.
                //
                // The services are closed (App.ReleaseServices above closed the connection), so from
                // here nothing in any window can write D'. The callback saves D' as a separate file
                // and deletes it only after that copy is confirmed, or deletes it after a confirmed
                // Discard — and if it fails or throws, D' and its marker stay: the reopen's crash
                // recovery offers them again (spec item 16a's loop), or the failed-switch path
                // (ResetAfterFailedOpenAsync) keeps a marked D'. Nothing is deleted in a catch.
                await resolveKeptChanges(oldSourcePath);
            }
            // Delete the old D' ONLY when its content is safely in the old D (save
            // succeeded) or nothing was edited (no dirty marker). Otherwise leave the
            // working copy and marker behind — the next time the user opens that
            // database they'll be offered their unsaved changes back.
            else if (saveOutcome == SaveOutcome.Success || !_checkout.HasDirtyMarker)
            {
                _checkout.CleanupWorkingCopy();
            }
            else if (wasWriteMode)
            {
                await _prompts.ShowMessageAsync("Changes Not Saved",
                    $"Your latest changes to \"{oldDbName}\" could not be saved. " +
                    "They have been kept, and TermPoint will offer to restore them " +
                    "the next time you open that database.");
            }
            CrashPoints.Hit("Switch.AfterOldCopyHandled");

            // The previous database is completely closed — its final save done, its working copy deleted
            // or deliberately kept — so give ITS guard back and keep only the target's. (When the target
            // is the very database that was open, e.g. "Take over as writer", its guard is the one kept.
            // With nothing open before, there was nothing to release.) From here until a database is
            // wired to the window, the held guard belongs to the database being opened; if that fails,
            // AbandonFailedSwitchAsync releases it.
            _instanceGuard.ReleaseAllExcept(newDatabasePath);

            // If D does not exist yet and the caller asked for creation (File → New ONLY), create
            // it with the schema so CheckoutAsync takes the normal D → D' path and all subsequent
            // writes go to the local working copy. Deadline-bounded tri-state probe: only create
            // when the location answered "missing" — an unreachable share must NOT trigger
            // creation attempts; checkout below surfaces the unreachable dialog instead.
            // The file is built in a LOCAL temp file and copied across by NewDatabaseFile:
            // SQLite never opens the shared path (no WAL header left on D, no handle held).
            // When creation is NOT requested, a file that vanished after the pre-check (it passed
            // a moment ago) is left alone: CheckoutAsync reports SourceMissing ("Database Not
            // Found") instead of this method inventing an empty database.
            if (createIfMissing
                && await NetworkFileOps.ProbeFileAsync(newDatabasePath) == FileProbeResult.Missing)
            {
                var created = await NewDatabaseFile.CreateAsync(newDatabasePath);
                switch (created.Outcome)
                {
                    case NewDatabaseOutcome.Created:
                        isNewDatabase = true;   // just created: the observer setting must not make it read-only
                        break;

                    case NewDatabaseOutcome.Unreachable:
                        await _prompts.ShowMessageAsync("Network Unreachable", NetworkFileOps.UnreachableMessage);
                        return await AbandonFailedSwitchAsync();

                    case NewDatabaseOutcome.AlreadyExists:
                        // Someone created a file with this name while we were building ours. It was
                        // not touched; don't silently open somebody else's database as "the new one".
                        // (Same wording as the pre-check's refusal of an existing file for File → New.)
                        await _prompts.ShowMessageAsync(DatabaseSwitchPrecheck.AlreadyExistsTitle,
                            DatabaseSwitchPrecheck.AlreadyExistsMessage(newDatabasePath));
                        return await AbandonFailedSwitchAsync();

                    default: // Failed — the outer catch logs it and shows "Failed to switch to database: …"
                        throw new InvalidOperationException(
                            $"The new database could not be created: {created.Error?.Message}", created.Error);
                }
            }

            // Re-evaluate license for the new share directory before checkout.
#if !BROWSER
            _evaluateLicense(newDatabasePath);
#endif

            // Run crash recovery + checkout for the new path. The guard for it was taken at the top, so
            // this housekeeping (stale-artifact sweep, recovery offer) cannot disturb a
            // live copy of TermPoint on the same database. Observers are not offered recovery, as at startup.
            var recoverUnsaved = await CleanUpAndOfferRecoveryAsync(newDatabasePath, ignoreObserverMode: isNewDatabase);

            var checkoutPath = await RunCheckoutAsync(
                newDatabasePath, recoverUnsaved, ignoreObserverMode: isNewDatabase);
            if (checkoutPath is null)
            {
                // Checkout cannot proceed — dialog already shown (see RunCheckoutAsync). The previous
                // database is already closed, so this ends in the no-database state, not a dead window.
                return await AbandonFailedSwitchAsync();
            }

            // Reinitialize DI and set new data context. The open-time integrity check of the local copy
            // (spec item 5, step 5d) is handled here exactly as at startup: a copy another program is using
            // (Try again / Close) or a damaged one (one fresh copy, or the recovery chooser). It returns no
            // view model only after ending the half-opened session WITHOUT saving; the previous database is
            // already closed, so that ends in the no-database state — with the damaged reason when it is the
            // shared database that is damaged. The raw "Failed to switch to database: ..." text below can
            // therefore never name the local copy's path for these two failures.
            //
            // As at startup, the local file is read from the checkout at the moment of each call (spec item 3,
            // step 3c), because a fresh copy can leave the checkout holding a different file: a new D', or a
            // read-only D'' when the version gate ran again on the fresh copy and found it needs a newer
            // TermPoint. (checkoutPath above is that file for the first call; it is only used for the null test.)
            var opened = await OpenWithLocalCopyCheckAsync(
                () => _host.OpenServices(_checkout.WorkingPath, savedYearId, savedSemesterIds));
            if (opened.ViewModel is null)
            {
                if (opened.SharedDatabaseDamaged)
                    _noDatabaseReason = RecoveryReason.Corrupt;
                return await AbandonFailedSwitchAsync();
            }
            var vm = opened.ViewModel;

            // Resolve the canonical source path D for the title bar and backup service.
            var canonicalPath = _checkout.SourcePath;

            vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);

            // Delegate the rest of window wiring (backup session, DataContext,
            // checkout event (re-)subscription, autosave restart, etc.) to the
            // shared helper used by every startup path.
            await _host.SetupWindowAsync(canonicalPath, vm);

            // The new database is now open and wired to the window (_isDatabaseOpen). The steps below
            // are refinements: a failure in one of them must not turn an open database into a failed
            // switch (which would send the user to the chooser, or make File → New skip its configuration
            // transfer). Log it — LogError also raises the standard error banner — and carry on.
            try
            {
                // Re-apply the semester selection after the DataContext swap.
                if (savedYearId != null && savedSemesterIds != null)
                    vm.RestoreSemesterSelection(savedYearId, savedSemesterIds);

                // Restored unsaved changes are still only in the working copy — make them
                // durable right away (mirrors the startup path; failure surfaces through
                // the standard save-error banner).
                if (recoverUnsaved && _checkout.Mode == CheckoutMode.WriteAccess)
                    await _checkout.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger().LogError(ex, "SwitchDatabase: a step after the database opened failed");
            }

            return SwitchResult.Opened;   // the new database is open and the window is wired to it
        }
        catch (Exception ex)
        {
            _logger().LogError(ex, "Failed to switch database");
            await _prompts.ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");

            // Past the point of no return the previous database is gone: clean up and go to the chooser.
            // Before it, the open database (if any) was never touched and stays — and so does its guard;
            // only the target's, taken at the top, is given back.
            if (phase == SwitchPhase.AfterTeardown)
                return await AbandonFailedSwitchAsync();

            ReleaseSwitchTargetGuard();
            return DatabaseSwitchRecovery.Decide(phase, _host.IsDatabaseOpen);
        }
    }

    /// <summary>
    /// Gives back the single-instance guard that <see cref="SwitchCoreAsync"/> took for a switch target
    /// that is NOT going to be opened (the pre-check refused it, or something threw before the old
    /// database was touched), while keeping the guard of the database that is still open. With no
    /// database open nothing is kept, so the no-database state holds no guard. Never throws.
    /// </summary>
    private void ReleaseSwitchTargetGuard()
        => _instanceGuard.ReleaseAllExcept(_host.IsDatabaseOpen ? _checkout.SourcePath : null);

    /// <summary>
    /// Cleans up after a switch that FAILED once the previous database had been closed (spec item 4d),
    /// returning the app to the same "no database open" state as startup — the state the database
    /// chooser then starts from. Never throws; each step is attempted even if an earlier one failed.
    ///
    /// <list type="number">
    /// <item><description><see cref="ISessionHost.IsDatabaseOpen"/> is cleared (a failure part-way through wiring the
    /// window may have left it set).</description></item>
    /// <item><description><see cref="App.ReleaseServices"/> disposes a container left half-built by a failed
    /// <see cref="App.InitializeServices"/> (closing its connection to D') — a no-op when it is already
    /// gone — and leaves <c>App.Services</c> null.</description></item>
    /// <item><description><see cref="CheckoutService.ResetAfterFailedOpenAsync"/> gives back a lock the failed
    /// attempt may have taken and forgets the working-copy bookkeeping, so that choosing Exit in the
    /// chooser can never delete a kept working copy (the previous database's unsaved changes) through a
    /// stale <c>WorkingPath</c>. A working copy with a dirty marker is never deleted.</description></item>
    /// <item><description><see cref="DatabaseInstanceGuard.ReleaseAll"/> gives back the single-instance guard
    /// that was taken for the database whose open just failed (spec item 4b), so the no-database state holds
    /// no guard and the user can pick that same database again, or another process may open it. Done LAST,
    /// after the working-copy bookkeeping above, because the guard is what keeps a second TermPoint out of
    /// those files while they are being sorted out.</description></item>
    /// </list>
    /// Covering the window is left to the chooser step (<see cref="ShowSwitchFailedChooserAsync"/>), which
    /// runs immediately after and for the refusal path as well.
    /// </summary>
    /// <returns>Always <see cref="SwitchResult.NoDatabaseOpen"/>, so a failure site can <c>return await</c> it.</returns>
    private async Task<SwitchResult> AbandonFailedSwitchAsync()
    {
        _host.IsDatabaseOpen = false;
        _logger().LogInfo("SwitchDatabase: failed after the previous database was closed — returning to the no-database state.");

        try { _host.ReleaseServices(); }
        catch (Exception ex) { _logger().LogError(ex, "SwitchDatabase: could not dispose the services of the failed attempt"); }

        try { await _checkout.ResetAfterFailedOpenAsync(); }
        catch (Exception ex) { _logger().LogError(ex, "SwitchDatabase: could not reset the checkout state after the failed attempt"); }

        _instanceGuard.ReleaseAll();

        return DatabaseSwitchRecovery.Decide(SwitchPhase.AfterTeardown, databaseOpen: false);
    }

    /// <summary>
    /// The "no database open" recovery that follows a failed switch (spec item 4d): shows the startup
    /// database chooser in recovery mode and keeps showing it until a database opens or the user leaves
    /// (<see cref="DatabaseSwitchRecovery.RunChooserLoopAsync"/>). Each pick is opened through
    /// <see cref="SwitchCoreAsync"/> itself — the same pre-check, checkout and wiring as any switch, which
    /// tolerates a null <c>App.Services</c> — and a pick that fails comes back to the chooser.
    ///
    /// <para>If the chooser itself cannot be shown the app has no way forward, so — like a
    /// failure during startup — the error is reported and the app shuts down.</para>
    /// </summary>
    /// <param name="failedPath">The database whose open failed; named in the chooser's banner.</param>
    private async Task RecoverToDatabaseChooserAsync(string failedPath)
    {
        try
        {
            _logger().LogInfo("SwitchDatabase: no database is open — showing the database chooser.");
            var opened = await DatabaseSwitchRecovery.RunChooserLoopAsync(
                failedPath,
                ShowSwitchFailedChooserAsync,
                async chosen => await SwitchCoreAsync(chosen, isNewDatabase: false, createIfMissing: false)
                                == SwitchResult.Opened);
            _logger().LogInfo(opened
                ? "SwitchDatabase: a database was opened from the chooser."
                : "SwitchDatabase: the chooser ended without opening a database (exit, or the setup wizard took over).");
        }
        catch (Exception ex)
        {
            _logger().LogError(ex, "SwitchDatabase: could not show the database chooser after a failed switch");
            await _host.ReportFatalAndShutDownAsync(ex);
        }
    }

    /// <summary>
    /// Covers the window and shows the startup database chooser in recovery mode with
    /// <see cref="_noDatabaseReason"/> — <see cref="RecoveryReason.SwitchFailed"/>, or
    /// <see cref="RecoveryReason.AlreadyOpen"/> when the single-instance guard refused the database (spec
    /// item 4b). Chooser outcomes behave exactly as at startup, because
    /// this goes through the very same <see cref="MainWindow.ShowDatabaseChooserAsync"/>: a pick is returned, Create
    /// New hands over to the setup wizard (null), Exit shows the cancel message and shuts down (null). The
    /// chooser is shown NON-modally and never closed, as there (Avalonia 12 compositor deadlock).
    /// </summary>
    /// <param name="failedPath">The database that could not be opened; shown in the banner and kept in the recent list.</param>
    /// <returns>The database the user picked, or null when they exited or started the wizard.</returns>
    private Task<string?> ShowSwitchFailedChooserAsync(string failedPath)
    {
        // Nothing bound to the closed database may be visible or clickable behind the chooser or the
        // error dialogs that follow it. Idempotent, so the repeat visits of the loop cost nothing.
        _host.CoverWindowForNoDatabase();
        return _prompts.ChooseDatabaseAsync(ChooserMode.Recovery, _noDatabaseReason, failedPath);
    }

    /// <summary>
    /// Offers crash recovery for <paramref name="sourcePath"/> when unsaved changes from
    /// a session that did not end cleanly are found on disk.
    ///
    /// <para>Sequence: verify the recovered data is structurally sound (a damaged file is
    /// archived and the user informed), then ask Restore-or-Discard. Restore does not copy
    /// anything by itself — it makes the subsequent checkout run in recovery mode
    /// (<see cref="CheckoutService.CheckoutAsync"/> with <c>recoverWorkingCopy: true</c>),
    /// which resumes the crashed session only after proving D is unchanged since the crash.</para>
    ///
    /// <para><b>The kept changes are deleted only on a confirmed Discard</b> (see
    /// <see cref="AskKeepOrDiscardAsync"/>): the window's X re-asks the question, and Discard first asks
    /// "Discard Unsaved Changes?".</para>
    ///
    /// <para><b>UX rule:</b> the user is never told about working copies or file paths —
    /// from their point of view TermPoint simply "kept their unsaved changes."</para>
    /// </summary>
    /// <param name="sourcePath">The database path with crash artifacts.</param>
    /// <returns>True when the checkout should run in recovery mode (user chose Restore).</returns>
    private async Task<bool> OfferCrashRecoveryAsync(string sourcePath)
    {
        var info = _checkout.InspectCrashRecovery(sourcePath);
        if (info is null) return false;

        if (!info.WorkingCopyIntact)
        {
            // The crash damaged the unsaved data beyond SQLite's own journal recovery.
            // Archive it (support-level salvage stays possible) and tell the user honestly.
            _checkout.ArchiveCorruptWorkingCopy(sourcePath);
            await _prompts.ShowMessageAsync(
                LocalCopyCheckPrompt.UnsavedChangesLostTitle, LocalCopyCheckPrompt.UnsavedChangesLostMessage);
            return false;
        }

        // Neutral phrasing: these artifacts can come from a crash, a failed
        // save-on-exit, or a lock-loss demotion — "did not close properly" would
        // be wrong for the latter two.
        // F1 / rule 3: the kept changes are the user's only copy, so neither the window's X nor a single click on
        // Discard may delete them — the helper keeps asking until the user presses Restore or confirms the discard.
        var restore = await AskKeepOrDiscardAsync("Restore Unsaved Changes?",
            "Unsaved changes from a previous session with this database were kept." +
            "\n\nWould you like to restore them?",
            "Restore", "Discard", RecoveryConflictPrompt.DiscardConfirmMessage);

        if (!restore)
        {
            _checkout.DiscardCrash(sourcePath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asks a keep-or-discard question about the user's kept unsaved changes and returns only when the user has
    /// made a real decision (data-persistence review, F1; rule 3 of the shared-database rules: never delete the only
    /// copy of someone's unsaved work on a click that was not a decision). Loops:
    /// <list type="bullet">
    /// <item><description>The window's X (<see cref="ChoiceResult.Closed"/>): asks the same question again. The X is not a decision.</description></item>
    /// <item><description>The keep button: returns true.</description></item>
    /// <item><description>The discard button: asks "Discard Unsaved Changes?" with [Discard] and [Go Back]. Discard returns
    /// false; Go Back, or that confirmation's own X, asks the original question again.</description></item>
    /// </list>
    /// Used by both crash-recovery questions ("Restore Unsaved Changes?" and "Can't Restore Unsaved Changes").
    /// </summary>
    /// <param name="title">Title of the original question.</param>
    /// <param name="message">Body of the original question.</param>
    /// <param name="keepLabel">Label of the original question's confirming button (the one that keeps the changes).</param>
    /// <param name="discardLabel">Label of the original question's other button (the one that leads to the confirmation).</param>
    /// <param name="confirmMessage">Body of the "Discard Unsaved Changes?" confirmation (texts in <see cref="RecoveryConflictPrompt"/>).</param>
    /// <returns>True when the user chose to keep the changes; false only when the user confirmed the discard.</returns>
    private async Task<bool> AskKeepOrDiscardAsync(
        string title, string message, string keepLabel, string discardLabel, string confirmMessage)
    {
        while (true)
        {
            var answer = await _prompts.ShowChoiceOrCloseAsync(title, message, keepLabel, discardLabel);

            if (answer == ChoiceResult.Affirmative)
                return true;

            if (answer == ChoiceResult.Negative)
            {
                // Discard is asked about before it is done. The confirmation's X is Go Back (ShowChoiceAsync folds
                // the X into its negative button), so only the Discard button deletes anything.
                var confirmed = await _prompts.ShowChoiceAsync(
                    RecoveryConflictPrompt.DiscardConfirmTitle, confirmMessage,
                    RecoveryConflictPrompt.DiscardConfirmButton, RecoveryConflictPrompt.GoBackButton);
                if (confirmed)
                    return false;
            }

            // Closed with the X, or Go Back: ask the original question again.
        }
    }

    /// <summary>
    /// Handles <see cref="CheckoutOutcome.RecoveryConflict"/>: the unsaved changes exist
    /// and are intact, but D was modified since the crash (or the marker is unverifiable),
    /// so restoring them automatically would overwrite someone else's work. Offers to
    /// export the unsaved version as a separate file, then discards the crash artifacts.
    /// A cancelled save-picker, a name that is already taken, an unreachable location or a
    /// failed copy re-offers the choice rather than silently discarding, and so does the
    /// window's X; Discard discards only after "Discard Unsaved Changes?" is confirmed
    /// (<see cref="AskKeepOrDiscardAsync"/>). The texts live in <see cref="RecoveryConflictPrompt"/>.
    /// </summary>
    /// <param name="sourcePath">The database path whose recovery hit a conflict.</param>
    private async Task HandleRecoveryConflictAsync(string sourcePath)
    {
        var info = _checkout.InspectCrashRecovery(sourcePath);

        while (info is not null)
        {
            // F1 / rule 3: only a CONFIRMED discard leaves the loop without a copy; the window's X just asks again.
            var export = await AskKeepOrDiscardAsync(
                RecoveryConflictPrompt.ConflictTitle,
                RecoveryConflictPrompt.ConflictMessage,
                RecoveryConflictPrompt.SaveCopyButton, RecoveryConflictPrompt.DiscardButton,
                RecoveryConflictPrompt.DiscardConfirmWithCopyMessage);

            if (!export)
                break;

            // Only stop asking once the copy actually landed — a cancelled picker, a refused
            // name, an unreachable location or a failed copy must not silently destroy the
            // user's last copy of their edits.
            if (await ExportRecoveredCopyAsync(sourcePath))
                break;
        }

        _checkout.DiscardCrash(sourcePath);
    }

    /// <summary>
    /// Shows a save-file picker and saves the unsaved changes held in the crash-recovery file
    /// as a NEW database at the chosen location. The exported file is a complete, standalone
    /// database that can be opened via Files → Open Database.
    ///
    /// <para>The work is done by <see cref="CheckoutService.ExportRecoveredCopyAsync"/>: a
    /// local snapshot (so changes still in the recovery file's WAL are included), placed without
    /// overwrite on a deadline, off the UI thread. The picker's own overwrite prompt is OFF
    /// (<c>ShowOverwritePrompt = false</c>) because an existing file — the shared database
    /// itself included — is never replaced: the user is told to choose a new name and the caller
    /// offers the export again. An unreachable location gets the network message, also followed
    /// by another offer.</para>
    /// </summary>
    /// <param name="sourcePath">Original database path D (used for the suggested filename and to find its recovery file).</param>
    /// <returns>True when the copy was written; false on picker cancel, a refused name, an unreachable location or a failure.</returns>
    private async Task<bool> ExportRecoveredCopyAsync(string sourcePath)
    {
        var destPath = await _prompts.PickSaveFileAsync(
            RecoveryConflictPrompt.SaveDialogTitle,
            RecoveryConflictPrompt.SuggestedFileName(sourcePath),
            "db");
        if (destPath is null) return false; // user cancelled

        var result = await _checkout.ExportRecoveredCopyAsync(sourcePath, destPath);
        return await ShowExportOutcomeAsync(result);
    }

    /// <summary>
    /// Turns the result of exporting the kept unsaved changes as a separate file
    /// (<see cref="CheckoutService.ExportRecoveredCopyAsync"/>, or the early
    /// <see cref="NewFilePlacement.RefuseIfPresentAsync"/> refusal) into what the user sees: nothing for
    /// <see cref="NewDatabaseOutcome.Created"/>; for a name that is taken, a location that does not answer, or a
    /// failure, the matching message (texts in <see cref="RecoveryConflictPrompt"/> and
    /// <see cref="DatabaseSwitchPrecheck"/>). The one mapping shared by crash recovery
    /// (<see cref="ExportRecoveredCopyAsync(string)"/>) and the save-conflict banner
    /// (<see cref="ResolveSaveConflictAsync"/>).
    /// </summary>
    /// <param name="result">The export (or early-refusal) result.</param>
    /// <returns>True when the copy was written (<see cref="NewDatabaseOutcome.Created"/>); false otherwise, after the message was shown.</returns>
    public async Task<bool> ShowExportOutcomeAsync(NewDatabaseResult result)
    {
        switch (result.Outcome)
        {
            case NewDatabaseOutcome.Created:
                return true;

            case NewDatabaseOutcome.AlreadyExists:
                await _prompts.ShowMessageAsync(
                    RecoveryConflictPrompt.AlreadyExistsTitle, RecoveryConflictPrompt.AlreadyExistsMessage);
                return false;

            case NewDatabaseOutcome.Unreachable:
                await _prompts.ShowMessageAsync(DatabaseSwitchPrecheck.UnreachableTitle, NetworkFileOps.UnreachableMessage);
                return false;

            default:
                _logger().LogError(result.Error, "Failed to export recovered copy");
                await _prompts.ShowMessageAsync(
                    RecoveryConflictPrompt.ExportFailedTitle, RecoveryConflictPrompt.ExportFailedMessage(result.Error));
                return false;
        }
    }

    // ── Restore from backup (spec item 5, step 5b) ───────────────────────────
    // RestoreFromBackupAsync sets these; OnClosing reads them. See RestoreFromBackupAsync for the sequence.

    /// <summary>
    /// True from the moment a restore starts saving the user's changes until the restore's outcome is known.
    /// While true, <see cref="MainWindow.OnClosing"/> refuses every close request: the window must never close while the
    /// database is being replaced, or the final save/teardown would race the restore. Reset in a
    /// <c>finally</c>, so it can never stay stuck.
    /// </summary>
    private bool _restoreInProgress;

    // ── Save conflict (spec item 10, step 10a) ───────────────────────────────

    /// <summary>
    /// True while the user's choice from the save-conflict banner (Save a Copy… / Discard Changes) is ending the
    /// session, from the moment it is confirmed until the kept changes have been saved elsewhere or discarded
    /// (<see cref="ResolveKeptChangesAsync"/>). While true, <see cref="MainWindow.OnClosing"/> refuses every close request,
    /// exactly as for <see cref="_restoreInProgress"/>: closing mid-way would race the teardown. Reset in a
    /// <c>finally</c>, so it can never stay stuck; it is also reset as soon as the kept changes are resolved,
    /// because the reopening that follows is no more delicate than an ordinary database switch.
    /// </summary>
    private bool _saveConflictInProgress;

    /// <summary>
    /// True once a restore has replaced D (or may have: an unconfirmed rename) and the application is about to
    /// close. <see cref="MainWindow.OnClosing"/> then tears the session down WITHOUT saving — the working copy is stale,
    /// and a save would be refused anyway — and discards the working copy and its crash marker: the restored D
    /// supersedes them, and everything was saved BEFORE the restore started, so nothing is lost.
    /// </summary>
    private bool _endSessionAfterRestore;

    /// <summary>
    /// The path of D as it was when the restore began (captured before anything is torn down). Used to discard
    /// the crash marker of the session that is ending (<see cref="CheckoutService.DiscardCrash"/>).
    /// </summary>
    private string? _restoredSourcePath;

    /// <summary>
    /// True when <see cref="MainWindow.OnClosing"/> should start a fresh copy of TermPoint after the single-instance guard
    /// is released: after a confirmed restore ("TermPoint will now restart"). False after an unconfirmed
    /// one, where the user is told to reopen TermPoint themselves once the connection is back.
    /// </summary>
    private bool _relaunchAfterClose;

    /// <summary>
    /// True while a restore from a backup is running (see <see cref="RestoreFromBackupAsync"/>). Read by
    /// <see cref="MainWindow.OnClosing"/>, which refuses every close request while it is true.
    /// </summary>
    public bool IsRestoreInProgress => _restoreInProgress;

    /// <summary>
    /// True while the user's choice from the save-conflict banner is ending the session (see
    /// <see cref="ResolveSaveConflictAsync"/>). Read by <see cref="MainWindow.OnClosing"/>, which refuses every
    /// close request while it is true.
    /// </summary>
    public bool IsSaveConflictResolutionInProgress => _saveConflictInProgress;

    /// <summary>
    /// The teardown that <see cref="MainWindow.OnClosing"/> runs on the first close request (after its refusal
    /// checks), moved here whole: stop the periodic backup, then either end the stale session after a restore
    /// (<see cref="EndSessionAfterRestoreAsync"/>) or save D' to D and release the lock, close the secondary
    /// windows, dispose the services and clean up the working copy (keeping it, and saying so, when the final
    /// save failed); then give back the single-instance guard, start the fresh copy of TermPoint after a
    /// confirmed restore, and log the shutdown checkpoint. The caller re-triggers the window close afterwards.
    ///
    /// <para>Never throws: the teardown's own failures are caught and logged so the window can always close.</para>
    /// </summary>
    /// <returns>A task that completes when the managed teardown has finished.</returns>
    public async Task TeardownForCloseAsync()
    {
        try
        {
            // Stop the periodic backup timer before disposing the container.
            _host.StopBackupSession();

            if (_endSessionAfterRestore)
            {
                // The database has just been replaced by a restore: end the stale session without saving.
                await EndSessionAfterRestoreAsync();
            }
            else
            {
                // Stop autosave and save D' → D (releases lock on success).
                var wasWriteMode = _checkout.Mode == CheckoutMode.WriteAccess;
                _checkout.StopAutoSave();
                var saveOutcome = await _checkout.ReleaseAsync(saveFirst: wasWriteMode);
                CrashPoints.Hit("Close.AfterRelease");

                // Close any detached panels / sticky notes BEFORE disposing the DI container,
                // so windows whose content binds to view models are torn down while those VMs
                // still exist. ShutdownMode.OnMainWindowClose would close them anyway, but doing
                // it here is deterministic and suppresses the detach-reattach side effect.
                _host.CloseSecondaryWindows();

                // Dispose the DI container, which closes the SQLite connection cleanly.
                _host.DisposeServices();
                CrashPoints.Hit("Close.AfterServicesDisposed");

                // Delete the working copy ONLY when its content is safely in D (save
                // succeeded) or nothing was ever edited (no dirty marker). Otherwise the
                // working copy and its marker are deliberately left behind: they are the
                // only copy of the user's unsaved changes, and the next launch will offer
                // to restore them. (ReleaseAsync has already parked the write lock
                // appropriately for each failure kind.)
                if (saveOutcome == SaveOutcome.Success || !_checkout.HasDirtyMarker)
                {
                    _checkout.CleanupWorkingCopy();
                }
                else if (wasWriteMode)
                {
                    await _prompts.ShowMessageAsync("Changes Not Saved",
                        "Your latest changes could not be saved to the database. " +
                        "They have been kept, and TermPoint will offer to restore them " +
                        "the next time you open this database.");
                }
                CrashPoints.Hit("Close.AfterWorkingCopyCleanup");
            }
        }
        catch (Exception ex)
        {
            // Log but still allow the window to close — don't leave the user
            // stuck with an uncloseable window.
            _logger().LogError(ex, "Error during shutdown — some changes may not have been saved");
        }

        // Give back the single-instance guard LAST: it protects the working copy that was just saved
        // or deliberately kept (a failed final save keeps D' and its dirty marker for the next start), so
        // a second TermPoint must not be able to start sorting through those files until we are done with
        // them. Not strictly needed (the OS drops the guard when the process ends) but explicit, and it
        // runs even when the teardown above threw. Never throws.
        _instanceGuard.ReleaseAll();

        // After a confirmed restore, start the fresh copy of TermPoint. Only now: the guard is released (a
        // copy started earlier would be refused as "already open"), the lock is given back and the working
        // copy is gone, so the new copy opens the restored database from a clean state.
        if (_relaunchAfterClose)
            _host.RelaunchApplication();

        // ── Shutdown checkpoint #1 ───────────────────────────────────────────
        // Managed teardown (save → release lock → dispose DI container → cleanup
        // working copy) has finished. Pairs with checkpoint #2 in Program.Main:
        // if BOTH lines are logged but TermPoint.exe lingers, the leftover is a
        // debugger/native artifact, not our code.
        _logger().LogInfo("[Shutdown] Managed teardown complete; closing main window.");
    }

    // ── Restore from backup (spec item 5, step 5b) ───────────────────────────

    /// <summary>
    /// Restores <paramref name="backupPath"/> over the open database, safely. Called (after the user has
    /// confirmed) from <see cref="MainWindowViewModel"/>, like <see cref="SwitchDatabaseAsync"/>, because it
    /// is window-lifecycle work: a wait dialog, a blocked close, and a close and restart at the end.
    ///
    /// <para><b>Sequence.</b></para>
    /// <list type="number">
    /// <item><description>Only the writer may restore; anyone else is told so and nothing happens.</description></item>
    /// <item><description>Autosave is stopped (an autosave tick must not collide with the restore's use of the
    /// save gate) and window closing is blocked (<see cref="_restoreInProgress"/>).</description></item>
    /// <item><description>Under a wait dialog that blocks edits: the user's changes are SAVED FIRST; if that save
    /// fails the restore is never started. Then <see cref="CheckoutService.RestoreFromBackupAsync"/> runs the
    /// shared safe restore (<see cref="DatabaseRestore"/>), which keeps the replaced database beside it.</description></item>
    /// <item><description>Every outcome that leaves the database unchanged is reported and the session simply
    /// continues (autosave resumes). <see cref="RestoreOutcome.Restored"/> and
    /// <see cref="RestoreOutcome.RenameUnconfirmed"/> are reported and then the window is closed: the normal
    /// close path (<see cref="MainWindow.OnClosing"/>) ends the stale session without saving, and after a confirmed restore
    /// restarts TermPoint.</description></item>
    /// </list>
    ///
    /// <para>Nothing is ever lost: the changes were saved before the restore began, so they are in the
    /// pre-restore copy; a failed save means the restore did not start.</para>
    /// </summary>
    /// <param name="backupPath">Full path of the backup .db file to restore (may be on a share).</param>
    public async Task RestoreFromBackupAsync(string backupPath)
    {
        // Only the writer may restore: it replaces the database for everyone, and only the lock holder has a
        // session that can be saved first and ended cleanly.
        if (_checkout.Mode != CheckoutMode.WriteAccess)
        {
            await ShowRestoreMessageAsync(new RestoreResult(RestoreOutcome.NotWriter));
            return;
        }

        if (_restoreInProgress)
        {
            _logger().LogInfo("MainWindow: RestoreFromBackupAsync ignored — a restore is already in progress.");
            return;
        }

        // Captured now, before anything is torn down: OnClosing needs it to discard this session's crash marker.
        var sourcePath = _checkout.SourcePath;

        _restoreInProgress = true;
        _checkout.StopAutoSave();

        // Null means "the save failed, so the restore was never started".
        RestoreResult? result;
        try
        {
            result = await _prompts.RunWithWaitAsync(
                RestorePrompt.WaitTitle,
                RestorePrompt.WaitMessage,
                () => SaveThenRestoreAsync(backupPath));
        }
        catch (Exception ex)
        {
            // SaveAsync and RestoreFromBackupAsync never throw, so this is a safety net only.
            _logger().LogError(ex, "MainWindow: unexpected exception while restoring from a backup");
            result = new RestoreResult(RestoreOutcome.Failed, Error: ex);
        }
        finally
        {
            _restoreInProgress = false;
        }

        // The save failed: the restore was not started. (SaveAsync has already shown its own save banner.)
        if (result is null)
        {
            await _prompts.ShowMessageAsync(RestorePrompt.NotStartedTitle, RestorePrompt.SaveFailedMessage);
            ResumeAutoSave();
            return;
        }

        // Every other outcome that leaves D alone: say what happened; the session simply continues.
        if (result.TargetUnchanged)
        {
            await ShowRestoreMessageAsync(result);
            ResumeAutoSave();
            return;
        }

        // Restored or RenameUnconfirmed: the session is stale. Arrange the end of the session FIRST, so that
        // even if the window is closed by other means while the message is up (the OS shutting down, say) the
        // normal close path still ends it correctly instead of trying to save the stale working copy.
        _endSessionAfterRestore = true;
        _restoredSourcePath     = sourcePath;
        _relaunchAfterClose     = result.Outcome == RestoreOutcome.Restored;

        // Stop the periodic backup timer: a backup of the now-stale working copy must never appear as the
        // newest backup.
        try
        {
            _host.StopBackupSession();
        }
        catch (Exception ex)
        {
            _logger().LogError(ex, "MainWindow: could not stop the periodic backup after a restore");
        }

        try
        {
            await ShowRestoreMessageAsync(result);
        }
        finally
        {
            _host.CloseWindow(); // OnClosing ends the stale session and, after a confirmed restore, restarts TermPoint.
        }
    }

    /// <summary>
    /// The work behind the wait dialog: saves the user's current changes, and only if that succeeded runs the
    /// restore. Neither call throws.
    /// </summary>
    /// <param name="backupPath">The backup to restore.</param>
    /// <returns>The restore's result, or null when the save did not succeed (so the restore was not started).</returns>
    private async Task<RestoreResult?> SaveThenRestoreAsync(string backupPath)
    {
        // Autosave was stopped before this, but stopping the timer doesn't stop a tick that is already
        // saving. Wait for it (bounded), or our save would hit the in-flight gate and be skipped as
        // "already in progress" — reported to the user as a failed save. Same as ReleaseAsync's exit save.
        await _checkout.WaitForInFlightSaveAsync();

        var save = await _checkout.SaveAsync();
        if (save != SaveOutcome.Success)
        {
            _logger().LogInfo($"MainWindow: restore not started — the save before it ended with {save}.");
            return null;
        }

        return await _checkout.RestoreFromBackupAsync(backupPath);
    }

    /// <summary>
    /// Shows the message for a restore result (<see cref="RestorePrompt.Describe"/>) in the standard message dialog.
    /// </summary>
    /// <param name="result">The result to describe.</param>
    private Task ShowRestoreMessageAsync(RestoreResult result)
    {
        var (title, message) = RestorePrompt.Describe(result);
        return _prompts.ShowMessageAsync(title, message);
    }

    /// <summary>
    /// Restarts autosave after a restore that left the database unchanged, if the user has it switched on and
    /// this session is still the writer. (The restore stopped it so no autosave tick could collide with it.)
    /// </summary>
    private void ResumeAutoSave()
    {
        if (_settings().AutoSaveEnabled && _checkout.Mode == CheckoutMode.WriteAccess)
            _checkout.StartAutoSave();
    }

    /// <summary>
    /// Ends the session of a database that a restore has just replaced. Called by <see cref="TeardownForCloseAsync"/> when
    /// <see cref="_endSessionAfterRestore"/> is set. Unlike the normal exit it does NOT save (the working copy is
    /// stale and a save would be refused): it releases the write lock, closes the secondary windows, disposes
    /// the services, and then ALWAYS deletes the working copy and its crash marker — the restored D supersedes
    /// them, and all the user's changes were saved before the restore started. Deleting the marker also keeps
    /// the next start from mistaking the leftovers for a crash.
    /// </summary>
    private async Task EndSessionAfterRestoreAsync()
    {
        try
        {
            _checkout.StopAutoSave();
            await _checkout.ReleaseAsync(saveFirst: false);

            _host.CloseSecondaryWindows();

            // Closes the SQLite connection; only then can the working copy be deleted.
            _host.DisposeServices();
        }
        finally
        {
            _checkout.CleanupWorkingCopy();
            if (!string.IsNullOrEmpty(_restoredSourcePath))
                _checkout.DiscardCrash(_restoredSourcePath);
        }
    }

    // ── Save conflict (spec item 10, step 10a) ───────────────────────────────

    /// <summary>
    /// Carries out the user's choice from the save-conflict banner (<see cref="CheckoutService.HasSaveConflict"/>):
    /// <b>Save a Copy…</b> keeps their version as a separate file, <b>Discard Changes</b> gives it up; either way the
    /// session ends and the current database is reopened. Called from <see cref="MainWindowViewModel"/>, like
    /// <see cref="SwitchDatabaseAsync"/>, because it is window-lifecycle work.
    ///
    /// <para><b>Sequence (the order matters):</b></para>
    /// <list type="number">
    /// <item><description>Ignored unless a conflict is pending and no resolution is already running.</description></item>
    /// <item><description>Refused while an inline editor is open (<see cref="MainWindowViewModel.IsAnyPanelEditing"/>):
    /// an edit that has not been applied is not in the database at all. Nothing is changed.</description></item>
    /// <item><description>Save a Copy…: the save picker (16a's title and suggested name, no overwrite prompt), then
    /// the EARLY refusal (<see cref="NewFilePlacement.RefuseIfPresentAsync"/>): a file that exists, or a location
    /// that does not answer, gets its message and returns to the banner with nothing changed. Discard Changes: a
    /// confirmation; Cancel returns to the banner.</description></item>
    /// <item><description>Closing is blocked (<see cref="_saveConflictInProgress"/>) and the session is ended by the
    /// normal database switch to the SAME database, with a callback (<see cref="ResolveKeptChangesAsync"/>) that
    /// runs once the services — and with them the connection to the kept changes — are closed. See
    /// <see cref="SwitchCoreAsync"/> for why nothing is saved and nothing is deleted on that path.</description></item>
    /// </list>
    ///
    /// <para>A failed reopen follows the normal failed-switch path (the database chooser); the user's version is
    /// already saved, or still kept for crash recovery to offer again.</para>
    /// </summary>
    /// <param name="saveCopy">True for Save a Copy…, false for Discard Changes.</param>
    public async Task ResolveSaveConflictAsync(bool saveCopy)
    {
        if (!_checkout.HasSaveConflict || _saveConflictInProgress)
        {
            _logger().LogInfo("MainWindow: ResolveSaveConflictAsync ignored — no conflict is pending, or one is already being resolved.");
            return;
        }

        // An unapplied inline edit is not in the database at all (item 14 will handle it later): refuse first.
        if (_host.IsInlineEditorOpen)
        {
            _logger().LogBreadcrumb("SaveConflict: refused — an editor is open");
            await _prompts.ShowMessageAsync(SaveConflictPrompt.EditorOpenTitle, SaveConflictPrompt.EditorOpenMessage);
            return;
        }

        var sourcePath = _checkout.SourcePath;

        // Everything the user can still back out of happens BEFORE the session is touched.
        string? destPath = null;
        if (saveCopy)
        {
            _logger().LogBreadcrumb("SaveConflict: Save a Copy chosen");
            destPath = await PickRecoveredCopyPathAsync(sourcePath);
            if (destPath is null) return; // cancelled

            // Early refusal: a name that is taken, or a location that doesn't answer, returns to the banner.
            var early = await NewFilePlacement.RefuseIfPresentAsync(destPath);
            if (early is not null)
            {
                _logger().LogBreadcrumb("SaveConflict: destination refused", new() { ["outcome"] = early.Outcome.ToString() });
                await ShowExportOutcomeAsync(early);
                return;
            }
        }
        else
        {
            _logger().LogBreadcrumb("SaveConflict: Discard Changes chosen");
            var confirmed = await _prompts.ShowChoiceAsync(
                SaveConflictPrompt.DiscardConfirmTitle, SaveConflictPrompt.DiscardConfirmMessage,
                SaveConflictPrompt.DiscardConfirmButton, SaveConflictPrompt.DiscardCancelButton);
            if (!confirmed)
            {
                _logger().LogBreadcrumb("SaveConflict: Discard cancelled");
                return;
            }
        }

        _saveConflictInProgress = true;
        try
        {
            // The normal switch to the SAME database ends this session (never saving it) and reopens D.
            await SwitchDatabaseAsync(
                sourcePath,
                resolveKeptChanges: keptSourcePath => ResolveKeptChangesAsync(keptSourcePath, destPath));
        }
        finally
        {
            _saveConflictInProgress = false;
        }
    }

    /// <summary>
    /// Shows the save picker in which the user chooses where to save their version as a separate file: the title
    /// and suggested name of crash recovery's export (<see cref="RecoveryConflictPrompt"/>), and no overwrite
    /// prompt (an existing file is never replaced; the caller refuses it).
    /// </summary>
    /// <param name="sourcePath">The shared database D (for the suggested file name).</param>
    /// <returns>The chosen full path, or null when the user cancelled (or the choice has no local path).</returns>
    private async Task<string?> PickRecoveredCopyPathAsync(string sourcePath)
    {
        return await _prompts.PickSaveFileAsync(
            RecoveryConflictPrompt.SaveDialogTitle,
            RecoveryConflictPrompt.SuggestedFileName(sourcePath),
            "db");
    }

    /// <summary>
    /// The callback <see cref="ResolveSaveConflictAsync"/> hands to <see cref="SwitchCoreAsync"/>: runs once the
    /// session has ended and its services — and with them the connection to the kept changes — are closed, so
    /// nothing in any window can write them any more. It either keeps the user's version as a separate file or
    /// discards it.
    ///
    /// <para><b>The kept changes are deleted only after a confirmed copy or a confirmed Discard.</b> With
    /// <paramref name="destPath"/>, the export runs under a wait dialog; only <see cref="NewDatabaseOutcome.Created"/>
    /// deletes them (<see cref="CheckoutService.DiscardCrash"/>) and tells the user. Any other outcome shows its
    /// message and KEEPS them: the reopen's crash recovery then offers them again. Nothing is deleted in a
    /// <c>catch</c>: an unexpected exception propagates (the switch's failure path keeps a marked working copy).</para>
    /// </summary>
    /// <param name="sourcePath">The shared database D whose kept changes to save elsewhere or discard.</param>
    /// <param name="destPath">The file to save the user's version as, or null to discard it.</param>
    private async Task ResolveKeptChangesAsync(string sourcePath, string? destPath)
    {
        try
        {
            _logger().LogBreadcrumb("SaveConflict: session ended");

            if (destPath is null)
            {
                _checkout.DiscardCrash(sourcePath);
                _logger().LogInfo("MainWindow: save conflict — the user's changes were discarded.");
                _logger().LogBreadcrumb("SaveConflict: discarded");
                return;
            }

            var result = await _prompts.RunWithWaitAsync(
                SaveConflictPrompt.SavingTitle,
                SaveConflictPrompt.SavingMessage,
                () => _checkout.ExportRecoveredCopyAsync(sourcePath, destPath));
            _logger().LogBreadcrumb("SaveConflict: export finished", new() { ["outcome"] = result.Outcome.ToString() });

            if (result.Outcome == NewDatabaseOutcome.Created)
            {
                // The copy exists, so the kept changes are no longer the only copy.
                _checkout.DiscardCrash(sourcePath);
                _logger().LogInfo("MainWindow: save conflict — the user's version was saved as a separate file; the kept changes were deleted.");
                await _prompts.ShowMessageAsync(SaveConflictPrompt.SavedTitle,
                    SaveConflictPrompt.SavedMessage(Path.GetFileName(destPath)));
            }
            else
            {
                // Keep the kept changes: crash recovery offers them again when the database reopens.
                _logger().LogInfo("MainWindow: save conflict — the copy was not saved; the changes are kept.");
                await ShowExportOutcomeAsync(result);
            }
        }
        finally
        {
            // The kept changes are saved elsewhere, discarded, or deliberately kept: closing is safe again.
            _saveConflictInProgress = false;
        }
    }

    // ── Lost write lock ──────────────────────────────────────────────────────

    /// <summary>
    /// The body of the handler for <see cref="CheckoutService.WriteLockLost"/> — fired when a mid-session
    /// verification discovered that the write lock is no longer ours (taken over,
    /// deleted, or its <c>SessionGuid</c> was modified). Runs on the UI thread (the window's handler hops there
    /// first).
    ///
    /// <para>Builds a cause-specific banner from <paramref name="reason"/> and
    /// <see cref="CheckoutService.LockLossNewHolder"/>, then demotes the session to
    /// read-only <b>in place</b> without swapping the DataContext. The banner is set
    /// <b>before</b> demotion so that even a failed demotion leaves the user informed.</para>
    ///
    /// <para>The user is directed to restart the app to re-bid for write access;
    /// we deliberately do not auto-reacquire, because doing so would silently trample
    /// whoever now holds the lock.</para>
    ///
    /// <para>The window's view model and data connection are captured ONCE
    /// (<see cref="ISessionHost.CaptureLockLossSurface"/>) and those same two objects are used throughout.</para>
    /// </summary>
    /// <param name="reason">Why the write lock was lost.</param>
    /// <returns>A task that completes when the demotion (or its failure handling) has finished. Never throws: every failure is caught and logged.</returns>
    public async Task HandleWriteLockLostAsync(WriteLockLostReason reason)
    {
        try
        {
            var banner = BuildLockLossBanner(reason);

            var surface = _host.CaptureLockLossSurface();

            // Set the banner BEFORE demotion so it is visible even if demotion throws.
            surface.ShowSaveError(banner);

            async Task BeforeClose()
            {
                await Task.CompletedTask;
                surface.CloseConnection();
            }

            async Task AfterOpen()
            {
                await Task.CompletedTask;
                if (surface.HasConnection && !string.IsNullOrEmpty(_checkout.WorkingPath))
                {
                    try
                    {
                        surface.ReinitializeConnection(_checkout.WorkingPath);
                    }
                    catch (System.Exception ex)
                    {
                        _logger().LogError(ex, "OnWriteLockLost: ReinitializeConnection failed — database is unavailable until reopened");
                        surface.ShowSaveError(
                            "The database connection could not be restored after losing write access. " +
                            "Please close and reopen the database file.");
                    }
                }
            }

            var ok = await _checkout.DemoteToReadOnlyAsync(BeforeClose, AfterOpen);

            if (ok && surface.HasViewModel)
            {
                try { surface.ReloadSections(); }
                catch (System.Exception ex)
                {
                    _logger().LogError(ex, "OnWriteLockLost: post-demote reload failed");
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger().LogError(ex, "OnWriteLockLost: demotion failed");
        }
    }

    /// <summary>
    /// Builds a user-facing banner string explaining why write access was lost,
    /// using the <paramref name="reason"/> enum and the identity of the new holder
    /// (if any) from <see cref="CheckoutService.LockLossNewHolder"/>.
    /// </summary>
    private string BuildLockLossBanner(WriteLockLostReason reason)
    {
        var sb = new System.Text.StringBuilder();

        if (reason == WriteLockLostReason.TakenOver)
        {
            var holder = _checkout.LockLossNewHolder;
            if (holder is not null && !string.IsNullOrWhiteSpace(holder.Username))
                sb.Append($"Write access was taken over by {holder.Username} on {holder.Machine}.");
            else
                sb.Append("Write access was taken over by another user.");
        }
        else
        {
            sb.Append(
                "The database lock file was removed by another program " +
                "(possibly antivirus or cloud sync software).");
        }

        // Built BEFORE demotion runs, so HasDirtyMarker still reflects the write
        // session: DemoteToReadOnlyAsync preserves D' + marker exactly when it is true.
        sb.Append(_checkout.HasDirtyMarker
            ? " Your unsaved changes have been kept — TermPoint will offer to restore " +
              "them the next time you open this database with write access."
            : " There were no unsaved changes at the time.");
        sb.Append(" You can regain write access by exiting and restarting TermPoint.");

        return sb.ToString();
    }
}
