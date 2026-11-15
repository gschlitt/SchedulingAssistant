<#
.SYNOPSIS
    Field-test helper for scenario M1 (lives in tools\FieldTest; normally called through Show-SaveStall in the
    FieldTest module, which fetches the log first): reads the TermPoint log on the WRITER's computer and says which save
    stage stalled, when the save gave up, and whether anything alarming happened.

.DESCRIPTION
    Run it on the writer's computer right after a run, instead of reading the log by hand. It lists the lines that
    matter since -Since, one per line with the seconds after -HoleOpened (when you give it), and then a verdict:

      stage      which save step stalled: copy start/middle (A1), hash check (A2), end of copy or rename (A3), or an
                 earlier check that is not one of A1 to A3.
      gave up    when the save stopped trying. TermPoint shows its banner right after this, so "gave up" is when the
                 banner is due. About 10 to 15 s after the hole opened is normal (a 5 s stall deadline, then a 5 s
                 cleanup delete, sometimes a second stall); much longer is worth writing down.
      kept       lines saying the session stayed the writer.
      red flags  demotion, lost lock, an ERROR entry, an unobserved task fault. None is the pass.
      recovered  the first successful save after the last stall.

    The clocks of Mekler and the writer need to agree to within a second or the "+N s" figures are off by the
    difference. Check with:  w32tm /stripchart /computer:Mekler /samples:3 /dataonly

.PARAMETER Since
    Only entries at or after this time are considered (default: the last 10 minutes). Example: '12:40'.

.PARAMETER HoleOpened
    The time the black hole opened (the time the black-hole script printed), for the "+N s" column. Example: '12:41:03.250'.

.PARAMETER Log
    A log file to read. Default: today's TermPoint log, in %AppData% or, for an MSIX install, the package folder
    (whichever was written to last).

.EXAMPLE
    .\Show-SaveStall.ps1 -Since 12:40 -HoleOpened 12:41:03.250
    Show-SaveStall -Machine TPVM1 -Since 12:40 -HoleOpened 12:41:03.250   # via the module
#>
param(
    [datetime]$Since = (Get-Date).AddMinutes(-10),
    [datetime]$HoleOpened,
    [string]$Log = ''
)

$ErrorActionPreference = 'Stop'
$haveHole = $PSBoundParameters.ContainsKey('HoleOpened')

# Lines worth showing. Each: a pattern on the entry text, and what it means.
$interesting = @(
    'made no progress', 'timed out', 'stalled', 'abandoned', 'keeping writer state', 'share unreachable',
    'Heartbeat renewal failed', 'Save completed successfully', 'copy failed', 'demot', 'DemoteToReadOnly',
    'genuinely lost', 'no longer ours', 'Unobserved', 'already matches'
)
$interestingRegex = ($interesting | ForEach-Object { [Regex]::Escape($_) }) -join '|'

