<#
.SYNOPSIS
    Builds a signed MSIX package for TermPoint and exports the signing cert.

.DESCRIPTION
    Publishes the app as self-contained, creates the MSIX mapping file,
    packs with makeappx, and signs with a self-signed certificate. Auto-
    creates the signing cert if not present in Cert:\CurrentUser\My. The
    version in AppxManifest.xml is auto-synced from the csproj.

    Output (in $MsixDir): TermPoint.msix + TermPoint-cert.cer. Distribute
    both together — install the .cer into Local Machine\Trusted Root
    Certification Authorities on the target machine, then double-click
    the .msix.

.PARAMETER Store
    Builds a Microsoft Store submission package instead of a sideload
    package. In Store mode the script:
      - Stamps the manifest with the identity reserved in Partner Center
        (StoreIdentityName / StorePublisher / StorePublisherDisplayName).
        These come from Partner Center -> Product -> Product identity.
      - Does NOT sign the package (the Store re-signs on submission) and
        does NOT export a .cer.
      - Drops the .pdb from the payload to keep the package lean.
    The result is still a single TermPoint.msix — upload that one file.

.EXAMPLE
    .\build-msix.ps1 -Store `
        -StoreIdentityName "12345Publisher.TermPoint" `
        -StorePublisher "CN=ABCDEF12-3456-7890-ABCD-EF1234567890" `
        -StorePublisherDisplayName "Academic Solutions"
#>
param(
    [string]$CertSubject = "CN=Academic-Solutions",

    [switch]$Store,
    [string]$StoreIdentityName,
    [string]$StorePublisher,
    [string]$StorePublisherDisplayName
)

# In Store mode all three Partner Center identity strings are required —
# they must match the reserved identity exactly or submission is rejected.
if ($Store) {
    $missing = @()
    if (-not $StoreIdentityName)        { $missing += "StoreIdentityName" }
    if (-not $StorePublisher)           { $missing += "StorePublisher" }
    if (-not $StorePublisherDisplayName){ $missing += "StorePublisherDisplayName" }
    if ($missing.Count -gt 0) {
        Write-Error "Store mode requires: $($missing -join ', '). Find these in Partner Center -> Product -> Product identity."
        exit 1
    }
}

$ErrorActionPreference = "Stop"

$RepoRoot    = $PSScriptRoot
$CsprojPath  = Join-Path $RepoRoot "src\TermPoint\TermPoint.csproj"

# Source inputs (under version control)
$PackagingDir     = Join-Path $RepoRoot "packaging"
$SourceManifest   = Join-Path $PackagingDir "AppxManifest.xml"
$SourceAssetsDir  = Join-Path $PackagingDir "assets"

# Build outputs (regenerated each run; safe to delete)
$MsixDir      = "C:\temp\termpoint-msix"
$PublishDir   = Join-Path $MsixDir "publish"
$ManifestPath = Join-Path $MsixDir "AppxManifest.xml"
$MappingPath  = Join-Path $MsixDir "mapping.txt"
$MsixPath     = Join-Path $MsixDir "TermPoint.msix"
$CerPath      = Join-Path $MsixDir "TermPoint-cert.cer"

$MakeAppx    = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
$SignTool    = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
$MakePri     = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makepri.exe"

# ── Validate source inputs ───────────────────────────────────────────────
if (-not (Test-Path $SourceManifest)) {
    Write-Error "Missing source manifest: $SourceManifest"
    exit 1
}
if (-not (Test-Path $SourceAssetsDir)) {
    Write-Error "Missing source assets directory: $SourceAssetsDir"
    exit 1
}

# Ensure build output dir exists and copy source inputs into it
New-Item -ItemType Directory -Force -Path $MsixDir | Out-Null
Copy-Item $SourceManifest $ManifestPath -Force
$BuildAssetsDir = Join-Path $MsixDir "assets"
New-Item -ItemType Directory -Force -Path $BuildAssetsDir | Out-Null
Copy-Item (Join-Path $SourceAssetsDir "*") $BuildAssetsDir -Force

# ── Locate or create signing cert ────────────────────────────────────────
# Skipped entirely in Store mode — the Microsoft Store re-signs the package
# on submission, so a self-signed cert would only cause an identity mismatch.
if (-not $Store) {
    # Match by Subject. The cert's CN must exactly match the Publisher in
    # AppxManifest.xml or signtool will reject the package.
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        Write-Host "No valid signing cert found for $CertSubject — creating one..." -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $CertSubject `
            -KeyUsage DigitalSignature `
            -FriendlyName "TermPoint Dev Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
            -NotAfter (Get-Date).AddYears(3)
        Write-Host "Created cert: thumbprint $($cert.Thumbprint)" -ForegroundColor Green
    } else {
        Write-Host "Using existing cert: thumbprint $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Cyan
    }
    $CertThumbprint = $cert.Thumbprint

    # Export the public cert (.cer, no private key) for distribution.
    Export-Certificate -Cert $cert -FilePath $CerPath -Type CERT -Force | Out-Null
    Write-Host "Exported public cert: $CerPath" -ForegroundColor Cyan
} else {
    Write-Host "Store mode: skipping signing cert (the Store signs the package)." -ForegroundColor Yellow
}

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

# ── Update AppxManifest.xml version (in build dir, not source) ───────────
# Parse as XML so we update ONLY the <Identity Version="..."> attribute,
# not the XML declaration's version="1.0" or <Dependencies MinVersion=...>.
$manifestXml = [xml](Get-Content $ManifestPath)
$identityNode = $manifestXml.Package.Identity
$identityNode.Version = $msixVersion

# In Store mode, overwrite the identity with the values reserved in Partner
# Center. These must match exactly or the submission is rejected.
if ($Store) {
    $identityNode.Name      = $StoreIdentityName
    $identityNode.Publisher = $StorePublisher
    $manifestXml.Package.Properties.PublisherDisplayName = $StorePublisherDisplayName
    Write-Host "Store identity: Name=$StoreIdentityName Publisher=$StorePublisher" -ForegroundColor Cyan
}

$manifestXml.Save($ManifestPath)
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
# Store packages ship no debug symbols — drop the .pdb to keep the upload lean.
$publishArgs = @($CsprojPath, '-c', 'Release', '-f', 'net10.0', '-r', 'win-x64', '--self-contained', '-o', $PublishDir)
if ($Store) {
    $publishArgs += @('-p:DebugType=none', '-p:DebugSymbols=false')
}
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# ── Build mapping file ───────────────────────────────────────────────────
# Use an ArrayList so .Add() mutates in place — `+=` inside ForEach-Object
# operates on a copy of the variable and silently loses entries.
Write-Host "Building mapping file..." -ForegroundColor Cyan
$lines = [System.Collections.ArrayList]::new()
[void]$lines.Add('[Files]')
[void]$lines.Add("`"$ManifestPath`" `"AppxManifest.xml`"")

