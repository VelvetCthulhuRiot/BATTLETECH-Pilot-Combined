# ============================================================================
#  probe_api.ps1 - print the REAL field / property / method signatures of a game
#  type, using reflection.  This is the authoritative source for API questions
#  (tools\iltool.py can list members but does not resolve field types, and
#  dnfile/dncil are broken on this machine's Python).
#
#  ASCII-only on purpose (Windows PowerShell 5.1 reads .ps1 as ANSI w/o BOM).
#
#  Usage:
#     ... -File tools\probe_api.ps1 -Type BattleTech.PilotGenerator
#     ... -File tools\probe_api.ps1 -Type BattleTech.PilotGenerator -Member GenderWeights
#     ... -File tools\probe_api.ps1 -Type BattleTech.PilotConstantsDef -Member Weight
# ============================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Type,
    [string]$Member = "",
    [string]$GameDir = "D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH",
    [switch]$Methods
)

$ErrorActionPreference = "Stop"
$managed = Join-Path $GameDir "BattleTech_Data\Managed"

$handler = [System.ResolveEventHandler] {
    param($sender, $e)
    $simple = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    $f = Join-Path $managed ($simple + ".dll")
    if (Test-Path $f) {
        try { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($f) } catch { }
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($handler)

$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $managed "Assembly-CSharp.dll"))
$t = $asm.GetType($Type, $false)
if (-not $t) {
    Write-Host "type not found: $Type" -ForegroundColor Red
    # helpful fallback: substring search
    Write-Host "--- types whose name contains that string ---"
    try { $all = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $all = $_.Exception.Types }
    $all | Where-Object { $_ -and $_.Name -like ("*" + $Type + "*") } |
        Select-Object -First 40 | ForEach-Object { Write-Host ("   " + $_.FullName) }
    exit 1
}

Write-Host ("TYPE " + $t.FullName + "   [visibility=" + $t.IsPublic + "]")

$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor `
         [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Instance -bor `
         [System.Reflection.BindingFlags]::DeclaredOnly

Write-Host ""
Write-Host "--- fields (name : declared type) ---"
foreach ($f in $t.GetFields($flags)) {
    $line = "   {0,-28} : {1}" -f $f.Name, $f.FieldType.FullName
    if ($f.IsStatic) { $line += "   [static]" }
    if (-not $Member -or $f.Name -like ("*" + $Member + "*")) { Write-Host $line }
}

Write-Host ""
Write-Host "--- properties (name : type, getter visibility) ---"
foreach ($p in $t.GetProperties($flags)) {
    $g = $p.GetGetMethod($true)
    $vis = if ($g) { if ($g.IsPublic) { "public" } else { "nonpublic" } } else { "no-getter" }
    $line = "   {0,-28} : {1}   [{2}]" -f $p.Name, $p.PropertyType.FullName, $vis
    if (-not $Member -or $p.Name -like ("*" + $Member + "*")) { Write-Host $line }
}

if ($Methods -or -not $Member) {
    Write-Host ""
    Write-Host "--- methods (return name(params)) ---"
    foreach ($m in ($t.GetMethods($flags) | Sort-Object Name)) {
        if ($m.Name.StartsWith("get_") -or $m.Name.StartsWith("set_")) { continue }
        $ps = @($m.GetParameters() | ForEach-Object {
            $pn = if ($_.Name) { $_.Name } else { "arg" }
            $_.ParameterType.Name + " " + $pn
        }) -join ", "
        $line = "   {0,-12} {1}({2})" -f $m.ReturnType.Name, $m.Name, $ps
        if (-not $Member -or $m.Name -like ("*" + $Member + "*")) { Write-Host $line }
    }
}
