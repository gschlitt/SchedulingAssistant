# Scenario files are read by Test-TermPointLog. Patterns are case-insensitive regexes matched against the log
# entry text. Em dashes in the app's log text are written as \u2014 here (this file is ASCII only).
@{
    Name = 'M1-B-save-stall'
    Description = 'The network is cut during a save: the save stalls and gives up, the writer stays writer, and a later save lands. Pass = a stall line, then a successful save, no demotion, no errors.'
    Roles = @('Writer')
    Expect = @(
        # Verified: NetworkFileOps.cs LogStall (~489) "{label} made no progress for 5000ms"; CheckoutService.cs ~1828,
        # ~1882, ~1976, ~2125, ~2174, ~2198 ("... stalled", "rename timed out", "source hash timed out", ...).
        # Not plain 'timed out': the heartbeat's own File.ReadAllText/File.Move timeouts would match that too.
        @{ Role = 'Writer'; Pattern = '(CopyFile|ComputeHash) \([a-z]+\) made no progress|CheckoutService: .*(stalled|rename timed out|source hash timed out|lock verification timed out|source stat timed out)'; Note = 'the cut stalls a save step' }
        # Verified: CheckoutService.cs ~2015.
        @{ Role = 'Writer'; Pattern = 'CheckoutService: Save completed successfully'; Note = 'a save after the cut lands' }
    )
    Forbid = @(
        # Verified: CheckoutService.cs ~3687 (wake check), ~3874 (heartbeat verify), ~3960/3989 (DemoteToReadOnly).
        @{ Role = '*'; Pattern = 'demot|genuinely lost|no longer ours'; Note = 'the writer is never moved to read-only' }
    )
    AllowErrors = $false
}
