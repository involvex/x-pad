# Publish X-Pad for Windows x64
param(
    [ValidateSet("framework", "selfcontained")]
    [string]$Mode = "framework"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Mode -eq "framework") {
    $out = "publish/win-x64"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained false -o $out
    Write-Host "Framework-dependent publish: publish/win-x64/xpad.exe"
}
else {
    $out = "publish/win-x64-sc"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $out
    Write-Host "Self-contained publish: publish/win-x64-sc/xpad.exe"
}
