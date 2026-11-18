<#
.SYNOPSIS
  Prepare a NestCoop release: checks, package, checksum, release notes and an annotated git tag.

.DESCRIPTION
  The version comes from Mod.Version in NestCoop/Mod.cs (the same value that names the zip and is used by the network
  version check), so bump it and add a "## <version>" section to packaging/CHANGELOG.md before running this.

  Steps:
    1. Checks: clean working tree, on main, main pushed to origin, tag v<version> not used yet, changelog has the section.
    2. Builds the package: dist/TDWolff-NestCoop-<version>.zip (dotnet build NestCoop -c Release -t:Package).
    3. Writes dist/TDWolff-NestCoop-<version>.zip.sha256 and dist/release-notes-<version>.md (changelog section +
       install instructions).
    4. Creates the annotated tag v<version> locally.
  Nothing is pushed or published unless you pass -Push (pushes the tag). Publishing the GitHub Release is up to you:
  the script prints the exact gh command and the website steps.

  The game's IL2CPP proxy DLLs are needed to build, which is why releases are built locally and not in CI.

.PARAMETER DryRun
  Only run the checks and write the release notes; no build, no tag.

.PARAMETER Push
  Also push the tag to origin (creates nothing else on GitHub).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/release.ps1 -DryRun
  powershell -ExecutionPolicy Bypass -File tools/release.ps1
  powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Push
#>
param(
    [switch]$DryRun,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail([string]$msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }
function Step([string]$msg) { Write-Host "== $msg" -ForegroundColor Cyan }
function Invoke-GitChecked { & git @args; if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') failed" } }

# ---------------------------------------------------------------- version
$modCs = Get-Content -Raw 'NestCoop/Mod.cs'
$m = [regex]::Match($modCs, 'Version = "(\d+\.\d+\.\d+)"')
if (-not $m.Success) { Fail 'could not read Version = "x.y.z" from NestCoop/Mod.cs' }
$version = $m.Groups[1].Value
$tag = "v$version"
$zip = "dist/TDWolff-NestCoop-$version.zip"
$sha = "$zip.sha256"
$notesFile = "dist/release-notes-$version.md"
Write-Host "NestCoop $version (tag $tag)" -ForegroundColor Green

# ---------------------------------------------------------------- checks
Step 'Checks'
$dirty = & git status --porcelain
if ($dirty) { Fail "working tree not clean - commit or stash first:`n$($dirty -join "`n")" }

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { Fail "on branch '$branch' - release from main" }

& git fetch --quiet origin 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host '   (could not fetch origin; comparing with the last known origin/main)' -ForegroundColor Yellow }
$ahead = (& git rev-list --count 'origin/main..main').Trim()
$behind = (& git rev-list --count 'main..origin/main').Trim()
if ($ahead -ne '0') { Fail "main is $ahead commit(s) ahead of origin - push first (git push origin main)" }
if ($behind -ne '0') { Fail "main is $behind commit(s) behind origin - pull first" }

if (& git tag --list $tag) { Fail "tag $tag already exists locally - bump Mod.Version (never move a published tag)" }
$remoteTag = & git ls-remote --tags origin "refs/tags/$tag" 2>$null
if ($remoteTag) { Fail "tag $tag already exists on origin - bump Mod.Version" }

$changelog = Get-Content -Raw 'packaging/CHANGELOG.md'
$sec = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($version))\s*\r?\n(.*?)(?=^## |\z)")
if (-not $sec.Success -or -not $sec.Groups[1].Value.Trim()) { Fail "packaging/CHANGELOG.md has no '## $version' section" }
Write-Host '   clean tree, on main, pushed, tag free, changelog section found'

# ---------------------------------------------------------------- notes
Step 'Release notes'
New-Item -ItemType Directory -Force 'dist' | Out-Null
$notes = @"
## What's new in $version
$($sec.Groups[1].Value.Trim())

## Install
1. Install **r2modman** and pick IRON NEST: Heavy Turret Simulator (it installs MelonLoader).
2. Download **TDWolff-NestCoop-$version.zip** below.
3. r2modman → Settings → **Import local mod** → choose the zip.
4. Start modded, quit once, then set ``Transport = "Steam"`` in ``UserData/MelonPreferences.cfg`` (``[NestCoop]``).

All players need the **same NestCoop version** and game build. Full guide: README in the zip.
SHA-256 of the zip: see **TDWolff-NestCoop-$version.zip.sha256**.
"@
[System.IO.File]::WriteAllText((Join-Path $root $notesFile), $notes, (New-Object System.Text.UTF8Encoding $false))
Write-Host "   $notesFile"

if ($DryRun) {
    Write-Host "`nDry run: checks passed and notes written; no build, no tag." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- build
Step 'Build package'
& dotnet build NestCoop -c Release -t:Package
if ($LASTEXITCODE -ne 0) { Fail 'build failed' }
if (-not (Test-Path $zip)) { Fail "$zip was not produced" }

Step 'Checksum'
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLower()
[System.IO.File]::WriteAllText((Join-Path $root $sha), "$hash  $(Split-Path -Leaf $zip)`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "   $hash"

# ---------------------------------------------------------------- tag
Step "Tag $tag"
Invoke-GitChecked tag -a $tag -m "NestCoop $version"
Write-Host "   created $tag on $((& git rev-parse --short HEAD).Trim())"

if ($Push) {
    Step 'Push tag'
    Invoke-GitChecked push origin $tag
}

# ---------------------------------------------------------------- next steps
$pre = ''
if ($version.StartsWith('0.')) { $pre = ' --prerelease' }
Write-Host "`nDone. Files:" -ForegroundColor Green
Write-Host "   $zip`n   $sha`n   $notesFile"
Write-Host "`nNext:" -ForegroundColor Green
if (-not $Push) { Write-Host "   git push origin $tag" }
Write-Host "   gh release create $tag $zip $sha --title `"NestCoop $version`" --notes-file $notesFile$pre"
Write-Host "   (or on github.com: Releases -> Draft a new release -> tag $tag, paste $notesFile, attach the zip + .sha256$(if ($pre) {', tick pre-release'}))"
