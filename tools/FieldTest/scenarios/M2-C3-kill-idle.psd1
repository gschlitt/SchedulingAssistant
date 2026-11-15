@{
    Name = 'M2-C3-kill-idle'
    Description = 'The writer is hard-killed while idle (just after a save) and relaunched: the lock is reclaimed and there is no restore offer. Pass = reclaim line, nothing reported as unrestorable.'
    Roles = @('Writer')
    Expect = @(
        # Verified: WriteLockService.cs ~1079.
        @{ Role = 'Writer'; Pattern = 'Reclaimed lock from dead session'; Note = 'relaunch reclaims the lock at once' }
    )
    Forbid = @(
        # TODO: no log line found for the restore offer itself ("Restore Unsaved Changes?", SessionLifecycle.cs ~1229 shows
        # a dialog without logging). The user must confirm by eye that no prompt appeared. Nearest logged signal below.
        # Verified: HandledEventException.cs ~83-85.
        @{ Role = 'Writer'; Pattern = 'Kept changes not restored'; Note = 'no kept-changes conflict was reported' }
    )
    AllowErrors = $false
}
