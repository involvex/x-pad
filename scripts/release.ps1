# Bump version, update CHANGELOG, tag, push, and let CI create the GitHub release.
# About panel reads Version from x-pad.csproj (assembly).
#
# Examples:
#   pwsh scripts/release.ps1                  # patch bump (0.2.2 -> 0.2.3)
#   pwsh scripts/release.ps1 -Bump minor      # 0.2.2 -> 0.3.0
#   pwsh scripts/release.ps1 -Version 1.0.0
#   pwsh scripts/release.ps1 -DryRun
#   pwsh scripts/release.ps1 -NoPush          # commit + tag locally only

param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",

    [string]$Version,

    [string]$Message,

    [switch]$DryRun,
    [switch]$SkipTests,
    [switch]$NoPush,
    [switch]$AllowDirty
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

function Set-CsprojVersion {
    param([string]$Path, [string]$NewVersion)
    $raw = Get-Content -Raw -Path $Path
    $updated = [regex]::Replace($raw, '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>", 1)
    if ($updated -eq $raw) {
        throw "Failed to update <Version> in $Path"
    }
    Set-Content -Path $Path -Value $updated -NoNewline
}

function Set-PackageJsonVersion {
    param([string]$Path, [string]$NewVersion)
    if (-not (Test-Path $Path)) { return }
    $raw = Get-Content -Raw -Path $Path
    $updated = [regex]::Replace($raw, '("version"\s*:\s*")[^"]+(")', "`${1}$NewVersion`${2}", 1)
    Set-Content -Path $Path -Value $updated -NoNewline
}

function Get-SemVerParts {
    param([string]$Value)
    if ($Value -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>.*)?$') {
        throw "Version '$Value' is not semver X.Y.Z"
    }
    return [pscustomobject]@{
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
        Suffix = $Matches.suffix
    }
}

function Get-NextVersion {
    param([string]$Current, [string]$BumpKind)
    $p = Get-SemVerParts $Current
    switch ($BumpKind) {
        "major" { return "{0}.0.0" -f ($p.Major + 1) }
        "minor" { return "{0}.{1}.0" -f $p.Major, ($p.Minor + 1) }
        default { return "{0}.{1}.{2}" -f $p.Major, $p.Minor, ($p.Patch + 1) }
    }
}

function Get-LatestVersionTag {
    $tag = git tag -l "v*" --sort=-v:refname | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($tag)) { return $null }
    return $tag.Trim()
}

function Get-CommitSubjectsSince {
    param([string]$SinceTag)
    if ($SinceTag) {
        $range = "$SinceTag..HEAD"
    }
    else {
        $range = "HEAD"
    }

    $subjects = git log $range --pretty=format:%s --no-merges
    if (-not $subjects) { return @() }
    if ($subjects -is [string]) { return @($subjects) }
    return @($subjects)
}

function Update-Changelog {
    param(
        [string]$Path,
        [string]$NewVersion,
        [string[]]$Subjects,
        [string]$ExtraMessage
    )

    $date = Get-Date -Format "yyyy-MM-dd"
    $bullets = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ExtraMessage)) {
        $bullets.Add("- $ExtraMessage")
    }
    foreach ($s in $Subjects) {
        $line = $s.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        # Skip prior release bump commits
        if ($line -match '(?i)^bump version\b') { continue }
        if ($line -match '(?i)^release\b') { continue }
        $bullets.Add("- $line")
    }
    if ($bullets.Count -eq 0) {
        $bullets.Add("- Maintenance release")
    }

    $section = @(
        "## $NewVersion - $date"
        ""
    ) + $bullets.ToArray() + @("")

    if (Test-Path $Path) {
        $existing = Get-Content -Raw -Path $Path
        if ($existing -match [regex]::Escape("## $NewVersion ")) {
            throw "CHANGELOG.md already has a section for $NewVersion"
        }
        if ($existing -match '(?m)^# Changelog\s*\r?\n') {
            $insertAt = $Matches[0].Length
            $newContent = $existing.Substring(0, $insertAt) + "`n" + ($section -join "`n") + $existing.Substring($insertAt).TrimStart("`r", "`n")
            if (-not $newContent.EndsWith("`n")) { $newContent += "`n" }
            Set-Content -Path $Path -Value $newContent -NoNewline
        }
        else {
            $body = "# Changelog`n`n" + ($section -join "`n") + "`n" + $existing.TrimStart()
            Set-Content -Path $Path -Value $body -NoNewline
        }
    }
    else {
        $body = "# Changelog`n`n" + ($section -join "`n") + "`n"
        Set-Content -Path $Path -Value $body -NoNewline
    }

    return ($section -join "`n").Trim()
}

