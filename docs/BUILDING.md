# Building

## Requirements

| Tool | Version | Notes |
| --- | --- | --- |
| Windows | 10/11 | CI uses `windows-latest` |
| .NET SDK | 10.0.100 or newer 10.0.x (`global.json`, `rollForward: latestFeature`) | Verified with 10.0.400 |
| Git | any recent version | |
| Internet access for the first restore | — | NuGet: `LavaGang.MelonLoader` 0.7.3, xUnit |

**No Schedule I files are needed for a normal build** (the Phase 02 phone app and the Phase 03 player-sales
hooks are the exception, see [local references](#phase-02-phone-app-local-references)). MelonLoader and HarmonyX come from NuGet as
compile-time references; the copies installed in the game are used at runtime.

## Commands

From the repository root:

```powershell
dotnet restore TakeItOrLeaveIt.sln
```

```powershell
dotnet build TakeItOrLeaveIt.sln -c Release
```

```powershell
dotnet test TakeItOrLeaveIt.sln -c Release --no-build
```

```powershell
pwsh tools/Package-Mod.ps1 -Configuration Release
```

## Output

| File | Location after `dotnet build -c Release` | Install to |
| --- | --- | --- |
| `TakeItOrLeaveIt.Il2Cpp.dll` | `src/TakeItOrLeaveIt.Game/bin/Release/net6.0/` | `<game>/Mods/` (default Steam branch) |
| `TakeItOrLeaveIt.Mono.dll` | `src/TakeItOrLeaveIt.Game/bin/Release/net472/` | `<game>/Mods/` (`alternate` branch) |
| `TakeItOrLeaveIt.Core.dll` | next to either of the above | `<game>/UserLibs/` |

`tools/Package-Mod.ps1` copies these into `artifacts/package/Il2Cpp/{Mods,UserLibs}` and
`artifacts/package/Mono/{Mods,UserLibs}` and prints sizes and hashes. CI uploads the same folder as the
`TakeItOrLeaveIt-<sha>` artifact.

## Optional: automatic deployment to a local game

1. Copy `local.build.props.example` to `local.build.props` (git-ignored).
2. Set `ScheduleOneGameDir` (the folder containing `Schedule I.exe`), `ScheduleOneRuntime` (`Il2Cpp` or `Mono`) and `DeployToGame` = `true`.
3. `dotnet build` copies the matching mod DLL into `<game>/Mods` and Core into `<game>/UserLibs` after each build.
   The build fails with a clear message if the folder or MelonLoader is missing. It only ever writes our own files.

## Phase 02 phone app (local references)

> The same `S1RefsDir` also enables the **Phase 03 player-sales hooks** (`GAME_PATCHES`, see below).

The in-game menu is an S1API phone app. It needs Unity UI and S1API types at compile time, which are not
on NuGet for IL2CPP, so it is compiled **only** for the IL2CPP build and **only** when `S1RefsDir` points to
reference assemblies exported from a real game install. Without it (CI, fresh clones) everything still
builds and tests, but the mod has no menu and logs that.

1. On the gaming PC (game launched once with MelonLoader, S1API installed), run the read-only export:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\Export-BuildReferences.ps1
   ```

2. Copy the zip to the development PC and unzip it **outside the repository**. The folder must contain
   `Mods\S1API.Il2Cpp.MelonLoader.dll` and `MelonLoader\Il2CppAssemblies\*.dll` (the script writes that layout).
3. In `local.build.props` set `<S1RefsDir>` to that folder (see `local.build.props.example`).
4. Build. The output must show `TakeItOrLeaveIt: building WITH the S1API phone app` and
   `TakeItOrLeaveIt: building WITH the player-sales game hooks`.

Verified with: Schedule I v0.4.7f12 IL2CPP, S1API 3.2.3.0 (`S1API.Il2Cpp.MelonLoader.dll`, built against
MelonLoader 0.7.3 / Il2CppInterop 1.5.1, the same versions as our NuGet references), MelonLoader 0.7.4 runtime folder.

Referenced (compile-time only, `Private=false`, never copied into output or the package): S1API,
Il2Cppmscorlib, UnityEngine.CoreModule, UnityEngine.UI, UnityEngine.UIModule, UnityEngine.TextRenderingModule,
UnityEngine.IMGUIModule, UnityEngine.InputLegacyModule. **Never commit these files** (they are game-derived;
`.gitignore` blocks `*.dll` and `local.build.props`).

## Phase 03 player-sales hooks (local references)

The Harmony hooks on the customer-contract code (`src/TakeItOrLeaveIt.Game/Sales/`) are compiled only for the
IL2CPP build and only when `S1RefsDir` contains `MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`
(define `GAME_PATCHES`). The export from step 1 above already contains everything. Additional compile-time
references (`Private=false`, never packaged): Assembly-CSharp, Il2CppScheduleOne.Core, Il2CppFishNet.Runtime,
Unity.TextMeshPro.

Without them (CI, Mono) the mod still builds and all tests run; the log and the status report then say
`Player Sales: Unavailable` with the reason, and sales stay vanilla. The package contents do not change: still
only `TakeItOrLeaveIt.Il2Cpp.dll` / `TakeItOrLeaveIt.Mono.dll` and `TakeItOrLeaveIt.Core.dll`.

To check the hooks against a new game version, rerun the export on the gaming PC and rebuild: a renamed or
removed member fails the build; a changed overload fails at startup and the mod removes all its patches.
The call graph can be regenerated with the inspector's `--xref` mode ([PHASE_03_PATCH_ANALYSIS.md](PHASE_03_PATCH_ANALYSIS.md)).

## Game assembly references

Discovery does **not** reference game assemblies: it reads game data by reflection at runtime. The phone app
and the player-sales hooks above use compile-time references through the same local mechanism:

- Mono: `<game>/Schedule I_Data/Managed/Assembly-CSharp.dll`, `ScheduleOne.Core.dll`, `UnityEngine*.dll`
- IL2CPP: `<game>/MelonLoader/Il2CppAssemblies/*.dll` (generated by MelonLoader on first launch) and `<game>/MelonLoader/net6/Il2CppInterop.*.dll`

These are never committed (`.gitignore` blocks `*.dll`). CI will continue to build only what can be built
from public packages; the game-facing build is a documented local step.

## Common problems

| Symptom | Cause / fix |
| --- | --- |
| `NETSDK1045` / SDK not found | Install a .NET 10 SDK (`global.json` requires 10.0.100+) |
| `NU1603` about `Il2CppInterop … 1.5.1-ci.845` | Expected: the MelonLoader nuspec pins a CI build; 1.5.1 is used. Suppressed in the project |
| Any compiler warning fails the build | `TreatWarningsAsErrors` is on by design; fix the warning |
| `ScheduleOneGameDir … does not contain 'Schedule I.exe'` | Wrong path in `local.build.props` |
| Deploy step never runs | `DeployToGame` must be `true` **and** `ScheduleOneRuntime` must match the target (`Il2Cpp` = net6.0, `Mono` = net472) |
| Locked DLL during deploy | Close the game before building with deploy enabled |
| "building WITHOUT the phone app" | `S1RefsDir` unset or wrong, or `Mods\S1API.Il2Cpp.MelonLoader.dll` missing in it |
| `CS0246` for `S1API` / `UnityEngine` types | Same as above: the `PhoneMenu` folder only compiles with the references |
