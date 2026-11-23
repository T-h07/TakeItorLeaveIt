<#
.SYNOPSIS
    Builds the solution and lays out ready-to-copy mod folders for both Schedule I runtimes.

.DESCRIPTION
    Output (default .\artifacts\package):
        Il2Cpp\Mods\TakeItOrLeaveIt.Il2Cpp.dll   + Il2Cpp\UserLibs\TakeItOrLeaveIt.Core.dll
        Mono\Mods\TakeItOrLeaveIt.Mono.dll       + Mono\UserLibs\TakeItOrLeaveIt.Core.dll
    Copy the contents of the folder that matches your game runtime into the Schedule I folder.
    Contains only our own assemblies; no game or loader binaries.

.EXAMPLE
    pwsh tools/Package-Mod.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\package'),
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\TakeItOrLeaveIt.Game\TakeItOrLeaveIt.Game.csproj'

if (-not $NoBuild) {
    dotnet build $project -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

$builds = @(
    @{ Runtime = 'Il2Cpp'; Tfm = 'net6.0' },
    @{ Runtime = 'Mono';   Tfm = 'net472' }
)
foreach ($b in $builds) {
    $bin = Join-Path $root "src\TakeItOrLeaveIt.Game\bin\$Configuration\$($b.Tfm)"
    $modDll = Join-Path $bin "TakeItOrLeaveIt.$($b.Runtime).dll"
    $coreDll = Join-Path $bin 'TakeItOrLeaveIt.Core.dll'
    foreach ($f in $modDll, $coreDll) { if (-not (Test-Path $f)) { throw "Missing build output: $f" } }

    $mods = New-Item -ItemType Directory -Force (Join-Path $Output "$($b.Runtime)\Mods")
    $libs = New-Item -ItemType Directory -Force (Join-Path $Output "$($b.Runtime)\UserLibs")
    Copy-Item $modDll $mods
    Copy-Item $coreDll $libs
}

Get-ChildItem $Output -Recurse -File | ForEach-Object {
    '{0,-60} {1,8} bytes  SHA256 {2}' -f $_.FullName.Substring((Resolve-Path $Output).Path.Length + 1), $_.Length, (Get-FileHash $_.FullName).Hash.Substring(0, 16)
}
