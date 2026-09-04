# Build the three release assets into dist/:
#   CW4Archipelago-v<version>.zip  the mod - unzip into the game folder
#   cw4.apworld                    for whoever GENERATES the multiworld
#   Creeper World 4.yaml           a ready-to-use options file for a player
#
# Three assets on one release is the convention other BepInEx Archipelago mods
# use when they own both halves - see docs/developing.md, "Releases".
# Requirements: src\GameDir.props set up, game closed, Archipelago clone at
# repo root (for the spec-correct apworld packager).
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

# --- plugin zip ---
$proj = Join-Path $repo "src\CW4Archipelago\CW4Archipelago.csproj"
[xml]$csproj = Get-Content $proj
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1

# The version is written in three files, and after v0.1.1 shipped they drifted:
# main kept calling itself 0.1.1 through twelve commits, one of which renamed
# every progressive item. A player then had a mod and a seed that both said
# "0.1.1" and disagreed about what the items were called, and nothing in the
# build objected. Refuse to package unless all three agree.
$pluginCs = Get-Content (Join-Path $repo "src\CW4Archipelago\Plugin.cs") -Raw
if ($pluginCs -notmatch 'public const string Version = "([^"]+)"') {
    throw "could not read Plugin.Version from Plugin.cs"
}
$pluginVersion = $Matches[1]
$worldMeta = Get-Content (Join-Path $repo "apworld\cw4\archipelago.json") -Raw | ConvertFrom-Json
$worldVersion = $worldMeta.world_version
if ($version -ne $pluginVersion -or $version -ne $worldVersion) {
    throw ("version mismatch - csproj $version, Plugin.cs $pluginVersion, " +
           "archipelago.json $worldVersion. Bump all three before releasing.")
}
Write-Output "version $version (csproj, Plugin.cs and archipelago.json agree)"

# THE WORKFLOW THIS IMPLIES: bump the version in the COMMIT AFTER a release.
# tools/bump-version.ps1 does it - `.	oolsump-version.ps1 -Commit -Push`
# straight after publishing. It refuses to bump from an inconsistent state or
# onto a version that has already shipped.
# CI enforces the same rule (the `version` job), so main sitting past a tag at a
# shipped version is a red build rather than something noticed weeks later. The
# invariant being protected is that a version identifies exactly one code state.
#
# AND IT HAS TO BE A NEW VERSION. The check above only proves the three files
# agree with each OTHER; it says nothing about whether that version has already
# shipped. On 2026-09-04 main sat four commits past v0.1.2 still calling itself
# 0.1.2 - the same drift this script exists to prevent, one level up. If the tag
# already exists, the version has shipped and packaging again would produce a
# second, different "v$version".
# Tags are created by `gh release create` on the REMOTE, and a clone that has
# never fetched them sees nothing locally - which is exactly how this check
# silently passed the first time it ran. Ask the remote, and fall back to local
# tags when offline.
$tagged = & git ls-remote --tags origin "refs/tags/v$version" 2>$null
if (-not $tagged) { $tagged = & git tag --list "v$version" }
if ($tagged) {
    throw ("v$version is already tagged, so that version has shipped. Bump " +
           "the csproj Version, Plugin.Version and archipelago.json " +
           "world_version before packaging.")
}

dotnet build -c Release -v q $proj
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $repo "src\CW4Archipelago\bin\Release"
$stage = Join-Path $env:TEMP "cw4ap-stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$plugdir = Join-Path $stage "BepInEx\plugins\CW4Archipelago"
New-Item -ItemType Directory -Force $plugdir | Out-Null
Copy-Item (Join-Path $out "*.dll") $plugdir
$zip = Join-Path $dist "CW4Archipelago-v$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "BepInEx") -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Output "wrote $zip"

# --- apworld ---
$ap = Join-Path $repo "Archipelago"
if (-not (Test-Path $ap)) { throw "Archipelago clone not found at $ap" }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "ap-sync.ps1")
Push-Location $ap
try {
    $env:SKIP_REQUIREMENTS_UPDATE = "1"
    python Launcher.py "Build APWorlds" -- "Creeper World 4" --skip_open_folder
    if ($LASTEXITCODE -ne 0) { throw "Build APWorlds failed" }
} finally { Pop-Location }
Copy-Item (Join-Path $ap "build\apworlds\cw4.apworld") $dist -Force
Write-Output "wrote $(Join-Path $dist 'cw4.apworld')"

# --- sample yaml ---
# Shipped as a third asset so a player has something that works without first
# installing Archipelago and generating a template themselves. R.E.P.O.'s
# Archipelago mod ships its yaml the same way. Generated from the options
# themselves, so it cannot drift from the defaults the world actually uses.
Push-Location $ap
try {
    $env:SKIP_REQUIREMENTS_UPDATE = "1"
    python -c "import Options; Options.generate_yaml_templates('build/templates')"
    if ($LASTEXITCODE -ne 0) { throw "yaml template generation failed" }
} finally { Pop-Location }
$tpl = Join-Path $ap "build/templates/Creeper World 4.yaml"
if (-not (Test-Path $tpl)) { throw "template not found at $tpl" }
$yaml = Join-Path $dist "Creeper World 4.yaml"
Copy-Item $tpl $yaml -Force

# Stamp OUR minimum Archipelago version into the template.
#
# The generator writes the version of whatever clone produced it, and this repo
# develops against a clone that tracks main - so the template came out saying
# "requires 0.6.8", a version that is not released. A player on the current
# release would be told their Archipelago is too old. The minimum lives in
# archipelago.json and is what CI actually tests against, so take it from there
# rather than hard-coding it in two places.
$meta = Get-Content (Join-Path $repo "apworld\cw4\archipelago.json") -Raw | ConvertFrom-Json
$minAp = $meta.minimum_ap_version
if (-not $minAp) { throw "minimum_ap_version missing from archipelago.json" }
$text = Get-Content $yaml -Raw
$patched = [regex]::Replace($text, '(?m)^(\s*version:\s*)\S+(\s*#)', "`${1}$minAp`${2}")
if ($patched -eq $text) { throw "could not stamp the AP version into the yaml" }
Set-Content -Path $yaml -Value $patched -Encoding utf8
Write-Output "wrote $yaml (requires Archipelago $minAp)"

Write-Output "release artifacts ready in $dist"
