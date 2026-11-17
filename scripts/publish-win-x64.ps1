# Publish X-Pad for Windows x64
param(
    [ValidateSet("framework", "selfcontained")]
    [string]$Mode = "framework"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Mode -eq "framework") {
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
    Write-Host "Framework-dependent publish: publish/win-x64/xpad.exe"
}
else {
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64-sc
    Write-Host "Self-contained publish: publish/win-x64-sc/xpad.exe"
}
