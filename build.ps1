# ============================================================================
#  PilotCombined build script  (ASCII-only on purpose:
#  Windows PowerShell 5.1 reads .ps1 as ANSI when there is no BOM.
#  All Chinese text lives in the C# / JSON files instead.)
#
#  Uses the .NET Framework compiler shipped with Windows (csc.exe).
#  No .NET SDK required.
#
#  Usage:
#     powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#     powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Deploy
#     powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test -Deploy
#
#  Options:
#     -GameDir <path>   BattleTech install dir.  If omitted it is auto-detected
#                       (-GameDir, then %BATTLETECH_DIR%, then Steam's library list,
#                        then the usual default locations).
#     -ModsDir <path>   mods dir (default: %USERPROFILE%\Documents\My Games\BattleTech\mods)
#     -Deploy           copy the staged mod into the mods dir
#     -Clean            wipe the installed mod folder first (default: merge, so a
#                       combined.json / portraits\ you edited by hand is kept)
#     -Test             also build+run the offline math test (uses %TEMP%\PilotCombinedTest)
#     -Pack             also write dist\PilotCombined-<version>.zip (ready to attach to
#                       a GitHub Release, so users do not have to compile anything)
# ============================================================================
[CmdletBinding()]
param(
    [string]$GameDir = "",
    [string]$ModsDir = "",
    [switch]$Deploy,
    [switch]$Clean,
    [switch]$Test,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ModFolderName = "PilotCombined"
$DllName = "PilotCombined.dll"

if (-not $ModsDir) { $ModsDir = Join-Path $env:USERPROFILE "Documents\My Games\BattleTech\mods" }

# ---- locate the game -------------------------------------------------------
# The default -GameDir used to be a hard-coded machine path, which is useless (and
# leaks a local path) in a public repository.  The resolver lives in
# tools\resolve_gamedir.ps1 so that check_dll.ps1 / probe_api.ps1 share it:
#   -GameDir  ->  %BATTLETECH_DIR%  ->  Steam library list  ->  usual locations.
. (Join-Path $root "tools\resolve_gamedir.ps1")
$GameDir = Resolve-BattleTechDir $GameDir
if (-not $GameDir) {
    throw "BATTLETECH not found. Pass it explicitly:  build.ps1 -GameDir ""X:\path\to\BATTLETECH"""
}
Write-Host "game dir : $GameDir"

# ---- compiler -------------------------------------------------------------
$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "csc.exe (.NET Framework 4.x) not found." }
Write-Host "compiler : $csc"

# ---- game assemblies ------------------------------------------------------
$managed = Join-Path $GameDir "BattleTech_Data\Managed"
$harmony = Join-Path $GameDir "BattleTech_Data\StreamingAssets\data\mods\HBS\0Harmony.dll"

$refs = @(
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $managed "UnityEngine.UIModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managed "UnityEngine.ImageConversionModule.dll"),
    (Join-Path $managed "Newtonsoft.Json.dll"),
    $harmony
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "missing reference assembly: $r" } }

$outDir = Join-Path $root "out"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$dll = Join-Path $outDir $DllName

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }

$cscArgs = @(
    "/nologo", "/target:library", "/optimize+", "/debug-", "/langversion:5",
    "/codepage:65001",
    "/out:$dll"
)
foreach ($r in $refs) { $cscArgs += "/r:$r" }
$cscArgs += $sources

