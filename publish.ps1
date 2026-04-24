param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$CsprojPath  = "src\SchedulingAssistant\SchedulingAssistant.csproj"
$RepoUrl     = "https://github.com/gschlitt/SchedulingAssistant"
$PackId      = "TermPoint"
$TokenFile   = ".publish-token"

# ── Version ─────────────────────────────────────────────────────────────────

# Read the last <Version> element from the csproj (there may be two; the last wins).
$csprojContent = Get-Content $CsprojPath -Raw
$versionMatches = [regex]::Matches($csprojContent, '<Version>([^<]+)</Version>')
$currentVersion = $versionMatches[$versionMatches.Count - 1].Groups[1].Value

if (-not $Version) {
    $input = Read-Host "Version [$currentVersion]"
    $Version = if ($input.Trim()) { $input.Trim() } else { $currentVersion }
}

# Update the last <Version> tag in the csproj.
$oldTag    = "<Version>$currentVersion</Version>"
$newTag    = "<Version>$Version</Version>"
$lastIndex = $csprojContent.LastIndexOf($oldTag)
$csprojContent = $csprojContent.Substring(0, $lastIndex) + $newTag + $csprojContent.Substring($lastIndex + $oldTag.Length)
[System.IO.File]::WriteAllText((Resolve-Path $CsprojPath), $csprojContent)
Write-Host "csproj version set to $Version" -ForegroundColor Cyan

# ── Token ────────────────────────────────────────────────────────────────────

$token = $env:TERMPOINT_PUBLISH_TOKEN
if (-not $token -and (Test-Path $TokenFile)) {
    $token = (Get-Content $TokenFile -Raw).Trim()
}
if (-not $token) {
    $token = Read-Host "GitHub token (or create .publish-token file)"
}

# ── Clean ────────────────────────────────────────────────────────────────────

foreach ($dir in @("publish", "releases")) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}

# ── Publish ──────────────────────────────────────────────────────────────────

Write-Host "`nPublishing Windows $Version..." -ForegroundColor Cyan
dotnet publish $CsprojPath -c Release -r win-x64 --self-contained -o ./publish/win
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish (win-x64) failed"; exit 1 }

# ── Pack ─────────────────────────────────────────────────────────────────────

Write-Host "`nPacking Windows..." -ForegroundColor Cyan
vpk pack --packId $PackId --packVersion $Version --packDir ./publish/win --mainExe TermPoint.exe --outputDir ./releases/win
if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack (win) failed"; exit 1 }

# ── Upload Windows ───────────────────────────────────────────────────────────

Write-Host "`nUploading Windows to GitHub..." -ForegroundColor Cyan
vpk upload github --repoUrl $RepoUrl --token $token --outputDir ./releases/win --tag "v$Version" --releaseName "v$Version"
if ($LASTEXITCODE -ne 0) { Write-Error "vpk upload (win) failed"; exit 1 }

# ── Push git tag (triggers macOS GitHub Actions workflow) ────────────────────

Write-Host "`nPushing git tag v$Version..." -ForegroundColor Cyan
git tag "v$Version"
git push origin "v$Version"
if ($LASTEXITCODE -ne 0) { Write-Error "git push tag failed"; exit 1 }

# ── Done ─────────────────────────────────────────────────────────────────────

Write-Host "`nDone! Windows package uploaded and macOS build triggered." -ForegroundColor Green
Write-Host "  Monitor the macOS build at:" -ForegroundColor Green
Write-Host "  https://github.com/gschlitt/SchedulingAssistant/actions" -ForegroundColor Green
Write-Host "  Then publish the draft release at:" -ForegroundColor Green
Write-Host "  https://github.com/gschlitt/SchedulingAssistant/releases" -ForegroundColor Green
