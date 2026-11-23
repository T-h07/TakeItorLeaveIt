# Installation (development build 0.0.1-dev)

> Phase 02 build: it discovers products and adds the **Take It** phone app where you configure per-save
> product rules. It does **not** change customers, dealers, products, money, XP or game saves; the rules are
> applied to sales only from Phases 03/04. Phase 00 loading was verified on Schedule I v0.4.7f12 (IL2CPP);
> Phase 01/02 in-game steps are in [PHASE_01_TESTING.md](PHASE_01_TESTING.md) and [PHASE_02_TESTING.md](PHASE_02_TESTING.md).

## 1. Compatibility

| Item | Requirement | Status |
| --- | --- | --- |
| Game | Schedule I (Steam app 3164500), Windows | Game build not yet recorded; fill in from the probe |
| Runtime | IL2CPP (default branch) → `Il2Cpp` build · Mono (`alternate` beta) → `Mono` build | Both builds compile; neither has been loaded in-game yet |
| Loader | **MelonLoader 0.7.3** recommended (0.7.0+ accepted with a warning; avoid 0.7.1 on IL2CPP) | Built against 0.7.3 |
| S1API (S1API_Forked) | **3.2.3** (`S1API.Il2Cpp.MelonLoader.dll`), needed only for the phone menu. Without it the mod runs without a menu | Compiled against 3.2.3.0; in-game loading not yet observed |

Not sure which branch you have? Run `tools/Probe-ScheduleOne.ps1` (it prints a "Runtime verdict"), or check
Steam → Schedule I → Properties → Betas: *None* = IL2CPP, *alternate* = Mono.

## 2. Install

1. Install MelonLoader 0.7.3 into the Schedule I folder with the official MelonLoader installer
   (https://github.com/LavaGang/MelonLoader/releases). Launch the game **once** and quit. The first
   IL2CPP launch takes a while because MelonLoader generates its interop assemblies.
2. Install S1API 3.2.3 following its own instructions if it is not installed yet (only needed for the menu).
3. Get the mod files. **For the phone menu use a local IL2CPP package** built with references
   (`pwsh tools/Package-Mod.ps1`, output in `artifacts/package`; see [BUILDING.md](BUILDING.md#phase-02-phone-app-local-references)).
   The CI artifact `TakeItOrLeaveIt-<sha>` (pull request *Checks* tab) is built without game files and has **no menu**.
4. Open the subfolder matching your runtime (`Il2Cpp` or `Mono`) and copy its **contents** into the game folder:
   - `Mods/TakeItOrLeaveIt.<Runtime>.dll` → `<Schedule I>/Mods/`
   - `UserLibs/TakeItOrLeaveIt.Core.dll` → `<Schedule I>/UserLibs/`

   Install **only one** of the two mod DLLs.

## 3. Verify it loaded

Launch the game and open `<Schedule I>/MelonLoader/Latest.log` (the MelonLoader console window shows the
same lines). Expected lines, in order (timestamps omitted):

```text
[TakeItOrLeaveIt] Initializing...
[TakeItOrLeaveIt] TakeItOrLeaveIt 0.0.1-dev by T-h07 - phase 02 - In-game mod menu; build target: Il2Cpp.
[TakeItOrLeaveIt] Game: 'Schedule I' by 'TVGS', version '<game version>', Unity <unity version>.
[TakeItOrLeaveIt] MelonLoader 0.7.3; game runtime reported by loader: Il2Cpp.
[TakeItOrLeaveIt] Runtime detected from game files: Il2Cpp.
[TakeItOrLeaveIt]   evidence: ...
[TakeItOrLeaveIt] Compatibility: Supported.
[TakeItOrLeaveIt] MelonLoader integration initialized.
[TakeItOrLeaveIt] No gameplay patches enabled (sales are not changed until Phases 03/04).
[TakeItOrLeaveIt] Product discovery initialized (read-only; polling save state about once per second).
[TakeItOrLeaveIt] Mod menu settings initialized (stored per save under UserData/TakeItOrLeaveIt/saves).
[TakeItOrLeaveIt] Initialization completed.
[TakeItOrLeaveIt] Version 0.0.1-dev initialized successfully.
[TakeItOrLeaveIt] Phase 02: product discovery, mod menu and per-save settings. Sales are not changed yet (Phases 03/04).
[TakeItOrLeaveIt] S1API detected: the 'Take It' app is available on the in-game phone.
[TakeItOrLeaveIt] Scene loaded: '<scene>' (index n). No gameplay changes in Phase 02.

After loading a save, a `Product catalog: Ready` block lists the discovered products
(see [PHASE_01_TESTING.md](PHASE_01_TESTING.md)).
```

(`Mono` replaces `Il2Cpp` on the alternate branch.) Problems are reported as:

- `Unsupported environment during 'Compatibility check': Wrong mod build ...`: you installed the other runtime's DLL.
- `... failed unexpectedly (likely a mod defect ...)`: a bug; send the full log.
- No `[TakeItOrLeaveIt]` lines at all: MelonLoader did not load the mod. Check the MelonLoader lines near
  the top of the log for "incompatible" / "failed to load" messages about `TakeItOrLeaveIt`. The `MelonGame("TVGS", "Schedule I")`
  attribute is unverified against your install, so a mismatch would show up here.

## 4. Open the menu

Load a save, open the in-game phone and tap **Take It** (green circle with a check mark). The app only
exists in the gameplay scene. Screens: Overview, Products, Settings, Diagnostics; changes take effect only
after **Save**. Full walkthrough: [PHASE_02_TESTING.md](PHASE_02_TESTING.md).

Settings are stored per save in `<Schedule I>/UserData/TakeItOrLeaveIt/saves/<save-id>.json`. Game save
files are never modified.

If the log says `S1API is not installed` or `This build does not include the in-game phone menu`, install
S1API or use a locally built IL2CPP package respectively.

## 5. Logs

- `<Schedule I>/MelonLoader/Latest.log`: the current session
- `<Schedule I>/MelonLoader/Logs/`: previous sessions

## 6. Uninstall

Close the game and delete `<Schedule I>/Mods/TakeItOrLeaveIt.*.dll` and `<Schedule I>/UserLibs/TakeItOrLeaveIt.Core.dll`.
Optionally delete `<Schedule I>/UserData/TakeItOrLeaveIt/` (menu settings). The mod writes no other files and does not touch game saves. To remove MelonLoader as well, use its installer's
uninstall option or verify the game files in Steam.

## 7. Known limitations

- In-game loading is untested; the `MelonGame` developer/name strings come from public S1API documentation.
- Loading `TakeItOrLeaveIt.Core.dll` from `UserLibs` follows MelonLoader's documented layout but has not been observed yet.
  For the Mono build, Unity's Mono runtime must load a .NET Standard 2.0 library (normally supported; unverified for this game).
- No gameplay features exist yet: the menu saves rules, but sales use them only from Phases 03/04.
- The phone menu is IL2CPP-only and needs S1API 3.2.3; the Mono build has no menu.
