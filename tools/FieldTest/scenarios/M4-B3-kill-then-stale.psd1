@{
    Name = 'M4-B3-kill-then-stale'
    Description = 'The writer is hard-killed with the lock left behind; the reader sees the heartbeat stand still (~3 min), is offered the takeover, takes it and saves. Pass = reader poll line then a successful save.'
    Roles = @('Writer', 'Reader')
    Expect = @(
        # Verified: WriteLockService.cs ~1931 "Poll: lock heartbeat unchanged for {n}s \u2014 write access available."
        @{ Role = 'Reader'; Pattern = 'Poll: lock heartbeat unchanged for \d+s \u2014 write access available'; Note = 'reader sees the heartbeat stand still' }
        # Verified: the banner's Take over runs SwitchDatabaseAsync -> RunCheckoutAsync -> StaleHolder prompt ->
        # ForceCheckoutAsync -> WriteLockService.ForceAcquire, which logs this (~826).
        @{ Role = 'Reader'; Pattern = 'ForceAcquire: lock acquired after breaking stale lock'; Note = 'reader takes over' }
        # Verified: CheckoutService.cs ~2015.
        @{ Role = 'Reader'; Pattern = 'CheckoutService: Save completed successfully'; Note = 'reader takes over and saves' }
    )
    Forbid = @(
        # Verified: WriteLockService.cs ~798. Would mean the "dead" writer turned out to be alive.
        @{ Role = 'Reader'; Pattern = 'ForceAcquire: lock changed since it was judged stale'; Note = 'the takeover did not find the holder alive again' }
    )
    AllowErrors = $false
    # TODO: no log line found for the writer's relaunch outcome ("Restore Unsaved Changes?" then "Unsaved Changes Kept"): dialogs only.
}
