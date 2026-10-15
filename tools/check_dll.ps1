# ============================================================================
#  check_dll.ps1 - inspect the built PilotCombined.dll with real reflection.
#
#  ReflectionOnlyLoadFrom cannot resolve the game's Unity assemblies by itself,
#  so this script wires up a ReflectionOnlyAssemblyResolve handler pointing at
#  BattleTech_Data\Managed and the mod folder.  That makes every type load and
#  lets us print the actual patch methods that will be handed to Harmony.
#
#  ASCII-only on purpose (Windows PowerShell 5.1 reads .ps1 as ANSI w/o BOM).
#
#  Usage:
#     powershell -NoProfile -ExecutionPolicy Bypass -File tools\check_dll.ps1
#     powershell -NoProfile -ExecutionPolicy Bypass -File tools\check_dll.ps1 -Dll <path>
# ============================================================================
[CmdletBinding()]
param(
    [string]$GameDir = "D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH",
    [string]$Dll = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Dll) { $Dll = Join-Path $root "out\PilotCombined.dll" }
if (-not (Test-Path $Dll)) { throw "dll not found: $Dll" }

$script:managed = Join-Path $GameDir "BattleTech_Data\Managed"
$script:extra = @($script:managed, (Split-Path -Parent $Dll))

$handler = [System.ResolveEventHandler] {
    param($sender, $e)
    $simple = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    foreach ($dir in $script:extra) {
        if (-not $dir) { continue }
        $f = Join-Path $dir ($simple + ".dll")
        if (Test-Path $f) {
            try { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($f) } catch { }
        }
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($handler)

$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($Dll)
Write-Host ("assembly : " + $asm.FullName)

$types = $null
try { $types = $asm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types }

$ok = @($types | Where-Object { $_ -ne $null })
$bad = @($types | Where-Object { $_ -eq $null })
Write-Host ("types    : " + $ok.Count + " loaded, " + $bad.Count + " failed")
if ($bad.Count -gt 0) { Write-Host "WARNING: some types failed to load" -ForegroundColor Yellow }

Write-Host ""
Write-Host "--- all types ---"
foreach ($t in ($ok | Sort-Object FullName)) { Write-Host ("   " + $t.FullName) }

Write-Host ""
Write-Host "--- methods of every 'Patches' class (these are what Harmony gets) ---"
foreach ($t in ($ok | Where-Object { $_.Name -eq "Patches" } | Sort-Object FullName)) {
    Write-Host ("[" + $t.FullName + "]")
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor `
             [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Instance -bor `
             [System.Reflection.BindingFlags]::DeclaredOnly
    foreach ($m in ($t.GetMethods($flags) | Sort-Object Name)) {
        $ps = @($m.GetParameters() | ForEach-Object { $_.ParameterType.Name + " " + $_.Name }) -join ", "
        Write-Host ("   {0} {1}({2})" -f $m.ReturnType.Name, $m.Name, $ps)
    }
}

# ---- assertions -----------------------------------------------------------
# The merged DLL must contain BOTH hook sets plus the single entry point.
# M2 merged the logger (PilotCombined.Log) and the config loader (CombinedConfig).
# M3 moved the save-affecting postfix OUT of both subsystems into PilotCombined.SaveGuard,
# so the per-subsystem copies MUST be gone (their existence would mean a duplicate hook).
$required = @(
    "PilotCombined.PilotCombinedMod",
    "PilotCombined.Log",
    "PilotCombined.CombinedConfig",
    "PilotCombined.SaveGuard",
    "PilotCombined.GenderNames",
    "PilotCombined.GenderRatio.Patches",
    "PilotCombined.GenderRatio.GenderRatioMod",
    "PilotCombined.Portrait.Patches",
    "PilotCombined.Portrait.PortraitRegistry",
    "PilotCombined.Portrait.PortraitSwapMod"
)
$requiredMethods = @{
    "PilotCombined.SaveGuard"                 = @("Install", "GetSaveAffectingModDefs_Postfix")
    "PilotCombined.GenderRatio.Patches"       = @("GetNameAndGender_Prefix")
    "PilotCombined.Portrait.Patches"          = @("GeneratePortraitTexture_Prefix", "GetPortraitSprite_Postfix",
                                                  "GetPortraitSpriteThumb_Postfix", "GetPortraitSettings_Postfix")
    "PilotCombined.PilotCombinedMod"          = @("Init")
    "PilotCombined.Portrait.PortraitRegistry" = @("CountOfGender", "PoolSummary", "PortraitPriorityActive", "RescanPool")
}
# These must NOT exist any more (M3 removed them; a leftover copy = two hooks on one method).
$forbiddenMethods = @{
    "PilotCombined.GenderRatio.Patches" = @("GetSaveAffectingModDefs_Postfix")
    "PilotCombined.Portrait.Patches"    = @("GetSaveAffectingModDefs_Postfix")
}

Write-Host ""
Write-Host "--- assertions ---"
$fail = 0
foreach ($n in $required) {
    $t = $ok | Where-Object { $_.FullName -eq $n } | Select-Object -First 1
    if ($t) { Write-Host ("   [ok]   type " + $n) }
    else { Write-Host ("   [FAIL] type " + $n + " MISSING") -ForegroundColor Red; $fail++ }
}
foreach ($n in $requiredMethods.Keys) {
    $t = $ok | Where-Object { $_.FullName -eq $n } | Select-Object -First 1
    if (-not $t) { continue }
    foreach ($mn in $requiredMethods[$n]) {
        $m = $t.GetMethod($mn, [System.Reflection.BindingFlags]::Public -bor `
                                 [System.Reflection.BindingFlags]::NonPublic -bor `
                                 [System.Reflection.BindingFlags]::Static -bor `
                                 [System.Reflection.BindingFlags]::Instance)
        if ($m) { Write-Host ("   [ok]   " + $n + "." + $mn) }
        else { Write-Host ("   [FAIL] " + $n + "." + $mn + " MISSING") -ForegroundColor Red; $fail++ }
    }
}
foreach ($n in $forbiddenMethods.Keys) {
    $t = $ok | Where-Object { $_.FullName -eq $n } | Select-Object -First 1
    if (-not $t) { continue }
    foreach ($mn in $forbiddenMethods[$n]) {
        $m = $t.GetMethod($mn, [System.Reflection.BindingFlags]::Public -bor `
                                 [System.Reflection.BindingFlags]::NonPublic -bor `
                                 [System.Reflection.BindingFlags]::Static -bor `
                                 [System.Reflection.BindingFlags]::Instance)
        if ($m) {
            Write-Host ("   [FAIL] " + $n + "." + $mn + " should be GONE (duplicate hook)") -ForegroundColor Red
            $fail++
        } else {
            Write-Host ("   [ok]   " + $n + "." + $mn + " absent (single hook kept)")
        }
    }
}
Write-Host ""
if ($fail -eq 0) { Write-Host "dll check PASSED" -ForegroundColor Green; exit 0 }
else { Write-Host ("dll check FAILED (" + $fail + ")") -ForegroundColor Red; exit 1 }
