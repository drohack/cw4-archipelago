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
dotnet build -c Release -v q $proj
if ($LASTEXITCODE -ne 0) { throw "build failed" }
[xml]$csproj = Get-Content $proj
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
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
Copy-Item $tpl (Join-Path $dist "Creeper World 4.yaml") -Force
Write-Output "wrote $(Join-Path $dist 'Creeper World 4.yaml')"

Write-Output "release artifacts ready in $dist"
