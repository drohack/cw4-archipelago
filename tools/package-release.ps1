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
# silently passed the first time it ran. Ask the remote.
#
# THE OFFLINE FALLBACK USED TO RE-OPEN THE HOLE THIS COMMENT DESCRIBES. It was
# `if (-not $tagged) { $tagged = & git tag --list "v$version" }`, and local tags
# are exactly what cannot be trusted: measured in the working clone on
# 2026-09-17, `git tag --list v0.1.10` was EMPTY while origin had v0.1.10 at
# 0fe2bef - a version whose zip was sitting in dist/. So the fallback would have
# cheerfully re-packaged a shipped release.
#
# Worse, it could not tell a failure from an answer. $ErrorActionPreference does
# not apply to a native executable's exit code, so an ls-remote that failed on a
# dropped network returned empty and read as "not shipped".
#
# Fetch instead of guessing, and refuse to proceed if the remote cannot be
# reached - this check is the only thing standing between a re-used version
# number and a second, different "v$version" in the wild.
$tagged = & git ls-remote --tags origin "refs/tags/v$version" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw ("cannot reach origin to check whether v$version has shipped. Local " +
           "tags are not a safe substitute - this clone is missing v0.1.10 " +
           "right now. Connect, or run 'git fetch --tags' and re-run.")
}
if ($tagged) {
    throw ("v$version is already tagged, so that version has shipped. Bump " +
           "the csproj Version, Plugin.Version and archipelago.json " +
           "world_version before packaging.")
}

# CLEAN FIRST. bin\Release is gitignored, so it survives branch switches, and
# `dotnet build` never removes a file it has stopped producing. The zip is then
# assembled from a `*.dll` glob (below), so a dependency dropped from the csproj,
# or an assembly that was renamed, would keep shipping forever. Measured across
# all ten historical zips in dist/ the DLL set is identical, so this has not
# bitten yet - which is the point at which to fix it.
dotnet clean -c Release -v q $proj | Out-Null

# -p:SkipDeploy=true, because PACKAGING MUST NOT TOUCH THE PLAYER'S INSTALL.
# The csproj's DeployToGame target fires AfterTargets="Build" and copies into
# $(GameDir)\BepInEx\plugins\CW4Archipelago. The escape hatch is documented three
# lines above that target and this script - the one place it matters most - did
# not use it, so cutting a release silently overwrote the maintainer's live mod
# with a Release build mid-session.
dotnet build -c Release -v q -p:SkipDeploy=true $proj
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $repo "src\CW4Archipelago\bin\Release"
$stage = Join-Path $env:TEMP "cw4ap-stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$plugdir = Join-Path $stage "BepInEx\plugins\CW4Archipelago"
New-Item -ItemType Directory -Force $plugdir | Out-Null
Copy-Item (Join-Path $out "*.dll") $plugdir

# Copy-Item with a wildcard that matches NOTHING is a silent no-op, even under
# $ErrorActionPreference = "Stop" - verified directly. So if the output path ever
# moves (drop AppendTargetFrameworkToOutputPath and it becomes bin\Release\net6.0)
# this stages an empty folder, the debug blocklist below passes trivially, and an
# empty mod ships with a success message. Assert we actually staged something.
$staged = @(Get-ChildItem $plugdir -Filter *.dll)
if ($staged.Count -eq 0) {
    throw ("no assemblies were staged from $out - the build output path has " +
           "probably moved. Refusing to package an empty mod.")
}
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

# DELETE THE PREVIOUS BUILD FIRST, so a stale file cannot be mistaken for a new
# one. Archipelago's builder does not abort when a world fails to register - it
# does `logging.error(...)` then `continue` and exits 0 - so an import error or a
# world the pinned AP cannot load leaves build\apworlds untouched, $LASTEXITCODE
# zero, and the copy below picks up the LAST run's artifact. The absent case
# already failed loudly; the stale case was silent, and it is the dangerous one.
$built = Join-Path $ap "build\apworlds\cw4.apworld"
if (Test-Path $built) { Remove-Item -Force $built }

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
if (-not (Test-Path $built)) {
    throw ("Build APWorlds exited 0 but produced no cw4.apworld. The world " +
           "most likely failed to register - Archipelago logs that and carries " +
           "on. Check the output above for 'does not exist'.")
}
$apworldOut = Join-Path $dist "cw4.apworld"
Copy-Item $built $apworldOut -Force
Write-Output "wrote $apworldOut"

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

# --- the three assets are from THIS run, and say what they should ---
#
# dist/ is never cleaned, and only the zip carries the version in its name - the
# apworld and the yaml do not. So a throw anywhere between writing the zip and
# here used to leave a NEW zip sitting beside the PREVIOUS run's apworld and
# yaml, indistinguishable by inspection, for a human to hand-pick three files
# out of. (Right now dist/ holds ten zips and a cw4.apworld declaring 0.1.10.)
#
# Assert on the artifacts, not on the steps that produced them: the .apworld must
# carry this version and no test/, and the zip must actually contain the mod.
& python (Join-Path $PSScriptRoot "check-release.py") --apworld $apworldOut --zip $zip
if ($LASTEXITCODE -ne 0) { throw "the built assets did not pass tools/check-release.py" }

Write-Output "release artifacts ready in $dist"
