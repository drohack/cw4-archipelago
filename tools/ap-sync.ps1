# Sync the cw4 apworld source into the local Archipelago clone for testing.
# Usage: powershell -File tools/ap-sync.ps1
# Then run generation from the clone, e.g.:
#   python Generate.py --player_files_path <dir with a cw4 yaml>
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo "apworld\cw4"
$dst = Join-Path $repo "Archipelago\worlds\cw4"
if (-not (Test-Path $src)) { Write-Error "missing $src"; exit 1 }
if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
Copy-Item -Recurse $src $dst
# Keep the synced copy (and apworld builds) out of the clone's own git
# status so editors don't decorate the folder as changed. Local-only file.
$exclude = Join-Path $repo "Archipelago\.git\info\exclude"
if (Test-Path (Split-Path $exclude)) {
    $lines = @()
    if (Test-Path $exclude) { $lines = Get-Content $exclude }
    foreach ($entry in "/worlds/cw4/", "/build/") {
        if ($lines -notcontains $entry) { Add-Content -Encoding ascii $exclude $entry }
    }
}
Write-Output "synced apworld/cw4 -> Archipelago/worlds/cw4"
