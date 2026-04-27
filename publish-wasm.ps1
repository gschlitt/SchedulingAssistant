# publish-wasm.ps1 — Build, publish, and serve the WASM browser demo.
# Usage: .\publish-wasm.ps1 [-Port 8080]
# Ctrl+C stops the server cleanly.

param(
    [int]$Port = 8080
)

$AppBundle  = "src\SchedulingAssistant.Browser\bin\Release\net10.0-browser\browser-wasm\AppBundle"
$BrowserProj = "src\SchedulingAssistant.Browser\SchedulingAssistant.Browser.csproj"

# Kill any leftover node process from a previous run
Write-Host "Stopping any running serve process..." -ForegroundColor Cyan
Get-Process -Name "node" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Publish
Write-Host "Publishing WASM app..." -ForegroundColor Cyan
dotnet publish $BrowserProj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed." -ForegroundColor Red
    exit 1
}

# Serve — launch via cmd so npx resolves normally; track PID for clean teardown
Write-Host ""
Write-Host "Serving at http://localhost:$Port  (Ctrl+C to stop)" -ForegroundColor Green
Write-Host ""

$bundlePath = Resolve-Path $AppBundle
$proc = Start-Process -FilePath "cmd" `
    -ArgumentList "/c npx --yes serve -l $Port `"$bundlePath`"" `
    -NoNewWindow -PassThru

try {
    # Polling loop so Ctrl+C can interrupt between sleeps
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 300
    }
} finally {
    if (-not $proc.HasExited) {
        # taskkill /T kills the entire process tree (cmd + node children)
        taskkill /F /T /PID $proc.Id 2>$null | Out-Null
        Write-Host "`nServer stopped." -ForegroundColor DarkGray
    }
}
