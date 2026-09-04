# Bump the version in the three places it is written, after a release.
#
#   .\tools\bump-version.ps1                 # next patch: 0.1.3 -> 0.1.4
#   .\tools\bump-version.ps1 -To 0.2.0       # an explicit version
#   .\tools\bump-version.ps1 -Commit         # also commit it
#   .\tools\bump-version.ps1 -Commit -Push   # ...and push
#
# WHY THIS EXISTS. A version has to identify exactly one code state, and twice
# it did not: v0.1.1 shipped and main kept calling itself 0.1.1 for twelve
# commits including an item rename, then v0.1.2 shipped and main sat four
# commits past it. Both times a player could hold a mod and an apworld that
# agreed on a version number and disagreed about what the items were called.
#
# CI now fails any push where main sits past a shipped tag at that tag's
# version (the `version` job), so the bump is no longer optional - it is the
# first thing to do after publishing a release. This script is that step.
[CmdletBinding()]
param(
    [string]$To = "",
    [switch]$Commit,
    [switch]$Push
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

$csprojPath = Join-Path $repo "src\CW4Archipelago\CW4Archipelago.csproj"
$pluginPath = Join-Path $repo "src\CW4Archipelago\Plugin.cs"
$worldPath = Join-Path $repo "apworld\cw4\archipelago.json"

$csprojText = Get-Content $csprojPath -Raw
$pluginText = Get-Content $pluginPath -Raw
$worldText = Get-Content $worldPath -Raw

if ($csprojText -notmatch '<Version>([^<]+)</Version>') { throw "no <Version> in the csproj" }
$current = $Matches[1]
if ($pluginText -notmatch 'public const string Version = "([^"]+)"') { throw "no Version const in Plugin.cs" }
$pluginCurrent = $Matches[1]
if ($worldText -notmatch '"world_version"\s*:\s*"([^"]+)"') { throw "no world_version in archipelago.json" }
$worldCurrent = $Matches[1]

# Refuse to bump from an inconsistent state - otherwise this quietly "fixes" a
# mismatch that someone needs to look at.
if ($current -ne $pluginCurrent -or $current -ne $worldCurrent) {
    throw ("versions already disagree - csproj $current, Plugin.cs $pluginCurrent, " +
           "archipelago.json $worldCurrent. Fix that first; this script only bumps " +
           "a consistent version.")
}

if ($To) {
    $next = $To
} else {
    if ($current -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "cannot auto-bump '$current' - pass -To explicitly"
    }
    $next = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"
}

if ($next -eq $current) { throw "already at $next" }

# Refuse to bump ONTO a version that has shipped.
$tagged = & git ls-remote --tags origin "refs/tags/v$next" 2>$null
if (-not $tagged) { $tagged = & git tag --list "v$next" }
if ($tagged) { throw "v$next is already tagged - pick another version" }

# Write UTF-8 WITHOUT a BOM. Set-Content -Encoding utf8 on Windows PowerShell
# 5.1 writes one, and the first version of this script did: it put a U+FEFF at
# the head of archipelago.json, which breaks json.load and would have shipped an
# apworld the generator cannot read. Caught by reading the diff, where it showed
# up as a changed first line.
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
function Write-NoBom([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

Write-NoBom $csprojPath ($csprojText -replace "<Version>$([regex]::Escape($current))</Version>", "<Version>$next</Version>")
Write-NoBom $pluginPath ($pluginText -replace "public const string Version = ""$([regex]::Escape($current))""", "public const string Version = ""$next""")
Write-NoBom $worldPath ($worldText -replace """world_version""\s*:\s*""$([regex]::Escape($current))""", """world_version"": ""$next""")

Write-Output "bumped $current -> $next in all three files"

if ($Commit) {
    & git -C $repo add $csprojPath $pluginPath $worldPath
    & git -C $repo commit -m "Bump to $next after releasing v$current"
    if ($LASTEXITCODE -ne 0) { throw "commit failed" }
    Write-Output "committed"
    if ($Push) {
        & git -C $repo push origin HEAD
        if ($LASTEXITCODE -ne 0) { throw "push failed" }
        Write-Output "pushed"
    }
}
