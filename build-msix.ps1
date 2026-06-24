<#
.SYNOPSIS
    Builds a signed MSIX package for TermPoint.

.DESCRIPTION
    Publishes the app as self-contained, creates the MSIX mapping file,
    packs with makeappx, and signs with a self-signed certificate.
    The version in AppxManifest.xml is auto-synced from the csproj.

.PARAMETER CertThumbprint
    SHA1 thumbprint of the signing certificate. Defaults to the
    self-signed "TermPoint Dev Signing" cert created for local testing.
#>
param(
    [string]$CertThumbprint = "074F6D3AAEF899380BF7AE89B23C9F00DE357896"
)

$ErrorActionPreference = "Stop"

$RepoRoot    = $PSScriptRoot
$CsprojPath  = Join-Path $RepoRoot "src\TermPoint\TermPoint.csproj"
$MsixDir     = "C:\temp\termpoint-msix"
$PublishDir  = Join-Path $MsixDir "publish"
$ManifestPath = Join-Path $MsixDir "AppxManifest.xml"
$MappingPath = Join-Path $MsixDir "mapping.txt"
$MsixPath    = Join-Path $MsixDir "TermPoint.msix"
$MakeAppx    = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
$SignTool    = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"

# ── Read version from csproj ─────────────────────────────────────────────
$csprojXml = [xml](Get-Content $CsprojPath)
$version = $csprojXml.Project.PropertyGroup[0].Version
if (-not $version) {
    Write-Error "Could not read <Version> from $CsprojPath"
    exit 1
}
# MSIX requires four-part version (Major.Minor.Patch.Revision)
$parts = $version.Split('.')
while ($parts.Count -lt 4) { $parts += '0' }
$msixVersion = $parts[0..3] -join '.'
Write-Host "Version from csproj: $version -> MSIX version: $msixVersion" -ForegroundColor Cyan

# ── Update AppxManifest.xml version ──────────────────────────────────────
if (-not (Test-Path $ManifestPath)) {
    Write-Error "AppxManifest.xml not found at $ManifestPath. Run the initial MSIX setup first."
    exit 1
}
$manifestContent = Get-Content $ManifestPath -Raw
$manifestContent = $manifestContent -replace 'Version="[^"]*"', "Version=`"$msixVersion`""
Set-Content $ManifestPath -Value $manifestContent -Encoding UTF8
Write-Host "Updated AppxManifest.xml to version $msixVersion" -ForegroundColor Cyan

# ── Clean previous publish output ────────────────────────────────────────
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force -Confirm:$false
    Write-Host "Cleaned previous publish output" -ForegroundColor Cyan
}
if (Test-Path $MsixPath) {
    Remove-Item $MsixPath -Force -Confirm:$false
}

# ── Publish ──────────────────────────────────────────────────────────────
Write-Host "Publishing TermPoint..." -ForegroundColor Cyan
dotnet publish $CsprojPath -c Release -f net10.0 -r win-x64 --self-contained -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# ── Build mapping file ───────────────────────────────────────────────────
Write-Host "Building mapping file..." -ForegroundColor Cyan
$lines = @('[Files]')
$lines += "`"$ManifestPath`" `"AppxManifest.xml`""

Get-ChildItem (Join-Path $MsixDir "assets") -File | ForEach-Object {
    $lines += "`"$($_.FullName)`" `"Assets\$($_.Name)`""
}

Get-ChildItem $PublishDir -File -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring("$PublishDir\".Length)
    $lines += "`"$($_.FullName)`" `"$rel`""
}

$lines -join "`n" | Set-Content $MappingPath -Encoding UTF8
Write-Host "Mapping file: $($lines.Count - 1) entries" -ForegroundColor Cyan

# ── Pack ─────────────────────────────────────────────────────────────────
Write-Host "Packing MSIX..." -ForegroundColor Cyan
& $MakeAppx pack /f $MappingPath /p $MsixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makeappx pack failed"
    exit 1
}

# ── Sign ─────────────────────────────────────────────────────────────────
Write-Host "Signing MSIX..." -ForegroundColor Cyan
& $SignTool sign /fd SHA256 /sha1 $CertThumbprint $MsixPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "signtool sign failed"
    exit 1
}

Write-Host ""
Write-Host "Done! MSIX package: $MsixPath" -ForegroundColor Green
