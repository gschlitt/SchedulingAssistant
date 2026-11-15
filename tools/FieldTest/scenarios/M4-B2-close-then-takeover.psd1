@{
    Name = 'M4-B2-close-then-takeover'
    Description = 'The writer closes normally; the reader notices the lock file is gone within about a minute, takes over and its save completes. Pass = release line, poll line, reader save, nobody demoted.'
    Roles = @('Writer', 'Reader')
    Expect = @(
        # Verified: WriteLockService.cs ~641 "[WriteLockService] Released write lock: <path>".
        @{ Role = 'Writer'; Pattern = 'Released write lock'; Note = 'the writer closes normally' }
        # Verified: WriteLockService.cs ~1868.
        @{ Role = 'Reader'; Pattern = 'Poll: lock file gone \u2014 write access available'; Note = 'reader notices within ~60 s' }
        # Verified: CheckoutService.cs ~2015.
        @{ Role = 'Reader'; Pattern = 'CheckoutService: Save completed successfully'; Note = 'the reader, now writer, saves' }
    )
    Forbid = @(
        @{ Role = '*'; Pattern = 'demot|genuinely lost|no longer ours'; Note = 'nobody is moved to read-only' }
    )
    AllowErrors = $false
}
