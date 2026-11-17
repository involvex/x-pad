# Optional: add X-Pad to SendTo + per-user Open With for common text/code types.
# Does NOT replace notepad.exe as the default handler.
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

$extensions = @(
    ".txt", ".log", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
    ".ini", ".cfg", ".conf", ".toml", ".editorconfig", ".gitignore", ".env",
    ".cs", ".fs", ".vb", ".py", ".js", ".ts", ".jsx", ".tsx", ".css", ".html", ".htm",
    ".sql", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".c", ".h", ".cpp", ".hpp",
    ".go", ".rs", ".java", ".kt", ".rb", ".php", ".r", ".lua"
)

$appKey = "HKCU:\Software\Classes\Applications\xpad.exe"
New-Item -Path $appKey -Force | Out-Null
Set-ItemProperty -Path $appKey -Name "FriendlyAppName" -Value "X-Pad"

$cmdKey = Join-Path $appKey "shell\open\command"
New-Item -Path $cmdKey -Force | Out-Null
Set-ItemProperty -Path $cmdKey -Name "(default)" -Value "`"$exe`" `"%1`""

$supported = Join-Path $appKey "SupportedTypes"
New-Item -Path $supported -Force | Out-Null

foreach ($ext in $extensions) {
    Set-ItemProperty -Path $supported -Name $ext -Value ""
    $list = "HKCU:\Software\Classes\$ext\OpenWithList\xpad.exe"
    New-Item -Path $list -Force | Out-Null
    Set-ItemProperty -Path $list -Name "(default)" -Value ""
    $progids = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    New-Item -Path $progids -Force | Out-Null
    New-ItemProperty -Path $progids -Name "Applications\xpad.exe" -PropertyType None -Value ([byte[]]@()) -Force | Out-Null
}

Write-Host "Registered Open With for $($extensions.Count) extensions (per-user)."
Write-Host "Right-click a file -> Open with -> X-Pad"
Write-Host "Send to -> X-Pad"
