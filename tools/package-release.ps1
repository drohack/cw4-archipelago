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
# tools/bump-version.ps1 does it - run it with -Commit -Push straight after
# publishing. It refuses to bump from an inconsistent state or
# onto a version that has already shipped.
# CI enforces the same rule (the `version` job), so main sitting past a tag at a
# shipped version is a red build rather than something noticed weeks later. The
# invariant being protected is that a version identifies exactly one code state.
#
# AND IT HAS TO BE A NEW VERSION. The check above only proves the three files
# agree with each OTHER; it says nothing about whether that version has already
# shipped. On 2026-09-03 main sat four commits past v0.1.2 still calling itself
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

# The debug and measurement channel is a separate plugin
# (src\CW4Archipelago.Debug) and must never reach a player. Today it cannot: it
# builds to its own bin\Release and this script copies only the mod's. That is a
# property of the directory layout, which is exactly the kind of thing a later
# csproj change breaks silently - so assert on the artifact itself rather than
# on the layout that produced it.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = $archive.Entries | ForEach-Object { $_.FullName }
} finally { $archive.Dispose() }
$leaked = $entries | Where-Object { $_ -like "*Debug.dll" }
if ($leaked) {
    Remove-Item -Force $zip
    throw ("the release zip contains debug assemblies: " + ($leaked -join ", ") +
           ". The debug channel ships in no release.")
}
Write-Output "wrote $zip ($($entries.Count) entries, no debug assembly)"

# --- apworld ---
$ap = Join-Path $repo "Archipelago"
if (-not (Test-Path $ap)) { throw "Archipelago clone not found at $ap" }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "ap-sync.ps1")
Push-Location $ap
try {
    $env:SKIP_REQUIREMENTS_UPDATE = "1"
    # No --skip_open_folder: that flag does not exist in 0.6.7, which is the
    # version this repo's clone is pinned to and the minimum the world declares.
    # It arrived after that release, so passing it made argparse reject the whole
    # invocation ("unrecognized arguments") and the packager threw. Nothing
    # caught it because the clone was moved from main to 0.6.7 and no release has
    # been cut since. 0.6.7's builder takes only the world names, positionally,
    # and pops the output folder open when it finishes - cosmetic, and the price
    # of packaging with the version we actually support.
    python Launcher.py "Build APWorlds" -- "Creeper World 4"
    if ($LASTEXITCODE -ne 0) { throw "Build APWorlds failed" }
} finally { Pop-Location }
Copy-Item (Join-Path $ap "build\apworlds\cw4.apworld") $dist -Force
Write-Output "wrote $(Join-Path $dist 'cw4.apworld')"

# --- sample yaml ---
# Shipped as a third asset so a player has something that works without first
# installing Archipelago and generating a template themselves. R.E.P.O.'s
# Archipelago mod ships its yaml the same way. Generated from the options
# themselves, so it cannot drift from the defaults the world actually uses.
#
# GENERATED BY THE ARCHIPELAGO WE SUPPORT, not the clone we develop against.
# This repo's clone tracks main, so the template was coming out of a version
# that is not released. The first symptom was cosmetic - a core option's example
# text differed from what a player's own template showed, spotted by a player
# comparing the two - but the same mechanism would ship an option that only
# exists upstream, which a supported Archipelago would not understand. It used
# to be handled by rewriting the version: line afterwards, which corrected the
# claim and not the content.
#
# A git worktree of the clone at the minimum tag costs nothing: same repo, same
# interpreter, same installed packages.
$meta = Get-Content (Join-Path $repo "apworld\cw4\archipelago.json") -Raw | ConvertFrom-Json
$minAp = $meta.minimum_ap_version
if (-not $minAp) { throw "minimum_ap_version missing from archipelago.json" }

$apMin = Join-Path $repo ".aptest\ap-min"
if (Test-Path (Join-Path $apMin ".git")) {
    & git -C $apMin fetch --tags --quiet 2>$null
    & git -C $apMin checkout --quiet $minAp
    if ($LASTEXITCODE -ne 0) { throw "could not check out Archipelago $minAp in $apMin" }
} else {
    if (Test-Path $apMin) { Remove-Item -Recurse -Force $apMin }
    & git -C $ap worktree add --force --detach $apMin $minAp
    if ($LASTEXITCODE -ne 0) { throw "could not create an Archipelago $minAp worktree (is the tag fetched?)" }
}

# Confirm it really is that version, rather than trusting the checkout.
$apMinVersion = & python -c "import sys; sys.path.insert(0, r'$apMin'); import Utils; print('.'.join(str(n) for n in Utils.version_tuple))"
if ($apMinVersion.Trim() -ne $minAp) {
    throw "the worktree reports Archipelago $apMinVersion but minimum_ap_version is $minAp"
}
Write-Output "generating the yaml with Archipelago $apMinVersion (our declared minimum)"

$minWorld = Join-Path $apMin "worlds\cw4"
if (Test-Path $minWorld) { Remove-Item -Recurse -Force $minWorld }
Copy-Item (Join-Path $repo "apworld\cw4") $minWorld -Recurse -Force

Push-Location $apMin
try {
    $env:SKIP_REQUIREMENTS_UPDATE = "1"
    python -c "import Options; Options.generate_yaml_templates('build/templates')"
    if ($LASTEXITCODE -ne 0) { throw "yaml template generation failed" }
} finally { Pop-Location }
$tpl = Join-Path $apMin "build/templates/Creeper World 4.yaml"
if (-not (Test-Path $tpl)) { throw "template not found at $tpl" }
$yaml = Join-Path $dist "Creeper World 4.yaml"
Copy-Item $tpl $yaml -Force

# It should already say the right version, having been generated there. Assert
# rather than rewrite: a mismatch now means something is wrong upstream of here.
$text = Get-Content $yaml -Raw
if ($text -notmatch "(?m)^\s*version:\s*$([regex]::Escape($minAp))\b") {
    throw "the generated yaml does not declare Archipelago $minAp"
}
Write-Output "wrote $yaml (requires Archipelago $minAp)"

Write-Output "release artifacts ready in $dist"