Get-ChildItem (Join-Path $MsixDir "assets") -File | ForEach-Object {
    [void]$lines.Add("`"$($_.FullName)`" `"Assets\$($_.Name)`"")
}

Get-ChildItem $PublishDir -File -Recurse -Force | ForEach-Object {
    $rel = $_.FullName.Substring("$PublishDir\".Length)
    [void]$lines.Add("`"$($_.FullName)`" `"$rel`"")
}

$lines -join "`n" | Set-Content $MappingPath -Encoding UTF8
Write-Host "Mapping file: $($lines.Count - 1) entries" -ForegroundColor Cyan

# ── Generate resource index ─────────────────────────────────────────────
# resources.pri lets Windows discover targetsize / altform-unplated icon
# variants that aren't explicitly named in AppxManifest.xml.
$PriConfigPath = Join-Path $MsixDir "priconfig.xml"
$PriPath       = Join-Path $MsixDir "resources.pri"
Write-Host "Generating resources.pri..." -ForegroundColor Cyan
& $MakePri createconfig /cf $PriConfigPath /dq en-US /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makepri createconfig failed"
    exit 1
}
& $MakePri new /pr $MsixDir /cf $PriConfigPath /of $PriPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makepri new failed"
    exit 1
}
# Append resources.pri to the mapping file
Add-Content $MappingPath "`"$PriPath`" `"resources.pri`""

# ── Pack ─────────────────────────────────────────────────────────────────
Write-Host "Packing MSIX..." -ForegroundColor Cyan
& $MakeAppx pack /f $MappingPath /p $MsixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makeappx pack failed"
    exit 1
}

# ── Sign ─────────────────────────────────────────────────────────────────
# Store mode leaves the package unsigned — the Store signs it on submission.
if (-not $Store) {
    Write-Host "Signing MSIX..." -ForegroundColor Cyan
    & $SignTool sign /fd SHA256 /sha1 $CertThumbprint $MsixPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool sign failed"
        exit 1
    }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "  MSIX package: $MsixPath" -ForegroundColor Green
if ($Store) {
    Write-Host ""
    Write-Host "Store submission package (unsigned). Upload this single .msix in" -ForegroundColor Gray
    Write-Host "Partner Center -> your app -> Packages. The Store signs it for you." -ForegroundColor Gray
} else {
    Write-Host "  Signing cert: $CerPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "To install on another machine: copy both files, then on the target" -ForegroundColor Gray
    Write-Host "import the .cer into Local Machine\Trusted Root Certification Authorities" -ForegroundColor Gray
    Write-Host "(certlm.msc, run as admin) and double-click the .msix." -ForegroundColor Gray
}
