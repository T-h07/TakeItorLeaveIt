# Phase 02 testing: in-game mod menu

> **Status: IMPLEMENTATION COMPLETE — PHYSICAL ACCEPTANCE PENDING.**
> Everything below the "Automated tests" section must be run on the gaming PC. Nothing in this document
> has been observed in game yet. Phase 01 physical tests (P01-T03/T05/T21) are also still DEFERRED — NOT YET VERIFIED.

Phase 02 adds a phone app, **Take It**, where you configure which unlocked products may fill orders per
category. **It does not change sales yet**: customers and dealers behave exactly as in vanilla until
Phases 03/04. The menu only edits and saves settings and previews what the rules would allow.

## 1. What you need

| Item | Version | Where it comes from |
| --- | --- | --- |
| Schedule I | v0.4.7f12, IL2CPP (default Steam branch) | The build the references were exported from |
| MelonLoader | The version already installed (the exported `net6` folder contains MelonLoader 0.7.4.2673; 0.7.0+ accepted) | Already installed on the gaming PC |
| S1API (S1API_Forked) | **3.2.3**, file `S1API.Il2Cpp.MelonLoader.dll` | Already installed on the gaming PC (it was in `<game>/Mods`) |
| Take It Or Leave It | this branch, **IL2CPP build made with local references** | See §2 |

The CI artifact on GitHub **does not contain the phone app**: CI has no game or S1API files, so it builds
the mod without the menu. Use the locally built package (§2).

## 2. Install

