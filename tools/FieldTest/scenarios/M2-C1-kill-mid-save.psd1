@{
    Name = 'M2-C1-kill-mid-save'
    Description = 'The writer is hard-killed mid-save and relaunched: the lock is reclaimed from the dead session, the kept changes are restored, a save then completes. Pass = reclaim line, then a successful save, no "not restored" report.'
    Roles = @('Writer')
    Expect = @(
        # Verified: WriteLockService.cs ~1079 "Reclaimed lock from dead session (PID n, user@machine)."
        @{ Role = 'Writer'; Pattern = 'Reclaimed lock from dead session'; Note = 'relaunch reclaims the lock at once' }
        # TODO: no log line found for the "Restore Unsaved Changes?" offer or for the Restore click (only a Bugsnag
        # breadcrumb "Recovery: WriteAccess (resumed crashed session)", CheckoutService.cs ~1192, which is not in the file log).
        # Verified: CheckoutService.cs ~2015.
        @{ Role = 'Writer'; Pattern = 'CheckoutService: Save completed successfully'; Note = 'a save after the restore lands' }
    )
    Forbid = @(
        # Verified: HandledEventException.cs ~83-85 (written by HandledEventReporter as a [REPORT] entry).
        @{ Role = 'Writer'; Pattern = 'Kept changes not restored'; Note = 'the kept changes were restorable (D unchanged)' }
    )
    AllowErrors = $false
}
