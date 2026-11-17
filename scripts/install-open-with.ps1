# Optional: add X-Pad to the Windows "Open with" / SendTo menu.
# Run from an elevated or normal PowerShell after publishing.
# Does NOT replace notepad.exe.

param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"
$exe = (Resolve-Path $ExePath).Path
if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe"
}

$sendTo = Join-Path $env:APPDATA "Microsoft\Windows\SendTo"
$shortcut = Join-Path $sendTo "X-Pad.lnk"

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = Split-Path $exe
$link.Description = "Open with X-Pad"
$link.Save()

Write-Host "Created SendTo shortcut: $shortcut"
Write-Host "Right-click a file -> Send to -> X-Pad"
Write-Host ""
Write-Host "To register as an Open With handler for .txt:"
Write-Host "  ftype XPadFile=`"$exe`" `"%1`""
Write-Host "  assoc .txt=XPadFile"