1. On the development PC, build and package (see [BUILDING.md](BUILDING.md#phase-02-phone-app-local-references)):

   ```powershell
   pwsh tools/Package-Mod.ps1 -Configuration Release
   ```

   The build output must contain the line `TakeItOrLeaveIt: building WITH the S1API phone app`.
2. Copy `artifacts/package/Il2Cpp/` to the gaming PC and copy its **contents** into the game folder:
   - `Mods/TakeItOrLeaveIt.Il2Cpp.dll` → `<Schedule I>/Mods/`
   - `UserLibs/TakeItOrLeaveIt.Core.dll` → `<Schedule I>/UserLibs/`

   Replace older copies. Do not install `TakeItOrLeaveIt.Mono.dll`.
3. Leave S1API installed as it is. Our package contains no S1API, game or loader files.

## 3. Open the menu

1. Launch Schedule I and load a save (the app only appears in the gameplay scene, not the main menu).
2. Open the phone with the game's normal phone key.
3. On the home screen, tap the **Take It** icon (dark square, green circle with a white check mark).
4. Close it with the phone's own back/close controls or by closing the phone.

Expected log lines (`<Schedule I>/MelonLoader/Latest.log`):

```text
[TakeItOrLeaveIt] Mod menu settings initialized (stored per save under UserData/TakeItOrLeaveIt/saves).
[TakeItOrLeaveIt] Phase 02: product discovery, mod menu and per-save settings. Sales are not changed yet (Phases 03/04).
[TakeItOrLeaveIt] S1API detected: the 'Take It' app is available on the in-game phone.
```

If S1API is missing you get `S1API is not installed: the in-game phone menu is unavailable.` and the rest of
the mod keeps working. Any `Phone menu disabled after an error` line is a defect: send the full log.

## 4. Screens

| Screen | Contents |
| --- | --- |
| Top bar (always) | Title, version, status (save, catalog state, *Unsaved changes*, or the last message such as "Settings saved."), **Discard** and **Save** |
| Overview | Mod ON/OFF for this save, active save, catalog status, the Phases 03/04 notice, one card per category (mode, eligible / unlocked counts, warnings). Tap a card to edit it |
| Products | Category tabs; mode buttons *Any Unlocked / Selected Only / Disabled* with a description; search box; *Select all unlocked*, *Select shown*, *Clear*; product list (40 per page) with check boxes and status; pager; hypothetical eligibility preview on the right |
| Settings | Save / Discard, Reset to defaults (asks for confirmation), where settings are stored |
| Diagnostics | Mod, game, Unity, MelonLoader and runtime versions; catalog state and issues |

Before a save is loaded the screens show *Waiting for active save...*; while the catalog loads, *Waiting for
product data...*; if discovery fails, *Unable to load product catalog.* Products is unavailable in those states.

## 5. Acceptance tests

Record **PASS / FAIL / BLOCKED / PENDING** for each. Only mark PASS what you observed.

| ID | Test | Steps | Expected result |
| --- | --- | --- | --- |
| P02-T01 | Feature branch | `git log --oneline origin/develop..feature/phase-02-mod-menu` | Branch based on `develop` |
| P02-T02 | Phase 01 integration | Review | Menu uses `ProductCatalogService` and `SaleEligibilityPolicy`; no duplicate rules |
| P02-T03 | Build | §2 step 1 | 0 warnings, 0 errors, "WITH the S1API phone app" |
| P02-T04 | Dependencies | Launch, read log | S1API loads; "S1API detected" line; no `TakeItOrLeaveIt` load errors |
| P02-T05 | Game launch | Launch, load a save | No crash; no `[TakeItOrLeaveIt]` errors |
| P02-T06 | Menu registration | Open phone home screen | Exactly one **Take It** icon; no S1API errors about the app |
| P02-T07 | Menu opening | Tap the icon | App opens on Overview |
| P02-T08 | Menu closing | Type in search, then close the phone | Movement / hotkeys work again immediately (no stuck typing) |
| P02-T09 | Overview | Look at Overview | Save, catalog *Ready*, one card per discovered category with plausible counts |
| P02-T10 | Global toggle | Toggle OFF, Save, reopen | Shows OFF after reopening and after restart (T23) |
| P02-T11 | Category navigation | Products → tabs | Every discovered category is a tab (Weed, Meth, Cocaine, ... as discovered) |
| P02-T12 | Unlocked product list | Products list | Locked products show "Locked" and cannot be ticked; unlocked can |
| P02-T13 | New product unlock | Create/discover a new product in game, reopen app | New product appears without restarting |
| P02-T14 | Any Unlocked | Choose Any Unlocked | Every unlocked product shows *Eligible*; preview permits them |
| P02-T15 | Selected Only | Choose Selected Only, tick 2 products | Only those 2 are *Eligible*; preview lists only them |
| P02-T16 | Disabled | Choose Disabled | Category shows 0 eligible; preview says no product is permitted |
| P02-T17 | Multiselection | Tick several products | All stay ticked; counts update |
| P02-T18 | Search | Type part of a name, effect or `ID`; clear it | List filters case-insensitively; ticks survive the search |
| P02-T19 | Select/Clear | *Select all unlocked*, *Select shown* (with a search), *Clear* | Act on the current category only |
| P02-T20 | Empty selection | Selected Only with nothing ticked | Warning "No products selected. This category currently has no eligible products." |
| P02-T21 | Policy preview | Change modes/ticks | Preview updates immediately and marks "Showing unsaved changes" |
| P02-T22 | Save settings | Change something, Save | "Settings saved." only then; file appears in `UserData/TakeItOrLeaveIt/saves/` |
| P02-T23 | Restart persistence | Quit game, relaunch, load same save | Same settings shown |
| P02-T24 | Reset defaults | Settings → Reset → Confirm → Save | All categories Any Unlocked, no selections, mod ON |
| P02-T25 | Save isolation | Configure save A, load save B, back to A | B has its own (default) settings; A's settings return |
| P02-T26 | Catalog failure | Hard to force; observe if it happens | Menu shows "Unable to load product catalog.", saved file untouched |
| P02-T27 | Large catalog | Automated (see §6); in game, scroll and search your list | Usable, no visible stalls |
| P02-T28 | Scene transitions | Load a save, return to main menu, load again (2-3 times) | Still one icon; no invisible blocker; clicks work |
| P02-T29 | Customer regression | Complete a normal customer deal | Vanilla behaviour (exact product still required) |
| P02-T30 | Dealer regression | Let a dealer complete a deal | Vanilla behaviour |
| P02-T31 | Automated tests | `dotnet test` | All pass |
| P02-T32 | GitHub Actions | PR checks | Green |
| P02-T33 | Documentation | Review | README, MOD_MENU_SPEC, ARCHITECTURE, BUILDING, INSTALLATION, this file |
| P02-T34 | Pull request | GitHub | PR from `feature/phase-02-mod-menu` targets `develop` |

## 6. Automated tests

`dotnet test TakeItOrLeaveIt.sln -c Release --no-build` runs 210 tests (no game needed), including
menu navigation, selection, mode, search, preview, lifecycle, persistence (atomic write, unreadable file,
failed write, save isolation) and synthetic catalogs of 10/100/500/1000 products. Controller timings on the
development PC were at most ~5 ms per operation for 1000 products (UI build cost in game is not measured;
the list is paged at 40 rows to bound it).

## 7. Evidence to send back

1. `MelonLoader/Latest.log` from a session where you opened the app, saved, and switched saves.
2. Screenshots: phone home screen with the icon; Overview; Products in Selected Only with a warning; Settings with the reset confirmation; Diagnostics.
3. The JSON file(s) from `<Schedule I>/UserData/TakeItOrLeaveIt/saves/`.
4. The PASS/FAIL/BLOCKED table above.

## 8. Known limitations

- The phone app exists only in IL2CPP builds made with local references. The Mono build and the CI artifact have no menu.
- S1API creates the app on every phone home-screen start; S1API itself does not deduplicate icons. P02-T28 checks this.
- No gamepad navigation was designed; mouse and keyboard only.
- Rules are saved but **not applied** to sales until Phases 03/04. Customer/dealer options, presets and quality/effect rules are later phases.
- Multiplayer: settings are local to each player's machine; host authority is Phase 07.
