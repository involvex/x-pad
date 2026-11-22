# Publish X-Pad for Windows x64
param(
    [ValidateSet("framework", "selfcontained")]
    [string]$Mode = "framework"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Get-CsprojVersion {
    param([string]$Path)
    $raw = Get-Content -Raw -Path $Path
    if ($raw -notmatch '<Version>([^<]+)</Version>') {
        throw "Could not find <Version> in $Path"
    }
    return $Matches[1].Trim()
}

function Write-InstallManifest {
    param(
        [string]$OutDir,
        [string]$AssetName,
        [string]$Version
    )
    $manifest = @{ asset = $AssetName; version = $Version } | ConvertTo-Json -Compress
    Set-Content -Path (Join-Path $OutDir "xpad.install.json") -Value $manifest -Encoding utf8
}

$version = Get-CsprojVersion (Join-Path $root "x-pad.csproj")

if ($Mode -eq "framework") {
    $out = "publish/win-x64"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained false -o $out
    Write-InstallManifest -OutDir $out -AssetName "xpad-win-x64.zip" -Version $version
    Write-Host "Framework-dependent publish: publish/win-x64/xpad.exe"
}
else {
    $out = "publish/win-x64-sc"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    dotnet publish x-pad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $out
    Write-InstallManifest -OutDir $out -AssetName "xpad-win-x64-selfcontained.zip" -Version $version
    Write-Host "Self-contained publish: publish/win-x64-sc/xpad.exe"
}