if (-not $Log) {
    $today = Get-Date -Format 'yyyy-MM-dd'
    $candidates = @(Join-Path $env:APPDATA "TermPoint\Logs\app-$today.log") +
                  @(Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'Packages\*TermPoint*\LocalCache\Roaming\TermPoint\Logs') `
                        -Filter "app-$today.log" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    $Log = $candidates | Where-Object { Test-Path -LiteralPath $_ } |
           Sort-Object { (Get-Item -LiteralPath $_).LastWriteTime } -Descending | Select-Object -First 1
    if (-not $Log) { throw "No TermPoint log found for $today." }
}
Write-Host "Reading $Log (since $($Since.ToString('HH:mm:ss')))"

# Parse: "[2026-10-22 12:26:26.269] [INFO] [v1.2.3.0]" then "  Context : text" (stack traces follow; only the first line is kept).
$entries = New-Object System.Collections.Generic.List[object]
$header  = '^\[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] \[(?<lvl>[A-Z]+)\]'
$current = $null
foreach ($line in (Get-Content -LiteralPath $Log -Encoding UTF8)) {
    if ($line -match $header) {
        $current = [pscustomobject]@{ Time = [datetime]::ParseExact($Matches.ts, 'yyyy-MM-dd HH:mm:ss.fff', $null); Level = $Matches.lvl; Text = '' }
        $entries.Add($current)
    }
    elseif ($null -ne $current -and $current.Text -eq '' -and $line -match '^\s+Context : (?<t>.*)$') {
        $current.Text = $Matches.t
    }
}
$mine = @($entries | Where-Object { $_.Time -ge $Since })
if ($mine.Count -eq 0) { Write-Host 'No log entries since then.'; return }

# Only SAVE stalls count in the verdict: the heartbeat's own "timed out" lines (File.ReadAllText, File.Move) are noise
# for the question "when did the save give up".
$saveStallRegex = '(CopyFile|ComputeHash) \([a-z]+\) made no progress|CheckoutService: .*(stalled|rename timed out|source hash timed out|lock verification timed out|source stat timed out)'

# One run only: from -Since to the first successful save after the first save stall (the recovery). Later runs in the
# same log are ignored, so -Since only has to be "a little before this run".
$firstStall = $mine | Where-Object { $_.Text -match $saveStallRegex } | Select-Object -First 1
if ($firstStall) {
    $recovery = $mine | Where-Object { $_.Time -gt $firstStall.Time -and $_.Text -match 'Save completed successfully' } | Select-Object -First 1
    if ($recovery) { $mine = @($mine | Where-Object { $_.Time -le $recovery.Time }) }
}

function Get-Offset($time) {
    if (-not $haveHole) { return '' }
    return ('+{0:F1} s' -f ($time - $HoleOpened).TotalSeconds)
}

Write-Host ''
Write-Host 'Lines that matter:'
$shown = @($mine | Where-Object { $_.Text -match $interestingRegex -or $_.Level -eq 'ERROR' })
foreach ($e in $shown) {
    '  {0}  {1,-10}  [{2}] {3}' -f $e.Time.ToString('HH:mm:ss.fff'), (Get-Offset $e.Time), $e.Level, $e.Text
}

# Verdict
$stalls = @($mine | Where-Object { $_.Text -match $saveStallRegex })
$stageOf = {
    param($t)
    if     ($t -match 'rename timed out but D already matches')       { 'A3 (rename: the save landed anyway, a pass)' }
    elseif ($t -match 'D\.tmp rename timed out')                      { 'A3 (rename)' }
    elseif ($t -match 'CopyFile \(close\) made no progress')          { 'A3-type (end of copy)' }
    elseif ($t -match 'CopyFile \((read|write)\) made no progress')   { 'A1 (copy in progress)' }
    elseif ($t -match 'D\.tmp verification hash stalled|ComputeHash')  { 'A2 (hash check)' }
    elseif ($t -match 'source hash timed out|lock verification timed out|source stat timed out') { 'before the copy (not one of A1 to A3)' }
    else   { $null }
}
Write-Host ''
Write-Host 'Verdict:'
$stages = @($stalls | ForEach-Object { & $stageOf $_.Text } | Where-Object { $_ } | Select-Object -Unique)
if ($stages.Count -eq 0) { Write-Host '  stage      NO stall line found: the hole opened after the save passed its network steps (or never opened).' }
else                     { Write-Host ("  stage      " + ($stages -join '; ')) }

$last = $stalls | Select-Object -Last 1
if ($last) {
    # The save gives up after its cleanup of the leftover .tmp, when that cleanup also timed out (within 8 s of the stall).
    $cleanup = $mine | Where-Object { $_.Text -match 'File\.Delete timed out' -and $_.Time -gt $last.Time -and ($_.Time - $last.Time).TotalSeconds -le 8 } |
               Select-Object -First 1
    $end = if ($cleanup) { $cleanup } else { $last }
    Write-Host ("  gave up    {0}  {1}  (the banner is due about now)" -f $end.Time.ToString('HH:mm:ss.fff'), (Get-Offset $end.Time))
}

$kept = @($mine | Where-Object { $_.Text -match 'keeping writer state' })
Write-Host ("  kept       {0} line(s) saying it stayed the writer (0 is fine for a short hole: they appear only when a heartbeat check falls inside it)" -f $kept.Count)

$flags = @($mine | Where-Object { $_.Level -eq 'ERROR' -or $_.Text -match 'demot|DemoteToReadOnly|genuinely lost|no longer ours|Unobserved' })
if ($flags.Count -eq 0) { Write-Host '  red flags  none' }
else {
    Write-Host ("  red flags  {0}:" -f $flags.Count)
    $flags | ForEach-Object { Write-Host ("             {0} [{1}] {2}" -f $_.Time.ToString('HH:mm:ss.fff'), $_.Level, $_.Text) }
}

if ($last) {
    $rec = $mine | Where-Object { $_.Time -gt $last.Time -and $_.Text -match 'Save completed successfully' } | Select-Object -First 1
    if ($rec) { Write-Host ("  recovered  {0} first successful save after the last stall" -f $rec.Time.ToString('HH:mm:ss.fff')) }
    else      { Write-Host '  recovered  NO successful save after the last stall yet: set Drop to 0 / stop clumsy and Save again.' }
}
