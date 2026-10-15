# ============================================================================
#  resolve_gamedir.ps1 -- locate the BATTLETECH install directory.
#
#  ASCII-only on purpose (Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM).
#
#  Usage (dot-source it, then call):
#      . "$PSScriptRoot\resolve_gamedir.ps1"
#      $GameDir = Resolve-BattleTechDir $GameDir      # $null if not found
#
#  Resolution order:
#    1. the value passed in (e.g. a -GameDir parameter)
#    2. %BATTLETECH_DIR%
#    3. Steam: HKCU\Software\Valve\Steam\SteamPath + every "path" in
#       steamapps\libraryfolders.vdf  (so second-drive libraries are found)
#    4. well-known default locations (Steam / GOG)
# ============================================================================

function Test-BattleTechDir([string]$p) {
    if (-not $p) { return $false }
    return (Test-Path (Join-Path $p "BattleTech_Data\Managed\Assembly-CSharp.dll"))
}

function Resolve-BattleTechDir([string]$given) {
    if (Test-BattleTechDir $given) { return (Resolve-Path $given).Path }
    if (Test-BattleTechDir $env:BATTLETECH_DIR) { return (Resolve-Path $env:BATTLETECH_DIR).Path }

    $steamRoots = @()
    try {
        $sp = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath
        if ($sp) { $steamRoots += ($sp -replace "/", "\") }
    } catch { }
    $steamRoots += @(
        "C:\Program Files (x86)\Steam",
        "C:\Program Files\Steam",
        "D:\Steam", "E:\Steam", "F:\Steam"
    )
    foreach ($sr in ($steamRoots | Select-Object -Unique)) {
        if (-not (Test-Path $sr)) { continue }
        $libs = @($sr)
        $vdf = Join-Path $sr "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            # lines look like:  "path"    "D:\\Some\\Steam"
            foreach ($line in (Get-Content $vdf)) {
                $m = [regex]::Match($line, '"path"\s*"([^"]+)"')
                if ($m.Success) { $libs += ($m.Groups[1].Value -replace "\\\\", "\") }
            }
        }
        foreach ($lib in ($libs | Select-Object -Unique)) {
            $cand = Join-Path $lib "steamapps\common\BATTLETECH"
            if (Test-BattleTechDir $cand) { return (Resolve-Path $cand).Path }
        }
    }

    foreach ($cand in @(
        "C:\Program Files (x86)\Steam\steamapps\common\BATTLETECH",
        "C:\Program Files\GOG Galaxy\Games\BATTLETECH",
        "C:\GOG Games\BATTLETECH"
    )) {
        if (Test-BattleTechDir $cand) { return (Resolve-Path $cand).Path }
    }
    return $null
}
