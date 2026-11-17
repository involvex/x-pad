# Non-GUI smoke checks for X-Pad CLI / path resolution.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host "== build =="
dotnet build -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== test =="
dotnet test -v q --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root "bin\Debug\net10.0\xpad.exe"
if (-not (Test-Path $exe)) {
    throw "Missing $exe"
}

Write-Host "== --help =="
# WinExe may print via AttachConsole; also accept writing help to a temp file via cmd redirection.
$helpFile = Join-Path $env:TEMP ("xpad-help-" + [guid]::NewGuid().ToString("N") + ".txt")
cmd /c "`"$exe`" --help > `"$helpFile`" 2>&1"
$helpText = Get-Content -Raw $helpFile
Remove-Item $helpFile -ErrorAction SilentlyContinue
if ($helpText -notmatch "Usage:") { throw "help text missing Usage:`n$helpText" }

Write-Host "== publish =="
pwsh (Join-Path $PSScriptRoot "publish-win-x64.ps1")
$pub = Join-Path $root "publish\win-x64\xpad.exe"
if (-not (Test-Path $pub)) { throw "publish missing $pub" }
$pubHelpFile = Join-Path $env:TEMP ("xpad-help-pub-" + [guid]::NewGuid().ToString("N") + ".txt")
cmd /c "`"$pub`" --help > `"$pubHelpFile`" 2>&1"
$pubHelpText = Get-Content -Raw $pubHelpFile
Remove-Item $pubHelpFile -ErrorAction SilentlyContinue
if ($pubHelpText -notmatch "Usage:") { throw "published help missing Usage:`n$pubHelpText" }

Write-Host "== open-with registry (optional) =="
$appKey = "HKCU:\Software\Classes\Applications\xpad.exe"
$sendTo = Join-Path $env:APPDATA "Microsoft\Windows\SendTo\X-Pad.lnk"
if (Test-Path $appKey) {
    foreach ($ext in @(".txt", ".md", ".cs")) {
        $p = "HKCU:\Software\Classes\$ext\OpenWithList\xpad.exe"
        if (-not (Test-Path $p)) { throw "Missing Open With registration for $ext" }
    }
    if (-not (Test-Path $sendTo)) { throw "Missing SendTo shortcut: $sendTo" }
    Write-Host "Open With + SendTo present."
}
else {
    Write-Host "Skipped (run scripts/install-open-with.ps1 to register)."
}

Write-Host "Smoke OK (build/test/help/publish[/open-with]). Manual: second-instance file handoff while app is open."
