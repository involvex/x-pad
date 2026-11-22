# Smoke Open Folder / disk search primitives (no GUI).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host "== test (workspace + disk search) =="
dotnet test -v q --filter "FullyQualifiedName~WorkspaceServiceTests|FullyQualifiedName~DiskSearchTests|FullyQualifiedName~DiskReplaceTests|FullyQualifiedName~CliArgsTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root "bin\Debug\net10.0\xpad.exe"
if (-not (Test-Path $exe)) {
    Write-Host "== build =="
    dotnet build -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "== --help mentions folder =="
$helpFile = Join-Path $env:TEMP ("xpad-help-ws-" + [guid]::NewGuid().ToString("N") + ".txt")
cmd /c "`"$exe`" --help > `"$helpFile`" 2>&1"
$helpText = Get-Content -Raw $helpFile
Remove-Item $helpFile -ErrorAction SilentlyContinue
if ($helpText -notmatch "(?i)folder") {
    throw "help text should mention folders:`n$helpText"
}

Write-Host "== temp folder layout =="
$ws = Join-Path $env:TEMP ("xpad-smoke-ws-" + [guid]::NewGuid().ToString("N"))
$src = Join-Path $ws "src"
$nm = Join-Path $ws "node_modules\pkg"
New-Item -ItemType Directory -Path $src -Force | Out-Null
New-Item -ItemType Directory -Path $nm -Force | Out-Null
Set-Content -Path (Join-Path $src "note.txt") -Value "findme alpha`nfindme beta"
Set-Content -Path (Join-Path $nm "hidden.txt") -Value "findme hidden"

Write-Host "Created workspace: $ws"
Write-Host "Manual GUI: dotnet run -- `"$ws`"  then Ctrl+Shift+H search findme (should miss node_modules)."
Write-Host "Smoke OK (workspace/disk-search unit tests + help)."

Remove-Item -Recurse -Force $ws