# ---- offline math test (optional) ----------------------------------------
# NOTE: csc.exe and the test exe write into the source directory, which some
# sandboxes block.  Everything here is redirected into %TEMP% to avoid that.
# The exe is written into %TEMP%\PilotCombinedTest and removed right after the
# run so no stray binary is left behind.
if ($Test) {
    $t = Join-Path $env:TEMP "PilotCombinedTest"
    if (Test-Path $t) { Remove-Item $t -Recurse -Force }
    New-Item -ItemType Directory -Path $t | Out-Null

    Write-Host "building offline math test ..."
    $testArgs = @(
        "/nologo", "/target:exe", "/langversion:5", "/codepage:65001",
        "/out:$t\Math.exe",
        ("/r:" + (Join-Path $managed "Newtonsoft.Json.dll")),
        (Join-Path $root "src\Mechanic.cs"),
        (Join-Path $root "src\Ratio.cs"),
        (Join-Path $root "src\GenderNames.cs"),
        (Join-Path $root "src\Log.cs"),
        (Join-Path $root "tests\TestMain.cs")
    )
    & $csc @testArgs
    if ($LASTEXITCODE -ne 0) { throw "math test compile failed (exit $LASTEXITCODE)" }

    $out = Join-Path $t "math.out.txt"
    $err = Join-Path $t "math.err.txt"
    # Start-Process + redirection instead of "& exe 2>&1": with
    # $ErrorActionPreference = Stop, a single line on the child's stderr makes
    # PowerShell throw a NativeCommandError and the real test output is lost.
    $p = Start-Process -FilePath "$t\Math.exe" -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    $code = $p.ExitCode
    Get-Content $out -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if (Test-Path $err) {
        $et = Get-Content $err -Raw -Encoding UTF8
        if ($et -and $et.Trim()) { Write-Host $et -ForegroundColor DarkYellow }
    }
    if ($code -ne 0) {
        Write-Host "math test FAILED (exit $code)" -ForegroundColor Red
        throw "offline math test failed"
    }
    Write-Host "math test passed" -ForegroundColor Green

    # ---- offline config-migration test -----------------------------------
    # CombinedConfig / GenderRatioConfig / PortraitConfig / Log do NOT reference
    # UnityEngine, so the whole migration path can be verified without starting
    # the game.  This is the only code in the mod that touches user data, so it
    # gets its own offline test.
    Write-Host "building offline config-migration test ..."
    $cfgArgs = @(
        "/nologo", "/target:exe", "/langversion:5", "/codepage:65001",
        "/out:$t\Cfg.exe",
        ("/r:" + (Join-Path $managed "Newtonsoft.Json.dll")),
        (Join-Path $root "src\Mechanic.cs"),
        (Join-Path $root "src\Ratio.cs"),
        (Join-Path $root "src\Log.cs"),
        (Join-Path $root "src\GenderRatioConfig.cs"),
        (Join-Path $root "src\PortraitConfig.cs"),
        (Join-Path $root "src\CombinedConfig.cs"),
        (Join-Path $root "tests\TestConfig.cs")
    )
    & $csc @cfgArgs
    if ($LASTEXITCODE -ne 0) { throw "config test compile failed (exit $LASTEXITCODE)" }

    # The test exe runs from %TEMP%, so Newtonsoft.Json.dll must sit next to it;
    # otherwise touching JObject gives the useless "cannot print exception string".
    Copy-Item (Join-Path $managed "Newtonsoft.Json.dll") $t -Force

    $out2 = Join-Path $t "cfg.out.txt"
    $err2 = Join-Path $t "cfg.err.txt"
    $p2 = Start-Process -FilePath "$t\Cfg.exe" -ArgumentList @($root) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out2 -RedirectStandardError $err2
    $code2 = $p2.ExitCode
    Get-Content $out2 -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if (Test-Path $err2) {
        $et2 = Get-Content $err2 -Raw -Encoding UTF8
        if ($et2 -and $et2.Trim()) { Write-Host $et2 -ForegroundColor DarkYellow }
    }
    if ($code2 -ne 0) {
        Write-Host "config migration test FAILED (exit $code2)" -ForegroundColor Red
        throw "offline config-migration test failed"
    }
    Write-Host "config migration test passed" -ForegroundColor Green

    # ---- offline portrait-pool census -----------------------------------
    # Reproduces PortraitRegistry.BuildPool's scanning + gender classification
    # (that class needs UnityEngine, so it cannot be tested offline directly).
    # This is how we confirm - BEFORE starting the game - that e.g. a
    # "portraits\other\" folder is actually recognised as the third gender.
    Write-Host "building offline portrait-pool census ..."
    $scanArgs = @(
        "/nologo", "/target:exe", "/langversion:5", "/codepage:65001",
        "/out:$t\Scan.exe",
        (Join-Path $root "src\GenderNames.cs"),
        (Join-Path $root "tests\TestPoolScan.cs")
    )
    & $csc @scanArgs
    if ($LASTEXITCODE -ne 0) { throw "pool census compile failed (exit $LASTEXITCODE)" }

    $out3 = Join-Path $t "scan.out.txt"
    $err3 = Join-Path $t "scan.err.txt"
    # census the DEPLOYED folder when it exists (that is the user's real images)
    $censusTarget = Join-Path $ModsDir $ModFolderName
    if (-not (Test-Path $censusTarget)) { $censusTarget = $stage }
    # NOTE: -ArgumentList does NOT quote anything, and the mods path contains a space
    # ("My Games"), so the path must be quoted explicitly or it arrives truncated.
    $p3 = Start-Process -FilePath "$t\Scan.exe" -ArgumentList @('"' + $censusTarget + '"') -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out3 -RedirectStandardError $err3
    $code3 = $p3.ExitCode
    Get-Content $out3 -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if (Test-Path $err3) {
        $et3 = Get-Content $err3 -Raw -Encoding UTF8
        if ($et3 -and $et3.Trim()) { Write-Host $et3 -ForegroundColor DarkYellow }
    }
    if ($code3 -ne 0) {
        Write-Host "pool census FAILED (exit $code3)" -ForegroundColor Red
        throw "offline portrait-pool census failed"
    }
    Write-Host "portrait-pool census passed" -ForegroundColor Green
}

