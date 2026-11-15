<#
.SYNOPSIS
    Probe for the candidate finding of 2026-11-07: after a VM freezes right after a save, TermPoint's wake check
    answered "lock confirmed" although the lock had been taken over (designs/testing-readiness-1.2.4.md, Log).
.DESCRIPTION
    One run:
      1. Waits (inside the VM) for TermPoint's log to say "Save completed successfully" (you click Save in the VM).
      2. Freezes the VM at once (Suspend-VM) and prints how long after the save that took.
      3. While it is frozen, "Alice" takes the lock (Set-TestLock -Kind Takeover), like a colleague's takeover.
      4. Resumes the VM after -FreezeSeconds, samples the lock file every 5 s for -WatchSeconds, and flags any sample
         in which the VM's session holds the lock again (TermPoint rewrote someone else's lock: invariant I9, rule 2).
      5. Prints TermPoint's log lines from the VM after the wake (wake check, heartbeat, lock lines).
    -Mode Process freezes only TermPoint (Suspend-TermPoint's NtSuspendProcess) instead of the whole VM: Windows keeps
    running, so the SMB client itself can answer the server while TermPoint is frozen. Comparing the two modes tells
    whether the stale answer comes from the operating system being frozen.
    Verdict per run: STALE CONFIRM (the wake check said "lock confirmed"), RELOCKED (the VM took the lock back), or
    CORRECT (unreachable / NotOurs, lock stayed Alice's).
    The product is not changed. The only share writes are Set-TestLock's lock-file write (as every takeover test does).
.PARAMETER Machine
    The VM that runs TermPoint as the writer (default TPVM1).
.PARAMETER Mode
    VM (default): Suspend-VM / Resume-VM. Process: freeze only the TermPoint process.
.PARAMETER FreezeSeconds
    How long the VM stays frozen (default 240; the first sighting froze for 370).
.PARAMETER WatchSeconds
    How long to sample the lock after the resume (default 90).
.EXAMPLE
    cd <repo>\tools\FieldTest; Import-Module .\FieldTest.psm1 -Force
    Reset-TestDatabase; $w = Start-TermPoint -Machine TPVM1      # add a section in TPVM1, Apply, don't save yet
    .\probes\Probe-SaveThenFreeze.ps1                            # then click Save in TPVM1 when it says so
.EXAMPLE
    .\probes\Probe-SaveThenFreeze.ps1 -Mode Process
.NOTES
    Needs the FieldTest module imported in an elevated console, and TermPoint running as the writer on the VM.
    Afterwards TermPoint on the VM is read-only (Alice holds the lock): close it, then Reset-TestDatabase -Force.
#>
[CmdletBinding()]
param(
    [string]$Machine = 'TPVM1',
    [ValidateSet('VM', 'Process')][string]$Mode = 'VM',
    [int]$FreezeSeconds = 240,
    [int]$WatchSeconds = 90
)
$ErrorActionPreference = 'Stop'
$mod = Get-Module FieldTest
if (-not $mod) { throw 'Import the FieldTest module first (Import-Module .\FieldTest.psm1 -Force).' }

$cfg = Get-TestConfig
$lockPath = [IO.Path]::ChangeExtension($cfg.Database, '.lock')
$vmName = (& $mod { param($n) (Get-TestMachine -Name $n).VMName } $Machine)
$tp = @(Get-TermPointProcess -Machine $Machine)
if ($tp.Count -ne 1) { throw "Expected one TermPoint on $Machine, found $($tp.Count)." }
$tpId = [int]$tp[0].Id
# Process mode: compile the suspend helper in the cached session now, so the freeze itself is one quick call.
$ntApply = { param($id, $resume) $s = [FieldTestNt]::Apply($id, $resume); if ($s -ne 0) { throw ("NtSuspend/ResumeProcess returned 0x{0:X8}" -f $s) } }
if ($Mode -eq 'Process') {
    & $mod { param($n) Invoke-TestComputer -Name $n -ArgumentList @($script:NtSource) -ScriptBlock {
        param($src) if (-not ('FieldTestNt' -as [type])) { Add-Type -TypeDefinition $src } } } $Machine
}

# Who holds the lock now: it must be the VM, or the probe means nothing.
$before = & $mod { param($p) Read-TestLockFile -LockPath $p } $lockPath
if ($before.Machine -ne $Machine) { throw "The lock is held by '$($before.Machine)', not $Machine. Start TermPoint there as the writer first." }
$vmSession = $before.SessionGuid
Write-Host ("Lock held by {0} on {1}, session {2}." -f $before.Username, $before.Machine, $vmSession)

# 1. Wait inside the VM for the next "Save completed successfully" line in TermPoint's newest log.
Write-Host ''
Write-Host ">>> Click Save in TermPoint on $Machine now (waiting up to 5 minutes)." -ForegroundColor Yellow
$waitInVm = {
    param($timeoutSeconds)
    $log = Get-ChildItem "$env:LOCALAPPDATA\Packages\*TermPoint*\LocalCache\Roaming\TermPoint\Logs\app-*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $log) { return 'NO LOG' }
    $fs = [IO.File]::Open($log.FullName, 'Open', 'Read', [IO.FileShare]'ReadWrite, Delete')
    try {
        $fs.Seek(0, 'End') | Out-Null
        $sr = New-Object IO.StreamReader($fs)
        $buffer = ''
        $deadline = (Get-Date).AddSeconds($timeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $chunk = $sr.ReadToEnd()
            if ($chunk) {
                $buffer += $chunk
                if ($buffer -match 'Save completed successfully') { return 'SAVED' }
            }
            Start-Sleep -Milliseconds 50
        }
        return 'TIMEOUT'
    } finally { $fs.Dispose() }
}
$seen = Invoke-TestComputer -Name $Machine -ScriptBlock $waitInVm -ArgumentList 300
if ($seen -ne 'SAVED') { throw "No save seen on $Machine ($seen)." }

# 2. Freeze at once.
$sw = [Diagnostics.Stopwatch]::StartNew()
if ($Mode -eq 'VM') { Suspend-VM -Name $vmName; $what = $Machine }
else { Invoke-TestComputer -Name $Machine -ScriptBlock $ntApply -ArgumentList $tpId, $false; $what = "TermPoint PID $tpId on $Machine" }
$frozeAt = Get-Date
Write-Host ("{0:HH:mm:ss.fff}  Save seen; {1} frozen {2} ms later (plus the log-to-console hop)." -f $frozeAt, $what, $sw.ElapsedMilliseconds)

# 3. Alice takes over while the VM is frozen.
Start-Sleep -Seconds 5
Set-TestLock -Kind Takeover
$alice = & $mod { param($p) Read-TestLockFile -LockPath $p } $lockPath
Write-Host ("{0:HH:mm:ss}  Lock now: {1} on {2}, session {3}." -f (Get-Date), $alice.Username, $alice.Machine, $alice.SessionGuid)

# 4. Resume after the freeze, then watch the lock.
$left = $FreezeSeconds - [int]((Get-Date) - $frozeAt).TotalSeconds
if ($left -gt 0) { Write-Host "Frozen; resuming in $left s."; Start-Sleep -Seconds $left }
if ($Mode -eq 'VM') {
    Resume-VM -Name $vmName
    $resumedAt = Get-Date
    & $mod { param($n) Remove-CachedSession -Name $n } $Machine
}
else {
    Invoke-TestComputer -Name $Machine -ScriptBlock $ntApply -ArgumentList $tpId, $true
    $resumedAt = Get-Date
}
Write-Host ("{0:HH:mm:ss}  {1} resumed. Sampling the lock every 5 s for {2} s:" -f $resumedAt, $Machine, $WatchSeconds)
$relocked = $false
$end = $resumedAt.AddSeconds($WatchSeconds)
while ((Get-Date) -lt $end) {
    $l = & $mod { param($p) Read-TestLockFile -LockPath $p } $lockPath
    $mark = ''
    if ($l.SessionGuid -eq $vmSession) { $relocked = $true; $mark = '   <<< THE VM HOLDS THE LOCK AGAIN' }
    if ($l.Exists) { Write-Host ("  {0:HH:mm:ss}  {1} on {2}, heartbeat {3:HH:mm:ss}{4}" -f (Get-Date), $l.Username, $l.Machine, $l.Heartbeat, $mark) }
    else { Write-Host ("  {0:HH:mm:ss}  no lock file {1}" -f (Get-Date), $l.Error) }
    Start-Sleep -Seconds 5
}

# 5. TermPoint's log lines after the wake.
$tail = {
    param($sinceText)
    $log = Get-ChildItem "$env:LOCALAPPDATA\Packages\*TermPoint*\LocalCache\Roaming\TermPoint\Logs\app-*.log" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $since = [datetime]::Parse($sinceText, [Globalization.CultureInfo]::InvariantCulture)
    $lines = [IO.File]::ReadAllLines($log.FullName)
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        if ($lines[$i] -match '^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]') {
            $t = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture)
            if ($t -ge $since -and $lines[$i + 1] -match 'Wake|wake check|Heartbeat|heartbeat|lock|Lock|timed out|demot') {
                '{0:HH:mm:ss.fff}  {1}' -f $t, $lines[$i + 1].Trim()
            }
        }
    }
}
$wakeLines = @(Invoke-TestComputer -Name $Machine -ScriptBlock $tail -ArgumentList $frozeAt.ToString('yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture))
Write-Host ''
Write-Host "TermPoint on $Machine after the freeze:"
$wakeLines | ForEach-Object { Write-Host "  $_" }

# "wake check <em dash> lock confirmed": matched with '.' so this file stays ASCII (Windows PowerShell 5.1 reads a
# BOM-less file as ANSI).
$staleConfirm = @($wakeLines | Where-Object { $_ -match 'wake check . lock confirmed' }).Count -gt 0
Write-Host ''
if ($relocked) { Write-Host 'RELOCKED: the VM took the lock back from Alice.' -ForegroundColor Red }
if ($staleConfirm) { Write-Host 'STALE CONFIRM: the wake check said "lock confirmed" after Alice took over.' -ForegroundColor Red }
if (-not $relocked -and -not $staleConfirm) { Write-Host 'CORRECT: no stale confirm, and the lock stayed Alice''s.' -ForegroundColor Green }
"{0:yyyy-MM-dd HH:mm:ss}  {1}  mode={6}  froze {2:HH:mm:ss.fff}  frozen {3}s  staleConfirm={4}  relocked={5}" -f (Get-Date), $Machine, $frozeAt, $FreezeSeconds, $staleConfirm, $relocked, $Mode |
    Add-Content -LiteralPath (Join-Path $cfg.WorkFolder 'probe-save-then-freeze.txt') -Encoding ASCII
