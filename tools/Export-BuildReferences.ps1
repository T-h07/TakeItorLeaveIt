<#
.SYNOPSIS
    Packs the reference assemblies needed to compile the Phase 02 phone-app menu, so they can be moved
    from the gaming PC to the development PC.

.DESCRIPTION
    Run this ON THE GAMING PC after MelonLoader has generated its IL2CPP assemblies (launch the game once)
    and after S1API is installed. It finds Schedule I through Steam (or -GameDir), then copies:
      - <game>\MelonLoader\Il2CppAssemblies\*   (MelonLoader-generated interop assemblies)
      - <game>\MelonLoader\net6\*                (MelonLoader / Il2CppInterop runtime libraries)
      - S1API*.dll from <game>\Mods, \Plugins and \UserLibs
    into a single zip (default: your Desktop\TakeItOrLeaveIt-refs.zip) plus a manifest with file versions
    and SHA-256 hashes.

    It is READ-ONLY for the game folder: it never modifies, moves or deletes game files.
    These files are derived from the game and must NOT be committed or shared publicly. Copy the zip to
    the development PC and unzip it outside the repository (e.g. Desktop\S1Refs).

.PARAMETER GameDir
    Optional explicit game folder (the folder that contains "Schedule I.exe").

.PARAMETER Output
    Optional zip path. Default: <Desktop>\TakeItOrLeaveIt-refs.zip

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Export-BuildReferences.ps1
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$Output = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TakeItOrLeaveIt-refs.zip')
)

$ErrorActionPreference = 'Stop'
$AppId = '3164500'

function Find-Game {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $p = Get-ItemProperty $key -ErrorAction SilentlyContinue
        foreach ($v in @($p.SteamPath, $p.InstallPath)) { if ($v) { $roots += ($v -replace '/', '\') } }
    }
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"') {
                $libraries += ($m.Matches[0].Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
        if ((Test-Path $manifest) -and ((Get-Content $manifest -Raw) -match '"installdir"\s+"([^"]+)"')) {
            $dir = Join-Path $lib ("steamapps\common\" + $Matches[1])
            if (Test-Path (Join-Path $dir 'Schedule I.exe')) { return $dir }
        }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'Schedule I.exe'))) {
    throw 'Schedule I not found. Re-run with -GameDir "<folder containing Schedule I.exe>".'
}
Write-Output "Game folder: $GameDir"

$il2cpp = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
$net6 = Join-Path $GameDir 'MelonLoader\net6'
if (-not (Test-Path (Join-Path $il2cpp 'Assembly-CSharp.dll'))) {
    throw "No generated assemblies in $il2cpp. Install MelonLoader, launch the game once, quit, then run this again."
}
if (-not (Test-Path $net6)) { throw "MelonLoader runtime folder not found: $net6" }

$s1api = @()
foreach ($sub in 'Mods', 'Plugins', 'UserLibs') {
    $dir = Join-Path $GameDir $sub
    if (Test-Path $dir) { $s1api += Get-ChildItem $dir -Filter 'S1API*.dll' -File }
}
if (-not $s1api) {
    Write-Warning 'No S1API*.dll found in Mods/Plugins/UserLibs. Install S1API_Forked first if you want the phone app; continuing without it.'
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ('tiol-refs-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    # Same layout as the game folder, which is what S1RefsDir in local.build.props expects.
    $melon = New-Item -ItemType Directory -Path (Join-Path $staging 'MelonLoader')
    Copy-Item $il2cpp (Join-Path $melon 'Il2CppAssemblies') -Recurse
    Copy-Item $net6 (Join-Path $melon 'net6') -Recurse
    foreach ($f in $s1api) {
        $dest = New-Item -ItemType Directory -Force -Path (Join-Path $staging $f.Directory.Name)
        Copy-Item $f.FullName $dest
    }

    $log = Join-Path $GameDir 'MelonLoader\Latest.log'
    $manifest = @('# TakeItOrLeaveIt build references', "Exported: $(Get-Date -Format s)", '')
    if (Test-Path $log) {
        $manifest += '## Latest.log header'
        $manifest += Get-Content $log -TotalCount 40 | Where-Object { $_ -match 'MelonLoader|Game Name|Game Version|Unity|Runtime|S1API' }
        $manifest += ''
    }
    $manifest += '## Files'
    foreach ($f in Get-ChildItem $staging -Recurse -File) {
        $ver = $f.VersionInfo.FileVersion
        $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
        $manifest += "$($f.FullName.Substring($staging.Length + 1))  v$ver  $hash"
    }
    # Strip the Windows user name from any paths before the manifest leaves this PC.
    $manifest = $manifest -replace [regex]::Escape($env:USERPROFILE), '%USERPROFILE%'
    Set-Content (Join-Path $staging 'MANIFEST.txt') $manifest -Encoding UTF8

    if (Test-Path $Output) { Remove-Item $Output }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $Output
    $sizeMb = [math]::Round((Get-Item $Output).Length / 1MB, 1)
    Write-Output "Wrote $Output ($sizeMb MB): $((Get-ChildItem $staging -Recurse -File).Count) files, S1API DLLs: $($s1api.Count)."
    Write-Output 'Copy this zip to the development PC and unzip it OUTSIDE the repository (e.g. Desktop\S1Refs). Do not share it publicly.'
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
