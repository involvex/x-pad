# Verifies second-instance file handoff against the published WinExe.
# Skips if an xpad process is already running (would attach to the user's session).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$pub = Join-Path $root "publish\win-x64\xpad.exe"
if (-not (Test-Path $pub)) {
    pwsh (Join-Path $PSScriptRoot "publish-win-x64.ps1")
}
if (-not (Test-Path $pub)) { throw "Missing $pub" }

$existing = Get-Process -Name "xpad" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "SKIP: xpad already running (pid(s): $($existing.Id -join ', ')). Unit tests cover the pipe; close the app to run this smoke."
    exit 0
}

$dir = Join-Path $env:TEMP ("xpad-si-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $dir | Out-Null
$fileA = Join-Path $dir "a.txt"
$fileB = Join-Path $dir "b.txt"
Set-Content -Path $fileA -Value "alpha"
Set-Content -Path $fileB -Value "beta"

$proc = $null
try {
    Write-Host "== start primary =="
    $proc = Start-Process -FilePath $pub -ArgumentList @($fileA) -PassThru
    Start-Sleep -Seconds 3
    if ($proc.HasExited) { throw "Primary exited early with code $($proc.ExitCode)" }

    Write-Host "== start secondary (should hand off and exit) =="
    $secondary = Start-Process -FilePath $pub -ArgumentList @($fileB) -PassThru -Wait
    if ($secondary.ExitCode -ne 0 -and $null -ne $secondary.ExitCode) {
        Write-Host "Secondary exit code: $($secondary.ExitCode) (0 expected after handoff)"
    }

    Start-Sleep -Seconds 1
    $alive = @(Get-Process -Name "xpad" -ErrorAction SilentlyContinue)
    if ($alive.Count -ne 1) {
        throw "Expected exactly 1 xpad process after handoff, found $($alive.Count)"
    }
    if ($alive[0].Id -ne $proc.Id) {
        throw "Primary pid changed after handoff"
    }

    Write-Host "Single-instance smoke OK (one process retained)."
}
finally {
    Get-Process -Name "xpad" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    try { Remove-Item -Recurse -Force $dir } catch { }
}
