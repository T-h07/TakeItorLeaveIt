<#
.SYNOPSIS
    Read-only environment probe for Take It Or Leave It development (Phase 00, docs/ENVIRONMENT.md).

.DESCRIPTION
    Locates Schedule I through the Steam registry key and every Steam library listed in
    libraryfolders.vdf (custom libraries included), then reports:
      - Steam build ID and selected beta branch (Mono "alternate" vs default IL2CPP)
      - Unity scripting-backend markers on disk (Mono vs IL2CPP evidence)
      - MelonLoader presence/version and the header of MelonLoader\Latest.log
      - Installed .NET SDKs

    It changes nothing. Works in Windows PowerShell 5.1 and PowerShell 7.
    Paths in the output may contain your Windows user name; redact before sharing publicly.

.PARAMETER GameDir
    Optional explicit game folder (the folder that contains "Schedule I.exe").

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Probe-ScheduleOne.ps1 > probe.txt
#>
[CmdletBinding()]
param([string]$GameDir)

$ErrorActionPreference = 'Continue'
$AppId = '3164500'   # Schedule I Steam app ID (store.steampowered.com/app/3164500)

function Section($title) { Write-Output ''; Write-Output "== $title ==" }

Section 'System'
Write-Output ("OS: " + (Get-CimInstance Win32_OperatingSystem).Caption + " " + [Environment]::OSVersion.Version)
Write-Output ("PowerShell: " + $PSVersionTable.PSVersion)
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Output 'dotnet SDKs:'; dotnet --list-sdks
} else { Write-Output 'dotnet: not installed (only needed to build or run the inspector)' }

Section 'Steam libraries'
$steamRoots = @()
foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
    $p = Get-ItemProperty $key -ErrorAction SilentlyContinue
    foreach ($v in @($p.SteamPath, $p.InstallPath)) { if ($v) { $steamRoots += ($v -replace '/', '\') } }
}
$steamRoots = $steamRoots | Select-Object -Unique
$libraries = @()
foreach ($root in $steamRoots) {
    $libraries += $root
    $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($m in Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"') {
            $libraries += ($m.Matches[0].Groups[1].Value -replace '\\\\', '\')
        }
    }
}
$libraries = $libraries | Select-Object -Unique
if (-not $libraries) { Write-Output 'Steam not found in registry.' } else { $libraries | ForEach-Object { Write-Output "library: $_" } }

Section 'Schedule I installation(s)'
$found = @()
foreach ($lib in $libraries) {
    $manifest = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
    if (Test-Path $manifest) {
        $text = Get-Content $manifest -Raw
        $dir = if ($text -match '"installdir"\s+"([^"]+)"') { Join-Path $lib ("steamapps\common\" + $Matches[1]) } else { $null }
        $build = if ($text -match '"buildid"\s+"(\d+)"') { $Matches[1] } else { '?' }
        $target = if ($text -match '"TargetBuildID"\s+"(\d+)"') { $Matches[1] } else { '?' }
        $beta = if ($text -match '"BetaKey"\s+"([^"]*)"') { $Matches[1] } else { '' }
        if (-not $beta) { $beta = '(none = default branch)' }
        Write-Output "manifest: $manifest"
        Write-Output "  installdir: $dir"
        Write-Output "  buildid: $build (target $target)"
        Write-Output "  beta branch (BetaKey): $beta"
        if ($dir) { $found += $dir }
    }
}
if ($GameDir) { $found = @($GameDir) + $found }
$found = @($found | Select-Object -Unique)
if (-not $found) { Write-Output 'Schedule I not found. Re-run with -GameDir "<folder containing Schedule I.exe>".'; exit 1 }
if ($found.Count -gt 1) { Write-Output "WARNING: multiple installations found; the first is probed below. Use -GameDir to choose." }
$game = $found[0]

Section "Runtime markers ($game)"
$data = Join-Path $game 'Schedule I_Data'
$markers = [ordered]@{
    'Schedule I.exe'                                   = Join-Path $game 'Schedule I.exe'
    'GameAssembly.dll (IL2CPP)'                        = Join-Path $game 'GameAssembly.dll'
    'il2cpp_data\Metadata\global-metadata.dat (IL2CPP)' = Join-Path $data 'il2cpp_data\Metadata\global-metadata.dat'
    'Managed\Assembly-CSharp.dll (Mono)'               = Join-Path $data 'Managed\Assembly-CSharp.dll'
    'MonoBleedingEdge\ (Mono runtime)'                 = Join-Path $game 'MonoBleedingEdge'
}
foreach ($k in $markers.Keys) { Write-Output ("{0,-55} {1}" -f $k, $(if (Test-Path $markers[$k]) { 'present' } else { 'missing' })) }
$il2cpp = (Test-Path $markers['GameAssembly.dll (IL2CPP)']) -and (Test-Path $markers['il2cpp_data\Metadata\global-metadata.dat (IL2CPP)'])
$mono = Test-Path $markers['Managed\Assembly-CSharp.dll (Mono)']
$verdict = if ($il2cpp -and -not $mono) { 'IL2CPP' } elseif ($mono -and -not $il2cpp) { 'Mono' } else { 'UNKNOWN (conflicting or missing markers)' }
Write-Output "Runtime verdict: $verdict"
foreach ($f in 'Schedule I.exe', 'UnityPlayer.dll') {
    $p = Join-Path $game $f
    if (Test-Path $p) { $vi = (Get-Item $p).VersionInfo; Write-Output "$f version: file $($vi.FileVersion) / product $($vi.ProductVersion)" }
}
$appInfo = Join-Path $data 'app.info'
if (Test-Path $appInfo) { Write-Output ("app.info: " + ((Get-Content $appInfo -TotalCount 2) -join ' / ')) }

Section 'MelonLoader'
$ml = Join-Path $game 'MelonLoader'
if (-not (Test-Path $ml)) { Write-Output 'MelonLoader: not installed' } else {
    foreach ($sub in 'net6', 'net35') {
        $dll = Join-Path $ml "$sub\MelonLoader.dll"
        if (Test-Path $dll) { Write-Output "MelonLoader\$sub\MelonLoader.dll: $((Get-Item $dll).VersionInfo.FileVersion)" }
    }
    Write-Output ("Il2CppAssemblies generated: " + (Test-Path (Join-Path $ml 'Il2CppAssemblies\Assembly-CSharp.dll')))
    foreach ($d in 'Mods', 'Plugins', 'UserLibs') {
        $p = Join-Path $game $d
        if (Test-Path $p) { Write-Output ("$d\: " + ((Get-ChildItem $p -File | Select-Object -ExpandProperty Name) -join ', ')) }
    }
    $log = Join-Path $ml 'Latest.log'
    if (Test-Path $log) {
        Write-Output 'Latest.log (header + TakeItOrLeaveIt lines):'
        Get-Content $log -TotalCount 60 | Where-Object { $_ -match 'Version|Game|Runtime|Unity' } | ForEach-Object { "  $_" }
        Select-String -Path $log -Pattern 'TakeItOrLeaveIt' | ForEach-Object { "  $($_.Line)" }
    }
}
