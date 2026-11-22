# Pack X-Pad as a self-contained win-x64 MSIX for Microsoft Store / sideload.
# Defaults target a local identity so you can -Install before Partner Center exists.
# For Store upload, pass PackageName / Publisher / PublisherDisplayName from Product identity.
param(
    [string]$PackageName = "Einzelunternehmen.X-Pad",
    [string]$Publisher = "CN=50777D18-6AA9-43B0-9CE5-BB776BC9514F",
    [string]$PublisherDisplayName = "Einzelunternehmen",
    [string]$DisplayName = "X-Pad",
    [switch]$Install,
    [switch]$SkipSign
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

function Get-PackageVersion {
    param([string]$Version)
    $parts = $Version.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Length -lt 2 -or $parts.Length -gt 4) {
        throw "Version '$Version' must have 2–4 numeric parts (got $($parts.Length))."
    }
    while ($parts.Length -lt 3) { $parts += "0" }
    # Store submissions require revision (4th part) to be 0.
    return "$($parts[0]).$($parts[1]).$($parts[2]).0"
}

function Find-MakeAppx {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        return $null
    }
    $candidates = Get-ChildItem -Path $kitsRoot -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
        Sort-Object FullName -Descending
    if ($candidates) {
        return $candidates[0].FullName
    }
    return $null
}

function Find-SignTool {
    param([string]$MakeAppxPath)
    if ($MakeAppxPath) {
        $sibling = Join-Path (Split-Path $MakeAppxPath -Parent) "signtool.exe"
        if (Test-Path $sibling) { return $sibling }
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) { return $null }
    $candidates = Get-ChildItem -Path $kitsRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    return $null
}

function Ensure-PackageCertificate {
    param([string]$Subject)
    $existing = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
        Sort-Object NotBefore -Descending |
        Select-Object -First 1
    if ($existing) {
        Write-Host "Using existing certificate: $($existing.Thumbprint) ($Subject)"
        return $existing
    }

    Write-Host "Creating self-signed certificate for $Subject"
    return New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "X-Pad MSIX ($Subject)" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={text}"
        )
}

function Export-PngResize {
    param(
        [string]$SourcePath,
        [string]$DestPath,
        [int]$Size
    )
    Add-Type -AssemblyName System.Drawing
    $src = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePath))
    try {
        $bmp = New-Object System.Drawing.Bitmap $Size, $Size
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.DrawImage($src, 0, 0, $Size, $Size)
            }
            finally { $g.Dispose() }
            $dir = Split-Path $DestPath -Parent
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
            $bmp.Save($DestPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bmp.Dispose() }
    }
    finally { $src.Dispose() }
}

$csproj = Join-Path $root "x-pad.csproj"
$version = Get-CsprojVersion $csproj
$packageVersion = Get-PackageVersion $version
$description = "Modern lightweight notepad with AvaloniaEdit"

$layout = Join-Path $root "publish\msix-layout"
$assetsDir = Join-Path $layout "Assets"
$msixOut = Join-Path $root "publish\xpad-$version-win-x64.msix"
$iconSource = Join-Path $root "Assets\xpad-icon-512.png"
$manifestTemplate = Join-Path $root "packaging\windows\AppxManifest.xml"

if (-not (Test-Path $iconSource)) { throw "Missing icon: $iconSource" }
if (-not (Test-Path $manifestTemplate)) { throw "Missing manifest template: $manifestTemplate" }

$makeAppx = Find-MakeAppx
if (-not $makeAppx) {
    throw @"
makeappx.exe not found. Install the Windows 10/11 SDK (App certification kit / Windows SDK),
then re-run this script. Expected under: ${env:ProgramFiles(x86)}\Windows Kits\10\bin\<version>\x64\
"@
}
Write-Host "makeappx: $makeAppx"

if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $msixOut -Parent) | Out-Null

Write-Host "Publishing self-contained win-x64 (folder layout, not single-file)…"
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE" }

Get-ChildItem -Path $layout -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
$installManifest = Join-Path $layout "xpad.install.json"
if (Test-Path $installManifest) { Remove-Item -Force $installManifest }

Write-Host "Generating package logos…"
Export-PngResize -SourcePath $iconSource -DestPath (Join-Path $assetsDir "Square44x44Logo.png") -Size 44
Export-PngResize -SourcePath $iconSource -DestPath (Join-Path $assetsDir "Square150x150Logo.png") -Size 150
Export-PngResize -SourcePath $iconSource -DestPath (Join-Path $assetsDir "StoreLogo.png") -Size 50

$manifestText = Get-Content -Raw -Path $manifestTemplate
$manifestText = $manifestText.
    Replace("{{PACKAGE_NAME}}", $PackageName).
    Replace("{{PUBLISHER}}", $Publisher).
    Replace("{{VERSION}}", $packageVersion).
    Replace("{{DISPLAY_NAME}}", $DisplayName).
    Replace("{{PUBLISHER_DISPLAY_NAME}}", $PublisherDisplayName).
    Replace("{{DESCRIPTION}}", $description)
Set-Content -Path (Join-Path $layout "AppxManifest.xml") -Value $manifestText -Encoding utf8

if (Test-Path $msixOut) { Remove-Item -Force $msixOut }

Write-Host "Packing $msixOut …"
& $makeAppx pack /d $layout /p $msixOut /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit $LASTEXITCODE" }

if (-not $SkipSign) {
    $cert = Ensure-PackageCertificate -Subject $Publisher
    $signTool = Find-SignTool -MakeAppxPath $makeAppx
    if (-not $signTool) {
        throw "signtool.exe not found next to makeappx. Install the Windows SDK signing tools."
    }
    Write-Host "Signing with thumbprint $($cert.Thumbprint)…"
    & $signTool sign /fd SHA256 /a /sha1 $cert.Thumbprint $msixOut
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit $LASTEXITCODE" }
}
else {
    Write-Host "SkipSign set — package is unsigned (Store upload still needs a matching Publisher identity)."
}

Write-Host "MSIX ready: $msixOut"
Write-Host "Identity: Name=$PackageName Publisher=$Publisher Version=$packageVersion"

if ($Install) {
    if ($SkipSign) {
        throw "-Install requires a signed package (omit -SkipSign)."
    }
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } |
        Sort-Object NotBefore -Descending |
        Select-Object -First 1
    if (-not $cert) { throw "No certificate found for $Publisher" }

    $trusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $trusted) {
        Write-Host "Trusting certificate in CurrentUser\TrustedPeople for sideload…"
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try { $store.Add($cert) }
        finally { $store.Close() }
    }

    Write-Host "Installing package…"
    Add-AppxPackage -Path $msixOut -ForceApplicationShutdown
    Write-Host "Installed. Launch from Start, or: xpad.exe (execution alias)."
}