Write-Host "compiling ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }
Write-Host "OK -> $dll" -ForegroundColor Green

# ---- stage the mod folder -------------------------------------------------
$stage = Join-Path $outDir $ModFolderName
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item $dll $stage
Copy-Item (Join-Path $root "mod\mod.json") $stage

# The commented combined.json always ships as ".template".  The runtime uses it as
# the base document when it has to migrate an old genderratio.json / portraits.json,
# so a migrated file still carries all the explanatory comments.
# NOTE: combined.json itself is NOT staged - the deploy step below decides whether to
# write it, because writing it would pre-empt the runtime migration.
Copy-Item (Join-Path $root "mod\combined.json") (Join-Path $stage "combined.json.template")

# portrait images + the README that explains the folder layout
$modPortraits = Join-Path $root "mod\portraits"
if (Test-Path $modPortraits) {
    Copy-Item $modPortraits (Join-Path $stage "portraits") -Recurse -Force
}
# make sure the reserved "other" folder always exists in the deployed mod
New-Item -ItemType Directory -Force -Path (Join-Path $stage "portraits\other") | Out-Null

# UI language files (loc\en.json, zh.json, de.json, ru.json).  These override the
# built-in strings, so adding a language = dropping a json file in - no recompile.
$modLoc = Join-Path $root "mod\loc"
if (Test-Path $modLoc) {
    Copy-Item $modLoc (Join-Path $stage "loc") -Recurse -Force
}

# Safety: mod.json and systemMod.json must never coexist. Sharing a name across the
# system-mod and game-mod status tables triggers a vanilla duplicate-key crash in
# ModLoader.GetCombinedModStatus(). This project must NEVER ship a systemMod.json.
if (Test-Path (Join-Path $stage "systemMod.json")) {
    Remove-Item (Join-Path $stage "systemMod.json") -Force
    Write-Host "WARNING: removed a stray systemMod.json from the staged folder" -ForegroundColor Yellow
}

# Hard guarantee: the staged folder must contain mod.json, and only mod.json.
if (-not (Test-Path (Join-Path $stage "mod.json"))) { throw "staged folder has no mod.json!" }

Write-Host "staged -> $stage" -ForegroundColor Green

