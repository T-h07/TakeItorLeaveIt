# Environment

Verified toolchain and runtime findings for Phase 00. Every value is marked with how it was established.

## 1. Development machine (inspected 2026-11-23)

| Item | Value | How verified |
| --- | --- | --- |
| OS | Windows 11 Pro, build 10.0.26200 | `Win32_OperatingSystem`, `OSVersion` |
| PowerShell | 7.7.0 (Git Bash also available) | `$PSVersionTable` |
| Git | 2.55.0.windows.3 | `git --version` |
| .NET SDK | 10.0.400 (only SDK installed) | `dotnet --list-sdks` |
| .NET runtimes | Microsoft.NETCore.App 9.0.8, 10.0.11; WindowsDesktop 9.0.8, 10.0.11; AspNetCore 10.0.11 | `dotnet --list-runtimes` |
| C# compiler | Roslyn via the .NET 10 SDK (`csc` is not on PATH) | build output |
| MSBuild / Visual Studio | Visual Studio Build Tools 2022 17.14.41 registered (installer entry); the solution builds with `dotnet build` and does not need it | uninstall registry |
| GitHub CLI | installed (`gh`) | `Get-Command` |
| Steam | **not installed** (no registry key, no Steam folders) | registry + all drives (only `C:`) |
| Schedule I | **not installed** (no `Schedule I.exe` anywhere on `C:`) | full-disk search |
| MelonLoader | **not installed** (no `MelonLoader*.dll` anywhere on `C:`) | full-disk search |

**Consequence:** the game is installed on a different PC (confirmed by the project owner). Everything that
needs the game (runtime detection on the real install, assembly inspection, in-game load test) is
**BLOCKED on this machine** and **PENDING** on the gaming PC. Use the tools in §4.

## 2. Game runtime (Mono vs IL2CPP)

| Question | Answer | Status |
| --- | --- | --- |
| Which runtime does the owner's install use? | Unknown. The owner is not sure which Steam branch is installed. | **UNVERIFIED**: run `tools/Probe-ScheduleOne.ps1` on the gaming PC |
| What do the Steam branches ship? | Default branch = **IL2CPP**; Steam beta branch **`alternate`** = **Mono**. Most players use IL2CPP. | Public sources (S1 modding docs, mod pages, S1API docs); not observed locally |
| Steam app ID | 3164500 | Steam community URL |
| Game build ID / version | Unknown | **UNVERIFIED**: probe reads `buildid` and `BetaKey` from `appmanifest_3164500.acf`; MelonLoader logs the game version |
| Unity version | Unknown | **UNVERIFIED**: logged by MelonLoader and by the mod at startup |

**Decision:** because the runtime is unknown, the mod is built for **both** runtimes from one source:

| Build | Target framework | Output | For |
| --- | --- | --- | --- |
| IL2CPP | `net6.0` | `TakeItOrLeaveIt.Il2Cpp.dll` | default Steam branch (MelonLoader IL2CPP runs mods on .NET 6) |
| Mono | `net472` | `TakeItOrLeaveIt.Mono.dll` | `alternate` beta branch (MelonLoader Mono) |

At startup the mod compares its build with `MelonUtils.IsGameIl2Cpp()` and with the on-disk markers. If
the wrong DLL is installed, it logs an actionable error and stays inactive. Supporting both is cheap in
Phase 00 because the bootstrap uses no game types. From Phase 01 onward, each game-facing adapter needs
`#if IL2CPP_BUILD` / `#if MONO_BUILD` variants (Il2Cpp namespace prefix, Il2Cpp collections). If the owner
only plays one branch, we can drop the other build to halve the testing work. **This is a decision for the owner.**

### Evidence markers (what the probe/inspector/mod check)

| Marker | Indicates |
| --- | --- |
| `GameAssembly.dll` + `Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat` | IL2CPP |
| `Schedule I_Data/Managed/Assembly-CSharp.dll` (and `MonoBleedingEdge/`) | Mono |
| Both present | Leftover files after a branch switch → reported as *Unknown*; verify game files in Steam |
| `MelonLoader/Il2CppAssemblies/` | MelonLoader has generated IL2CPP interop assemblies (first launch) |

## 3. Dependencies

| Dependency | Version | Use | Status |
| --- | --- | --- | --- |
| `LavaGang.MelonLoader` (NuGet) | **0.7.3** (latest on nuget.org; contains `lib/net35`, `lib/net472`, `lib/net6.0`) | compile-time reference only | Verified: restores and compiles; API names checked by the compiler |
| HarmonyX (`0Harmony`, namespace `HarmonyLib`) | 2.10.2 (transitive dependency of MelonLoader 0.7.3) | patch infrastructure | Verified: compiles; supplied at runtime by the installed MelonLoader |
| MelonLoader installed in game | recommend **0.7.3** (≥ 0.7.0 accepted with warnings) | runtime loader | **UNVERIFIED** in-game. 0.7.1 has reported IL2CPP interop problems with Schedule I (S1APILoader ships a workaround; a mod page says it is unsupported); the mod warns if it sees it |
| S1API (`S1API.Forked`) | 3.2.3 on NuGet (netstandard2.1 Mono build only); Thunderstore ships both runtimes | candidate for Phase 02 UI | **Not a dependency in Phase 00**; see [ARCHITECTURE.md §6](ARCHITECTURE.md#6-in-game-menu-s1api-evaluation-phase-02-recommendation) |
| xUnit 2.9.3, Microsoft.NET.Test.Sdk 18.11.1, xunit.runner.visualstudio 3.1.5 | — | unit tests | Verified (41 tests pass) |

Known benign restore notes in `TakeItOrLeaveIt.Game`: MelonLoader's nuspec pins `Il2CppInterop.* 1.5.1-ci.845`
(not on nuget.org, so 1.5.1 is resolved), plus a net6.0-untested notice for its `Microsoft.Extensions.*` 10.x
dependencies. All of these are compile-time only and are suppressed with explanations in the project file.

## 4. Collecting evidence on the gaming PC

Both tools are read-only: they change nothing in Steam or the game folder.

1. **Probe (no dependencies):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\Probe-ScheduleOne.ps1 > probe.txt
   ```
   Reports the Steam libraries (custom libraries included), `buildid`, beta branch, runtime verdict,
   `Schedule I.exe`/`UnityPlayer.dll` versions, MelonLoader version, installed mods and the
   `Latest.log` header. Redact your Windows user name from paths before sharing publicly.
2. **Inspector (needs a .NET 8+ SDK on that PC, or copy the repo there):**
   ```powershell
   dotnet run --project tools/TakeItOrLeaveIt.Inspector -- --game "<folder containing Schedule I.exe>"
   ```
   For IL2CPP, launch the game once with MelonLoader first so that `MelonLoader/Il2CppAssemblies` exists.
   Output goes to `inspection-output/` (git-ignored). Send back `environment.md` and `candidates.md`;
   `keywords.md` and `types.md` are larger and help fill in the UNVERIFIED parts of the patch map.

## 5. Build limitations

- CI and this machine can build and test everything, because the Phase 00 mod uses only redistributable
  NuGet packages. **No proprietary game assembly is referenced or committed.**
- From Phase 01 onward, code that uses game types needs the game's assemblies on the building machine
  (`Schedule I_Data/Managed` for Mono, `MelonLoader/Il2CppAssemblies` for IL2CPP). They will be referenced
  through `local.build.props` (git-ignored). CI will keep building the game-independent projects. Game-facing
  projects will be built and tested locally on a machine with the game; no fake game API stubs will be used.
