# Installation (development build 0.0.1-dev)

> Phase 00 build: it **only loads and logs**. It does not change customers, dealers, products, money,
> XP or saves. In-game loading has **not been tested yet** (acceptance tests P00-T07/T08/T09 are pending).

## 1. Compatibility

| Item | Requirement | Status |
| --- | --- | --- |
| Game | Schedule I (Steam app 3164500), Windows | Game build not yet recorded; fill in from the probe |
| Runtime | IL2CPP (default branch) → `Il2Cpp` build · Mono (`alternate` beta) → `Mono` build | Both builds compile; neither has been loaded in-game yet |
| Loader | **MelonLoader 0.7.3** recommended (0.7.0+ accepted with a warning; avoid 0.7.1 on IL2CPP) | Built against 0.7.3 |
| Other mods | None required. S1API is **not** needed for Phase 00 | — |

Not sure which branch you have? Run `tools/Probe-ScheduleOne.ps1` (it prints a "Runtime verdict"), or check
Steam → Schedule I → Properties → Betas: *None* = IL2CPP, *alternate* = Mono.

## 2. Install

1. Install MelonLoader 0.7.3 into the Schedule I folder with the official MelonLoader installer
   (https://github.com/LavaGang/MelonLoader/releases). Launch the game **once** and quit. The first
   IL2CPP launch takes a while because MelonLoader generates its interop assemblies.
2. Get the mod files: the CI artifact `TakeItOrLeaveIt-<sha>` from the pull request's *Checks* tab, or a
   local `pwsh tools/Package-Mod.ps1` (output in `artifacts/package`).
3. Open the subfolder matching your runtime (`Il2Cpp` or `Mono`) and copy its **contents** into the game folder:
   - `Mods/TakeItOrLeaveIt.<Runtime>.dll` → `<Schedule I>/Mods/`
   - `UserLibs/TakeItOrLeaveIt.Core.dll` → `<Schedule I>/UserLibs/`

   Install **only one** of the two mod DLLs.

## 3. Verify it loaded

Launch the game and open `<Schedule I>/MelonLoader/Latest.log` (the MelonLoader console window shows the
same lines). Expected lines, in order (timestamps omitted):

```text
[TakeItOrLeaveIt] Initializing...
[TakeItOrLeaveIt] TakeItOrLeaveIt 0.0.1-dev by T-h07 - phase 00 - Foundation; build target: Il2Cpp.
[TakeItOrLeaveIt] Game: 'Schedule I' by 'TVGS', version '<game version>', Unity <unity version>.
[TakeItOrLeaveIt] MelonLoader 0.7.3; game runtime reported by loader: Il2Cpp.
[TakeItOrLeaveIt] Runtime detected from game files: Il2Cpp.
[TakeItOrLeaveIt]   evidence: ...
[TakeItOrLeaveIt] Compatibility: Supported.
[TakeItOrLeaveIt] MelonLoader integration initialized.
[TakeItOrLeaveIt] No gameplay patches enabled in Phase 00.
[TakeItOrLeaveIt] Initialization completed.
[TakeItOrLeaveIt] Version 0.0.1-dev initialized successfully.
[TakeItOrLeaveIt] Scene loaded: '<scene>' (index n). No gameplay changes in Phase 00.
```

(`Mono` replaces `Il2Cpp` on the alternate branch.) Problems are reported as:

- `Unsupported environment during 'Compatibility check': Wrong mod build ...`: you installed the other runtime's DLL.
- `... failed unexpectedly (likely a mod defect ...)`: a bug; send the full log.
- No `[TakeItOrLeaveIt]` lines at all: MelonLoader did not load the mod. Check the MelonLoader lines near
  the top of the log for "incompatible" / "failed to load" messages about `TakeItOrLeaveIt`. The `MelonGame("TVGS", "Schedule I")`
  attribute is unverified against your install, so a mismatch would show up here.

## 4. Logs

- `<Schedule I>/MelonLoader/Latest.log`: the current session
- `<Schedule I>/MelonLoader/Logs/`: previous sessions

## 5. Uninstall

Close the game and delete `<Schedule I>/Mods/TakeItOrLeaveIt.*.dll` and `<Schedule I>/UserLibs/TakeItOrLeaveIt.Core.dll`.
The mod writes no other files and does not touch saves. To remove MelonLoader as well, use its installer's
uninstall option or verify the game files in Steam.

## 6. Known limitations

- In-game loading is untested; the `MelonGame` developer/name strings come from public S1API documentation.
- Loading `TakeItOrLeaveIt.Core.dll` from `UserLibs` follows MelonLoader's documented layout but has not been observed yet.
  For the Mono build, Unity's Mono runtime must load a .NET Standard 2.0 library (normally supported; unverified for this game).
- No gameplay features exist yet (by design for Phase 00).