if ($Deploy) {
    $target = Join-Path $ModsDir $ModFolderName
    if (-not (Test-Path $ModsDir)) { throw "mods dir not found: $ModsDir" }

    if ($Clean -and (Test-Path $target)) {
        Remove-Item $target -Recurse -Force
        Write-Host "-Clean: wiped $target" -ForegroundColor Yellow
    }
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    # Merge by default: the portraits folder in the installed mod is a place the
    # user drops images into by hand, and combined.json holds their settings.
    # Those are never deleted here.
    # combined.json itself is handled separately below (it must NOT clobber the
    # user's settings, and must NOT pre-empt the runtime migration of the old files).
    Copy-Item (Join-Path $stage "*") $target -Recurse -Force

    # ---- config: combined.json ----
    $targetCfg = Join-Path $target "combined.json"
    $legacyGr = Test-Path (Join-Path $target "genderratio.json")
    $legacyPs = Test-Path (Join-Path $target "portraits.json")
    if (Test-Path $targetCfg) {
        Write-Host "config   : kept existing combined.json (your settings are untouched)" -ForegroundColor Cyan
    } elseif ($legacyGr -or $legacyPs) {
        Write-Host "config   : legacy genderratio.json / portraits.json found -> combined.json left ABSENT" -ForegroundColor Cyan
        Write-Host "           the mod migrates them into combined.json on first launch (your settings are kept)" -ForegroundColor Cyan
    } else {
        Copy-Item (Join-Path $root "mod\combined.json") $targetCfg -Force
        Write-Host "config   : wrote combined.json from the template" -ForegroundColor Cyan
    }

    if (Test-Path (Join-Path $target "systemMod.json")) {
        Remove-Item (Join-Path $target "systemMod.json") -Force
        Write-Host "WARNING: removed a stray systemMod.json from the installed mod" -ForegroundColor Yellow
    }

    Write-Host "deployed -> $target" -ForegroundColor Green
    Get-ChildItem $target | ForEach-Object { Write-Host ("   " + $_.Name) -ForegroundColor DarkGray }

    # ---- image census ------------------------------------------------------
    $counts = @{}
    foreach ($g in @("female", "male", "other")) {
        $dir = Join-Path $target ("portraits\" + $g)
        $n = 0
        if (Test-Path $dir) {
            $n = (Get-ChildItem $dir -Recurse -Include *.png,*.jpg,*.jpeg,*.dds -File).Count
        }
        $counts[$g] = $n
    }
    Write-Host ("portrait pool : female {0} / male {1} / other {2}" -f $counts["female"], $counts["male"], $counts["other"]) -ForegroundColor Green

    # ---- guard: the OLD standalone mods must not be enabled at the same time --
    # Both of them hook methods that this merged mod also hooks.  Two prefixes on
    # the same method is undefined behaviour, so warn loudly here.
    $legacy = @()
    foreach ($old in @("GenderRatio", "PortraitSwap")) {
        if (Test-Path (Join-Path $ModsDir $old)) { $legacy += $old }
    }
    if ($legacy.Count -gt 0) {
        Write-Host ("WARNING: legacy mod folder(s) still present in the mods dir: " + ($legacy -join ", ")) -ForegroundColor Red
        Write-Host "         Disable them in the in-game Mods screen, or move the folders out of 'mods'." -ForegroundColor Red
    }

    Write-Host "Start the game yourself when ready; the mod is a Game Mod, so it is safe to enable/disable in the in-game Mods screen." -ForegroundColor Cyan
}

# ============================================================================
#  -Pack : write dist\PilotCombined-<version>.zip
#  Why it matters: GitHub users cannot compile this themselves (it needs the game's
#  Managed assemblies), so a release must ship a ready-to-drop-in folder.
#  The zip contains the STAGED mod (built DLL + mod.json + combined.json template +
#  loc\ + portraits\), NOT the user's personal combined.json / image pool.
# ============================================================================
if ($Pack) {
    $ver = "0.0.0"
    try {
        $mj = Get-Content (Join-Path $root "mod\mod.json") -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($mj.Version) { $ver = $mj.Version }
    } catch { }
    $dist = Join-Path $root "dist"
    if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
    $zip = Join-Path $dist ("PilotCombined-" + $ver + ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }

    # Compress the CONTENTS under a PilotCombined\ folder so users get the right layout.
    $packRoot = Join-Path $env:TEMP ("PilotCombinedPack_" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path (Join-Path $packRoot "PilotCombined") | Out-Null
    Copy-Item (Join-Path $stage "*") (Join-Path $packRoot "PilotCombined") -Recurse -Force
    # A release is a fresh install: ship the commented config so the user can read it
    # (the deploy path deliberately does NOT do this, because there it would clobber
    #  the user's own combined.json / pre-empt the legacy migration).
    Copy-Item (Join-Path $root "mod\combined.json") (Join-Path $packRoot "PilotCombined\combined.json") -Force
    # the user's personal image pool must never leak into a release
    foreach ($g in @("female", "male", "other")) {
        $d = Join-Path $packRoot ("PilotCombined\portraits\" + $g)
        if (Test-Path $d) { Get-ChildItem $d -File | Remove-Item -Force -ErrorAction SilentlyContinue }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($packRoot, $zip)
    Remove-Item $packRoot -Recurse -Force

    $size = (Get-Item $zip).Length / 1MB
    Write-Host ("packed   -> {0}  ({1:N2} MB)" -f $zip, $size) -ForegroundColor Green
    Write-Host "           note: portraits\ in the zip is EMPTY by design (images stay out of the release;" -ForegroundColor DarkGray
    Write-Host "                 add them yourself, or attach them separately - see docs 素材与许可.md)" -ForegroundColor DarkGray
}
