@{
    Name = 'M5-sleep-takeover'
    Description = 'The writer (a frozen or paused VM) stops heartbeating; the reader judges its lock stale and takes over; when the writer wakes it logs the wake and learns it lost the lock. Pass = reader stale judgment and takeover, writer wake then lock-lost lines.'
    Roles = @('Writer', 'Reader')
    Expect = @(
        # Verified: WriteLockService.cs ~502 / ~1177 "Stale lock detected ..." (open-time prompt) and ~1931 (reader polling).
        @{ Role = 'Reader'; Pattern = 'Stale lock detected|Poll: lock heartbeat unchanged for \d+s \u2014 write access available'; Note = 'reader sees the frozen writer as inactive' }
        # Verified: WriteLockService.cs ~826.
        @{ Role = 'Reader'; Pattern = 'ForceAcquire: lock acquired after breaking stale lock'; Note = 'reader takes over' }
        # Verified: WriteLockService.cs ~1776 "Wake detected \u2014 gap was {n}s."
        @{ Role = 'Writer'; Pattern = 'Wake detected \u2014 gap was \d+s'; Note = 'the writer notices it was frozen' }
        # Verified: CheckoutService.cs ~3687 (wake check) and ~3874 (heartbeat verify).
        @{ Role = 'Writer'; Pattern = 'wake check \u2014 lock is no longer ours|heartbeat verify \u2014 lock genuinely lost'; Note = 'the writer learns it lost the lock' }
    )
    Forbid = @(
        # Verified: WriteLockService.cs ~798. The writer woke BEFORE the takeover: not this scenario.
        @{ Role = 'Reader'; Pattern = 'ForceAcquire: lock changed since it was judged stale'; Note = 'the takeover was not refused because the writer was active again' }
    )
    AllowErrors = $false
    # TODO: no log line found for the read-only banner itself or the kept-changes offer (dialogs/banners only).
    # The writer's "DemoteToReadOnly - preserving D' ..." line (CheckoutService.cs ~3989) appears only when it had unsaved changes.
}