# --- preflight ---
$status = git status --porcelain
if ($status -and -not $AllowDirty -and -not $DryRun) {
    throw "Working tree is dirty. Commit/stash first, or pass -AllowDirty."
}

$csproj = Join-Path $root "x-pad.csproj"
$packageJson = Join-Path $root "package.json"
$changelogPath = Join-Path $root "CHANGELOG.md"

$current = Get-CsprojVersion $csproj
if ([string]::IsNullOrWhiteSpace($Version)) {
    $next = Get-NextVersion -Current $current -BumpKind $Bump
}
else {
    $null = Get-SemVerParts $Version
    $next = $Version.Trim().TrimStart('v', 'V')
}

$tag = "v$next"
$existingTag = git tag -l $tag
if ($existingTag) {
    throw "Tag $tag already exists"
}

$prevTag = Get-LatestVersionTag
$subjects = Get-CommitSubjectsSince -SinceTag $prevTag

Write-Host "Current version : $current"
Write-Host "Next version    : $next ($tag)"
Write-Host "Since tag       : $(if ($prevTag) { $prevTag } else { '(none)' })"
Write-Host "Commits         : $($subjects.Count)"
if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] Would update x-pad.csproj, package.json, CHANGELOG.md"
    Write-Host "[DryRun] Would commit, tag $tag, and push (triggering .github/workflows/release.yml)"
    if ($subjects.Count -gt 0) {
        Write-Host ""
        Write-Host "Changelog subjects:"
        $subjects | ForEach-Object { Write-Host "  - $_" }
    }
    exit 0
}

if (-not $SkipTests) {
    Write-Host "Running tests..."
    dotnet test --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed"
    }
}

$notes = Update-Changelog -Path $changelogPath -NewVersion $next -Subjects $subjects -ExtraMessage $Message
Set-CsprojVersion -Path $csproj -NewVersion $next
Set-PackageJsonVersion -Path $packageJson -NewVersion $next

git add -- x-pad.csproj package.json CHANGELOG.md
git commit -m "Bump version to $next."
if ($LASTEXITCODE -ne 0) {
    throw "git commit failed"
}

git tag -a $tag -m "Release $tag"
if ($LASTEXITCODE -ne 0) {
    throw "git tag failed"
}

Write-Host ""
Write-Host "Created commit + tag $tag"
Write-Host "About panel version source: <Version>$next</Version> in x-pad.csproj"

if ($NoPush) {
    Write-Host "Skipping push (-NoPush). Push later with:"
    Write-Host "  git push origin HEAD"
    Write-Host "  git push origin $tag"
    exit 0
}

Write-Host "Pushing branch and tag..."
git push origin HEAD
if ($LASTEXITCODE -ne 0) {
    throw "git push (branch) failed"
}

git push origin $tag
if ($LASTEXITCODE -ne 0) {
    throw "git push (tag) failed"
}

Write-Host ""
Write-Host "Tag $tag pushed. GitHub Actions will build artifacts and create the release:"
Write-Host "  https://github.com/involvex/x-pad/actions"
Write-Host ""
Write-Host "Release notes preview:"
Write-Host $notes
