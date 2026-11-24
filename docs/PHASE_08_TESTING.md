# Phase 08 — master acceptance tracker

> **Every physical scenario is NOT TESTED until the owner reports a result from the game.** Results are recorded
> exactly as reported, with the RC id and evidence path. A FAIL is never overwritten by a later PASS: the retest gets
> its own entry in the *Retest* column and the earlier result is marked *superseded by rc.N*.

All scenario IDs from Phases 01–07 are kept. IDs that two phases used for different things are prefixed with their
phase (`P04:PR01` = fair pricing, `P06:PR01` = per-save profiles). New Phase 08 scenarios are `P08-…`. Setup, actions
and expected results are copied from the phase documents, which stay the detailed reference.

**Reuses** names an equivalent scenario: running that one and recording its evidence also answers this row (record
"PASS via <ID>" with the same evidence). Nothing is marked passed without its own row being filled in.

Status values: **PASS / FAIL / BLOCKED / NOT TESTED**. BLOCKED needs a reason and a release disposition in
[PHASE_08_RELEASE_BLOCKERS.md](PHASE_08_RELEASE_BLOCKERS.md).

## 1. Release candidates

| RC | Source commit | `Mods/TakeItOrLeaveIt.Il2Cpp.dll` SHA256 | `UserLibs/TakeItOrLeaveIt.Core.dll` SHA256 | ZIP | ZIP SHA256 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| **1.0.0-rc.1** | `843bb1a5c37a1de78f72f6ba85458bc67bc8383e` | `FF80C72C39F49C2663D6BCC815504F48FA3AFAB0675C8DE0D4F7FD8851B08C79` (125,952 B) | `8794B5E7D95F7964B2A0A57A3CDF2F5237F73694E774DA2D4FF2BC5CC43A7AAF` (302,592 B) | `artifacts/TakeItOrLeaveIt-1.0.0-rc.1-Il2Cpp.zip` (180,230 B) | `FAD92883E4D283EE1C4057152D2B8D2ACB707D6AC6534AE8B2541DABD40B5F02` | Superseded by rc.2 (no results reported on rc.1; its DLLs contain a build path, B-19: do not share) |
| **1.0.0-rc.2** | `0068d86df8ede828c11ab20e891c861167b3ad05` (Phase 08 hardening, B-16 … B-21; version set in `0c3228b`) | `EFD3C1353C69A6522116DCA565A3E3EA1D62476DADDD1311981F5B648F994228` (130,048 B) | `FC99196E5DD5884EB23140B961CAB45A69158794BE49EA04B1D146AFE8BCD9B8` (309,760 B) | `artifacts/TakeItOrLeaveIt-1.0.0-rc.2-Il2Cpp.zip` (185,147 B) | `5BA8100B33F63131F7AAD316884110C33AE56D4E6CF3F86CD3E9B3D519DAB7F1` | In test (Batch 01) |

Build command (development PC, from a **clean checkout** of the source commit, `local.build.props` pointing at the local
reference export). A clean checkout matters: Git's line-ending conversion is part of the input, because source checksums
end up in the DLL's debug record. Two clean builds of rc.2 gave identical DLL hashes; the ZIP hash differs per packing.

```powershell
dotnet build TakeItOrLeaveIt.sln -c Release --no-incremental
```

```powershell
pwsh -NoProfile -File tools/Package-Mod.ps1 -Configuration Release -NoBuild
```

The build output must contain `building WITH the S1API phone app` and `building WITH the player and dealer sales
game hooks`. The ZIP is made from `artifacts/package/Il2Cpp` and holds exactly the two files above (checked
programmatically; extracted copies match the packaged hashes). **Never test or release the CI artifact**
(`TakeItOrLeaveIt-CI-no-game-hooks-<sha>`): it has no phone app and no sales hooks.

## 2. Test environment (fill in from Batch 01)

| Item | Expected | Recorded (source) |
| --- | --- | --- |
| Game version | 0.4.7f12 | `Game: 'Schedule I' by '…', version '…'` line |
| Runtime | IL2CPP | `game runtime reported by loader:` line |
| Unity version | — | Same `Game:` line |
| MelonLoader | 0.7.3 recommended | `MelonLoader …` line |
| S1API (S1API_Forked) | 3.2.3 | MelonLoader's mod list at the top of `Latest.log` |
| Mod | 1.0.0-rc.2 | `TakeItOrLeaveIt 1.0.0-rc.2 by T-h07 - phase 08 …` line and Diagnostics |
| Harmony modules / methods | 3 / 13 | `Harmony patches active: … method(s) across … module(s).` |
| Windows / GPU / window size | — | Owner |
| Installed DLL hashes | as §1 | `Get-FileHash` output (step 1.6) |

## 3. Batch 01 instructions (RC2)

Goal: prove that RC2 installs, starts cleanly, applies exactly its three patch modules, and that the **Take It**
phone app is fully usable. **No sales in this batch.** If startup is unstable, stop and send the log; nothing later is
tested on an unstable start.

### 3.1 Backup (5 min)

1. Close Schedule I.
2. Copy your whole Schedule I save folder somewhere outside the game (normally
   `%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Saves`).
3. Copy `<Schedule I>\UserData\TakeItOrLeaveIt\` (if it exists) somewhere safe.
4. In the game, make a **copy** of a save to test with (or copy a save slot folder), and only load that copy.

### 3.2 Install RC2

1. Unzip `TakeItOrLeaveIt-1.0.0-rc.2-Il2Cpp.zip` **into the Schedule I folder** (Steam → Schedule I → Manage →
   Browse local files). Replace both files when asked:
   - `Mods\TakeItOrLeaveIt.Il2Cpp.dll`
   - `UserLibs\TakeItOrLeaveIt.Core.dll`
2. Make sure there is no other `TakeItOrLeaveIt*.dll` in `Mods\` (for example an old `TakeItOrLeaveIt.Mono.dll`).
3. In PowerShell, record the installed hashes (P08-ENV01 evidence):

```powershell
Get-FileHash "$((Get-Location).Path)\Mods\TakeItOrLeaveIt.Il2Cpp.dll","$((Get-Location).Path)\UserLibs\TakeItOrLeaveIt.Core.dll"
```

   (Run it from the Schedule I folder.) Both hashes must equal §1.

### 3.3 Startup (P05:IN01, P08-ENV01, P08-ENV02, P02-T04, P02-T05)

1. Launch Schedule I from Steam, wait for the main menu, load the **test copy**.
2. Open `<Schedule I>\MelonLoader\Latest.log` and find these lines (order may vary):

```text
[TakeItOrLeaveIt] TakeItOrLeaveIt 1.0.0-rc.2 by T-h07 - phase 08 - Release stabilization; build target: Il2Cpp.
[TakeItOrLeaveIt] Game: 'Schedule I' by '…', version '0.4.7f12', Unity ….
[TakeItOrLeaveIt] MelonLoader 0.7.3; game runtime reported by loader: Il2Cpp.
[TakeItOrLeaveIt] Compatibility: Supported; game build: BuildVerified.
[TakeItOrLeaveIt] Patch module applied: Player sales (category orders)
[TakeItOrLeaveIt] Patch module applied: Dealer sales (category appointments)
[TakeItOrLeaveIt] Patch module applied: Customer offers (policy filter)
[TakeItOrLeaveIt] Harmony patches active: 13 method(s) across 3 module(s).
[TakeItOrLeaveIt] Version 1.0.0-rc.2 initialized successfully.
[TakeItOrLeaveIt] S1API detected: the 'Take It' app is available on the in-game phone.
[TakeItOrLeaveIt] Take It session: SinglePlayerHost: Single player host.
```

   - `SupportedWithWarnings` is acceptable only if the warning is about the MelonLoader version (0.7.4 ≠ 0.7.3);
     copy every `Compatibility:` line.
   - **Stop and send the log** if you see `Patch module '…' failed`, `Initialization stopped`, any `Exception` from
     `TakeItOrLeaveIt`, or a method count other than 13.
3. After the save loads, the log also shows a `Product catalog: Ready` block. Leave the game running.

### 3.4 Phone app (P02-T06–T28 except T10/T22–T25, P05:IN02, P05:IN03, P05:UI01–UI04, P06:DG01, P08-UI01, P08-UI02)

Use the game window size you normally play at. For every screen take one screenshot.

| Step | Do | Check | IDs |
| --- | --- | --- | --- |
| 1 | Open the phone, look at the home screen | Exactly **one** Take It icon | P02-T06 |
| 2 | Tap it | Opens on **Overview**; status bar shows save and catalog Ready | P02-T07, P02-T09 |
| 3 | Tap each nav button: Overview, Products, Rules, Sales, Presets, Settings, Diagnostics | Each opens; nothing clipped or overlapping; text readable | P05:IN02, P05:UI01 |
| 4 | Products: tap each category tab | Every discovered category is a tab | P02-T11 |
| 5 | Products, Weed: look at the rows | Locked products say *Locked* and cannot be ticked | P02-T12 |
| 6 | Mode **Any Unlocked** | Every unlocked product *Permitted*; preview agrees | P02-T14 |
| 7 | Mode **Selected Only**, tick 2 products | Only those 2 permitted; counts update; preview says *Showing unsaved changes* | P02-T15, P02-T17, P02-T21 |
| 8 | Untick both | Warning *No products selected…* | P02-T20 |
| 9 | Mode **Disabled** | 0 eligible; preview says nothing permitted | P02-T16 |
| 10 | Mode **Blocklist** | Four mode buttons visible; *Block all unlocked* / *Block shown* | P05:UI02 |
| 11 | Back to Selected Only; type part of a product name in search, then an effect, then clear | List filters; ticks survive; **the player does not move while typing** | P02-T18, P05:UI04 |
| 12 | *Select all unlocked*, *Select shown* (with a search), *Clear* | Act on this category only | P02-T19 |
| 13 | Scroll the product list and the preview with the mouse wheel | Scrolls; check boxes still respond | P02-T27 |
| 14 | Rules screen: scroll the effects list | Scrolls; buttons usable | P05:UI03 |
| 15 | Diagnostics | Version **1.0.0-rc.2**; *Session and sales (Phase 07)* heading; *Rule engine* lines with schema 2, lineage count; *Save data* line; **no folder paths** | P05:IN03, P06:DG01 |
| 16 | Press **Discard** | Every change from steps 6–12 is gone; *Unsaved changes* disappears | (Discard check) |
| 17 | Close the phone (while the search box has focus once) | Movement and hotkeys work immediately | P02-T08 |
| 18 | Open and close the app 10 times, visiting a different screen each time | Still one icon; no duplicate panels; no new errors in the log | P08-UI01 |
| 19 | Quit to the main menu, load the test save again; repeat 3 times | One icon each time; app works; clicks are not blocked | P02-T28 |
| 20 | Switch the game to its smallest practical window size (Settings → Display) and repeat step 3 | Same checks; log `Layout check <screen> (…): OK` for every screen | P08-UI02 |
| 21 | Discover/create one new product if convenient (optional), reopen Products | New product listed without restart | P02-T13 |

**Do not press Save in this batch** (saving and persistence are Batch 02/08), except that pressing Save once with no
changes is harmless.

### 3.4a Log privacy (P08-HD01, new in rc.2)

Open `Latest.log` in a text editor, search for your Windows user name and for `C:\Users`. No line starting with
`[TakeItOrLeaveIt]` may contain either (lines from MelonLoader itself or other mods do not count).

### 3.5 Send back

1. `MelonLoader\Latest.log` from this session (it contains no folder paths written by the mod).
2. Screenshots: home screen icon; each of the 7 screens at normal size; Products in Selected Only with the warning;
   Diagnostics; one screen at the small window size.
3. The `Get-FileHash` output.
4. A list `ID: PASS/FAIL/BLOCKED – note` for: P05:IN01, P08-ENV01, P08-ENV02, P02-T04 … P02-T28 (the ones in the
   table), P05:IN02, P05:IN03, P05:UI01–UI04, P06:DG01, P08-UI01, P08-UI02, P08-HD01. Note anything odd even if it is not on
   the list (stutter, a red log line, text that looks wrong).

Afterwards you can keep RC2 installed; Batch 02 continues on the same build unless a fix is needed.

## 4. Test order

| Batch | Contents | Needs |
| --- | --- | --- |
| 01 | Startup and phone app | Any save copy |
| 02 | Product discovery and policies | Save with base strains and mixes (mixes of mixes if possible) |
| 03 | Basic player sale | ≥ 2 unlocked Weed, 1 other category |
| 04 | Basic dealer sale | ≥ 1 hired dealer with customers |
| 05 | Offers and economy (incl. the Sour Diesel / OG Kush exploit) | Strains with different prices |
| 06 | Quality, effects, mixing, switches, overrides | Two quality tiers |
| 07 | Dealer reservations, strict fulfilment, empty handover, quality lots, restock texts | Dealer, mixed-quality stock |
| 08 | Save, reload and recovery | Two saves; file edits with the game closed (disposable copies only) |
| 09 | Co-op and compatibility | Two PCs / Steam accounts |
| 10 | Endurance and regression | Long session |
| 11 | Final installation-package smoke test | Fresh game folder or clean reinstall |

Batch instructions for 02–11 are written when the previous batch's results are in, so they use the build actually under
test.

## 5. Tracker

### Batch 01 — Startup and phone app (41 scenarios; config: SP, disposable save copy)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P01-T02 | Discovery | — | PHASE_01_TESTING.md §2.2 startup log | Existing foundation |  | NOT TESTED |  |  |  |  |  | P05:IN01 |
| P02-T04 | Phone app | Dependencies | Launch, read log | S1API loads; "S1API detected" line; no `TakeItOrLeaveIt` load errors |  | NOT TESTED |  |  |  |  |  | P05:IN01 |
| P02-T05 | Phone app | Game launch | Launch, load a save | No crash; no `[TakeItOrLeaveIt]` errors |  | NOT TESTED |  |  |  |  |  | P05:IN01 |
| P02-T06 | Phone app | Menu registration | Open phone home screen | Exactly one **Take It** icon; no S1API errors about the app |  | NOT TESTED |  |  |  |  |  | P02-T28 |
| P02-T07 | Phone app | Menu opening | Tap the icon | App opens on Overview |  | NOT TESTED |  |  |  |  |  |  |
| P02-T08 | Phone app | Menu closing | Type in search, then close the phone | Movement / hotkeys work again immediately (no stuck typing) |  | NOT TESTED |  |  |  |  |  | P05:UI04 |
| P02-T09 | Phone app | Overview | Look at Overview | Save, catalog *Ready*, one card per discovered category with plausible counts |  | NOT TESTED |  |  |  |  |  |  |
| P02-T10 | Phone app | Global toggle | Toggle OFF, Save, reopen | Shows OFF after reopening and after restart (T23) |  | NOT TESTED |  |  |  |  |  | P03:S12 |
| P02-T11 | Phone app | Category navigation | Products → tabs | Every discovered category is a tab (Weed, Meth, Cocaine, ... as discovered) |  | NOT TESTED |  |  |  |  |  |  |
| P02-T12 | Phone app | Unlocked product list | Products list | Locked products show "Locked" and cannot be ticked; unlocked can |  | NOT TESTED |  |  |  |  |  |  |
| P02-T13 | Phone app | New product unlock | Create/discover a new product in game, reopen app | New product appears without restarting |  | NOT TESTED |  |  |  |  |  |  |
| P02-T14 | Phone app | Any Unlocked | Choose Any Unlocked | Every unlocked product shows *Eligible*; preview permits them |  | NOT TESTED |  |  |  |  |  |  |
| P02-T15 | Phone app | Selected Only | Choose Selected Only, tick 2 products | Only those 2 are *Eligible*; preview lists only them |  | NOT TESTED |  |  |  |  |  |  |
| P02-T16 | Phone app | Disabled | Choose Disabled | Category shows 0 eligible; preview says no product is permitted |  | NOT TESTED |  |  |  |  |  |  |
| P02-T17 | Phone app | Multiselection | Tick several products | All stay ticked; counts update |  | NOT TESTED |  |  |  |  |  |  |
| P02-T18 | Phone app | Search | Type part of a name, effect or `ID`; clear it | List filters case-insensitively; ticks survive the search |  | NOT TESTED |  |  |  |  |  |  |
| P02-T19 | Phone app | Select/Clear | *Select all unlocked*, *Select shown* (with a search), *Clear* | Act on the current category only |  | NOT TESTED |  |  |  |  |  |  |
| P02-T20 | Phone app | Empty selection | Selected Only with nothing ticked | Warning "No products selected. This category currently has no eligible products." |  | NOT TESTED |  |  |  |  |  |  |
| P02-T21 | Phone app | Policy preview | Change modes/ticks | Preview updates immediately and marks "Showing unsaved changes" |  | NOT TESTED |  |  |  |  |  |  |
| P02-T22 | Phone app | Save settings | Change something, Save | "Settings saved." only then; file appears in `UserData/TakeItOrLeaveIt/saves/` |  | NOT TESTED |  |  |  |  |  |  |
| P02-T24 | Phone app | Reset defaults | Settings → Reset → Confirm → Save | All categories Any Unlocked, no selections, mod ON |  | NOT TESTED |  |  |  |  |  |  |
| P02-T26 | Phone app | Catalog failure | Hard to force; observe if it happens | Menu shows "Unable to load product catalog.", saved file untouched |  | NOT TESTED |  |  |  |  |  |  |
| P02-T27 | Phone app | Large catalog | Automated (see §6); in game, scroll and search your list | Usable, no visible stalls |  | NOT TESTED |  |  |  |  |  |  |
| P02-T28 | Phone app | Scene transitions | Load a save, return to main menu, load again (2-3 times) | Still one icon; no invisible blocker; clicks work |  | NOT TESTED |  |  |  |  |  |  |
| P02-T29 | Phone app | Customer regression | Complete a normal customer deal | Vanilla behaviour (exact product still required) |  | NOT TESTED |  |  |  |  |  | P03:S12 |
| P02-T30 | Phone app | Dealer regression | Let a dealer complete a deal | Vanilla behaviour |  | NOT TESTED |  |  |  |  |  | P04:DS15 |
| P03-T05 | Player sales | — | Per PHASE_03_TESTING.md | Game build loads the mod |  | NOT TESTED |  |  |  |  |  | P05:IN01 |
| P03-T06 | Player sales | — | Per PHASE_03_TESTING.md | Mod menu still operates |  | NOT TESTED |  |  |  |  |  | P02-T07 |
| P05:IN01 | Startup | Phase 05 build installed | Launch, load a save | Phase 04 startup lines; 13 patched methods; no `failed` line |  | NOT TESTED |  |  |  |  |  |  |
| P05:IN02 | Startup | Loaded save | Open Take It | Nav shows Overview, Products, Rules, Sales, Presets, Settings, Diagnostics |  | NOT TESTED |  |  |  |  |  | P02-T07 |
| P05:IN03 | Startup | Loaded save | Diagnostics | *Rule engine (Phase 05)* lines with schema 2, preset, scopes, lineage count |  | NOT TESTED |  |  |  |  |  |  |
| P05:UI01 | Phone app | Smallest window you use | Visit every screen | `Layout check <screen> ...: OK` for every screen; nothing clipped or overlapping |  | NOT TESTED |  |  |  |  |  |  |
| P05:UI02 | Phone app | Products, Blocklist mode | Header | Four mode buttons visible; *Block all unlocked* / *Block shown* labels |  | NOT TESTED |  |  |  |  |  |  |
| P05:UI03 | Phone app | Rules screen with many effects | Scroll | List scrolls; buttons usable |  | NOT TESTED |  |  |  |  |  |  |
| P05:UI04 | Phone app | Search field focused | Type | Typing does not move the player (Phase 02 behaviour) |  | NOT TESTED |  |  |  |  |  |  |
| P06:DG01 | Diagnostics | — | Open Diagnostics in each state above | "Save data" and "Runtime files" lines match the state; no raw folder paths shown or logged |  | NOT TESTED |  |  |  |  |  |  |
| P08-ENV01 | Startup | Batch 1 install | Record game version (main menu corner), Unity, MelonLoader, S1API, mod version from Latest.log + Diagnostics | All values recorded in the tracker header; mod version = the RC under test |  | NOT TESTED |  |  |  |  |  |  |
| P08-ENV02 | Startup | Batch 1 install | Count `Patch module applied` lines and the `Harmony patches active` line | 13 method(s) across 3 module(s); no `failed` line |  | NOT TESTED |  |  |  |  |  |  |
| P08-UI01 | Phone app | Loaded save | Open/close the Take It app 10 times, switching screens each time | Stable; one icon; no duplicated UI; no errors |  | NOT TESTED |  |  |  |  |  |  |
| P08-UI02 | Phone app | Smallest practical window and normal resolution | Visit all 7 screens at both sizes | No clipping/overlap; `Layout check ... OK` for every screen |  | NOT TESTED |  |  |  |  |  |  |
| P08-HD01 | Log privacy (rc.2) | Batch 1 session | Search `Latest.log` for your Windows user name and for `C:\Users` | No `[TakeItOrLeaveIt]` line contains either; file names alone are fine |  | NOT TESTED |  |  |  |  |  |  |

### Batch 02 — Product discovery and policies (22 scenarios; config: SP, save with base strain + mixes)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P01-T03 | Discovery | — | PHASE_01_TESTING.md §2.3 discovery log | Category discovery |  | NOT TESTED |  |  |  |  |  |  |
| P01-T04 | Discovery | — | PHASE_01_TESTING.md §2.3 real IDs | Product identity |  | NOT TESTED |  |  |  |  |  |  |
| P01-T05 | Discovery | — | PHASE_01_TESTING.md §2.3 unlocks | Unlock detection |  | NOT TESTED |  |  |  |  |  |  |
| P01-T07 | Discovery | — | PHASE_01_TESTING.md §2.4 refresh | Catalog refresh |  | NOT TESTED |  |  |  |  |  |  |
| P01-T08 | Discovery | — | PHASE_01_TESTING.md §2.5 save isolation | Save isolation |  | NOT TESTED |  |  |  |  |  | P06:PR01 |
| P05:BS01 | Base strain | Any settings | Products → Weed | Mix rows show *from <base strain>*; base strains show *base strain* |  | NOT TESTED |  |  |  |  |  |  |
| P05:BS02 | Base strain | Weed = Selected Only, *Base strain*, base strain ticked | Products list | The base strain, its mixes and mixes of mixes show *Permitted*; other lineages *Not selected* |  | NOT TESTED |  |  |  |  |  |  |
| P05:BS03 | Base strain | Same | Accept a Weed order, offer a mix of a mix | Accepted |  | NOT TESTED |  |  |  |  |  |  |
| P05:BS04 | Base strain | Same | Offer a product of another lineage | Refused: *This product isn't from a strain on your Marijuana allowed list.* (the game's category name) |  | NOT TESTED |  |  |  |  |  |  |
| P05:BS05 | Base strain | A mix with a name similar to a base strain but not made from it (if available) | Check its row | Lineage follows the recipe, not the name |  | NOT TESTED |  |  |  |  |  |  |
| P05:MG01 | Migration | A Phase 04 settings file with Weed = Selected Only (two products ticked), mixed OFF | Load the save, open Products | Same mode and ticks; Rules screen shows *Exact recipe*; Sales shows *One product per order* |  | NOT TESTED |  |  |  |  |  |  |
| P05:MG02 | Migration | After MG01 | Change nothing, accept a Weed order | Same products permitted as in Phase 04 (mixes of ticked strains NOT added) |  | NOT TESTED |  |  |  |  |  |  |
| P05:MG03 | Migration | After MG01 | Change any setting, Save | `<save id>.json.schema1.bak` appears next to the settings file and equals the old file; the new file says `"schemaVersion": 2` |  | NOT TESTED |  |  |  |  |  |  |
| P05:MG04 | Migration | After MG03 | Save again | The `.schema1.bak` file is not changed |  | NOT TESTED |  |  |  |  |  | P05:MG03 |
| P05:PS01 | Presets | Defaults | Presets screen | *Current rules: Anything Goes* |  | NOT TESTED |  |  |  |  |  |  |
| P05:PS02 | Presets | Any | *Preview changes* on Vanilla Preferences | A list of changes and a warning; nothing applied yet |  | NOT TESTED |  |  |  |  |  |  |
| P05:PS03 | Presets | After PS02 | *Apply to draft*, then Discard | Draft back to the saved rules |  | NOT TESTED |  |  |  |  |  |  |
| P05:PS04 | Presets | Weed list with one product, Meth list empty | Apply *Selected Products Only*, Save | Weed uses its list; preview warned that Meth has nothing listed |  | NOT TESTED |  |  |  |  |  |  |
| P05:PS05 | Presets | After a preset | Change one rule | Label shows *Custom* |  | NOT TESTED |  |  |  |  |  |  |
| P05:PS06 | Presets | Vanilla Preferences saved | Reload the save | Still *Vanilla Preferences*; orders vanilla |  | NOT TESTED |  |  |  |  |  |  |
| P05:UA01 | Unknown lineage | Diagnostics shows some products with unknown lineage (otherwise BLOCKED) | Products → that product | Row says *lineage unknown* |  | NOT TESTED |  |  |  |  |  |  |
| P05:UA02 | Unknown lineage | Blocklist by base strain with anything blocked | Check that product | *Lineage unknown*, not permitted |  | NOT TESTED |  |  |  |  |  |  |

### Batch 03 — Basic player sale (38 scenarios; config: SP, ≥2 unlocked Weed + 1 other category)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P03-T04 | Player sales | — | Per PHASE_03_TESTING.md: exists in the installed build; runtime behaviour unobserved | Handover acceptance hook verified |  | NOT TESTED |  |  |  |  |  |  |
| P03-T07 | Player sales | — | Per PHASE_03_TESTING.md: S12 | Global enable/disable works |  | NOT TESTED |  |  |  |  |  | P03:S12 |
| P03-T08 | Player sales | — | Per PHASE_03_TESTING.md: §3; phone message limitation documented | Category-based offer displayed correctly |  | NOT TESTED |  |  |  |  |  |  |
| P03-T09 | Player sales | — | Per PHASE_03_TESTING.md: S12, contracts accepted before installing | Existing vanilla contracts remain safe |  | NOT TESTED |  |  |  |  |  |  |
| P03-T10 | Player sales | — | Per PHASE_03_TESTING.md: S1 | One eligible product fulfils a valid order |  | NOT TESTED |  |  |  |  |  | P03:S1 |
| P03-T11 | Player sales | — | Per PHASE_03_TESTING.md: S2 | Two eligible products fulfil a mixed order |  | NOT TESTED |  |  |  |  |  | P03:S2 |
| P03-T12 | Player sales | — | Per PHASE_03_TESTING.md: S3 | Mixed-stack-disabled behaviour |  | NOT TESTED |  |  |  |  |  | P03:S3 |
| P03-T13 | Player sales | — | Per PHASE_03_TESTING.md: S4 | Wrong category rejected |  | NOT TESTED |  |  |  |  |  | P03:S4 |
| P03-T14 | Player sales | — | Per PHASE_03_TESTING.md: S5 | Unselected product rejected |  | NOT TESTED |  |  |  |  |  | P03:S5 |
| P03-T15 | Player sales | — | Per PHASE_03_TESTING.md: automated only; hard to force in game | Locked or unknown product rejected |  | NOT TESTED |  |  |  |  |  |  |
| P03-T16 | Player sales | — | Per PHASE_03_TESTING.md: S6 | Insufficient quantity rejected |  | NOT TESTED |  |  |  |  |  | P03:S6 |
| P03-T17 | Player sales | — | Per PHASE_03_TESTING.md: S7 | Excess quantity causes no excess consumption |  | NOT TESTED |  |  |  |  |  | P03:S7 |
| P03-T18 | Player sales | — | Per PHASE_03_TESTING.md | Successful delivery consumes exactly the required items |  | NOT TESTED |  |  |  |  |  |  |
| P03-T19 | Player sales | — | Per PHASE_03_TESTING.md | Rejected handover preserves inventory |  | NOT TESTED |  |  |  |  |  |  |
| P03-T20 | Player sales | — | Per PHASE_03_TESTING.md | Payment credited exactly once |  | NOT TESTED |  |  |  |  |  |  |
| P03-T21 | Player sales | — | Per PHASE_03_TESTING.md | XP awarded correctly and once |  | NOT TESTED |  |  |  |  |  |  |
| P03-T22 | Player sales | — | Per PHASE_03_TESTING.md | Relationship progression correct |  | NOT TESTED |  |  |  |  |  |  |
| P03-T23 | Player sales | — | Per PHASE_03_TESTING.md: compare satisfaction popup of S1 with an exact-product sale of the same quality | Allowed recipe mismatch has no mismatch-only penalty |  | NOT TESTED |  |  |  |  |  |  |
| P03-T24 | Player sales | — | Per PHASE_03_TESTING.md: S9 | Repeated Submit cannot duplicate |  | NOT TESTED |  |  |  |  |  | P03:S9 |
| P03-T25 | Player sales | — | Per PHASE_03_TESTING.md: S10, S11 | Mid-order policy changes predictable |  | NOT TESTED |  |  |  |  |  | P06:RS01 |
| P03-T26 | Player sales | — | Per PHASE_03_TESTING.md: §5.3 | Cancellation/expiry |  | NOT TESTED |  |  |  |  |  |  |
| P03-T29 | Player sales | — | Per PHASE_03_TESTING.md | Global disable preserves safe behaviour |  | NOT TESTED |  |  |  |  |  | P03:S12 |
| P03-T30 | Player sales | — | Per PHASE_03_TESTING.md: §5.5 | Repeated customer sales |  | NOT TESTED |  |  |  |  |  |  |
| P03-T31 | Player sales | — | Per PHASE_03_TESTING.md: §5.4 | Dealer behaviour unchanged |  | NOT TESTED |  |  |  |  |  |  |
| P03:S1 | Player sales | Order: N Weed; settings: default | Hand over: N of one permitted Weed that is **not** the product named in the message | Accepted; normal success chance |  | NOT TESTED |  |  |  |  |  |  |
| P03:S10 | Player sales | Order: Order accepted with OG Kush permitted; then untick OG Kush and Save; settings: — | Hand over: N OG Kush | Accepted (the order keeps its snapshot) |  | NOT TESTED |  |  |  |  |  | P06:RS01 |
| P03:S11 | Player sales | Order: Order accepted after S10; settings: — | Hand over: N OG Kush | Refused ("not allowed") |  | NOT TESTED |  |  |  |  |  |  |
| P03:S12 | Player sales | Order: Any order accepted with the mod **OFF**; settings: mod OFF | Hand over: Exact product named in the message | Vanilla: only the exact product works |  | NOT TESTED |  |  |  |  |  |  |
| P03:S2 | Player sales | Order: N Weed; settings: Mixed ON | Hand over: A mix of two permitted Weeds adding up to N | Accepted |  | NOT TESTED |  |  |  |  |  |  |
| P08-HD02 | Player sales (rc.2 fail-closed change) | Order: N Weed; default settings | Hand over N of a permitted Weed that is not the named product, then repeat P03:S1–S3 | Accepted exactly as on rc.1; log has no `could not be checked right now` line in single player |  | NOT TESTED |  |  |  |  |  | P03:S1 |
| P08-HD03 | Handover screen (rc.2 cache change) | Order with 3–4 stacks in the handover screen | Add/remove stacks, swap a stack for another strain and for another quality, wait 30 s with the screen open | The verdict/error text follows every change at once; no stutter compared with the inventory screen; no `Category match failed` line |  | NOT TESTED |  |  |  |  |  |  |
| P03:S3 | Player sales | Order: N Weed; settings: Mixed OFF (saved **before accepting** the order) | Hand over: Same mix as S2 | Refused: "Mixed deliveries are off" |  | NOT TESTED |  |  |  |  |  |  |
| P03:S4 | Player sales | Order: N Weed; settings: default | Hand over: N Meth or Cocaine | Refused: "only wants Weed" |  | NOT TESTED |  |  |  |  |  |  |
| P03:S5 | Player sales | Order: N Weed; settings: Selected Only | Hand over: N of a Weed you unticked under Selected Only **before accepting** | Refused: "not allowed" |  | NOT TESTED |  |  |  |  |  |  |
| P03:S6 | Player sales | Order: N Weed; settings: default | Hand over: Fewer than N | Refused: "Needs N Weed: you offered ..." |  | NOT TESTED |  |  |  |  |  |  |
| P03:S7 | Player sales | Order: N Weed; settings: default | Hand over: More than N | Refused: "Needs exactly N" (no extra units taken) |  | NOT TESTED |  |  |  |  |  |  |
| P03:S8 | Player sales | Order: N Weed; settings: default | Hand over: Unpackaged product | Refused: "Package the product first" |  | NOT TESTED |  |  |  |  |  |  |
| P03:S9 | Player sales | Order: N Weed; settings: default | Hand over: Exactly N valid units, then press Done twice quickly | One sale only |  | NOT TESTED |  |  |  |  |  |  |

### Batch 04 — Basic dealer sale (18 scenarios; config: SP, ≥1 hired dealer with customers)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P04:DS01 | Dealer sales | Dealer holds only a permitted Weed that is **not** the ordered one, enough units | Let the dealer complete the deal | Delivered with the substitute; exactly N units leave the dealer; one payment, cut and XP |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS02 | Dealer sales | Dealer holds two permitted Weeds, neither enough alone, together ≥ N; Mixed ON | Complete | Both used; total removed = N |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS03 | Dealer sales | Same as DS02, Mixed **OFF** before acceptance | Let the deal time come | Appointment waits; at the handover **nothing** is handed over (`nothing handed over`), no payment; the dealer texts a shortage |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS04 | Dealer sales | Dealer holds N of the ordered product and lots of Meth | Complete | Only Weed removed; Meth untouched |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS05 | Dealer sales | Selected Only with one Weed ticked; dealer holds a lot of an unticked Weed | Complete | Unticked Weed never delivered; if the ticked one is short nothing is handed over |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS06 | Dealer sales | Dealer holds less than N eligible units | Accept, check diagnostics | `Waiting for eligible stock: 1` |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS07 | Dealer sales | After DS06, give the dealer enough permitted Weed | Wait a few seconds | Log `reserved`; diagnostics shows 1 reservation |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS08 | Dealer sales | Two appointments for the same dealer, stock for only one | Accept both | One Reserved, one Waiting; never both reserved |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS09 | Dealer sales | Two dealers, each with their own stock and appointment | Complete both | Each dealer only loses their own units |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS10 | Dealer sales | Appointment Reserved; take the reserved product back from the dealer | Wait a few seconds | Log `invalidated`; the appointment waits or replans to other stock |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS11 | Dealer sales | Reserved appointment | Let the contract expire (or cancel it) | Log `ended (Expired/Canceled ...) reservation released`; stock free for the next appointment |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS12 | Dealer sales | Reserved appointment | Save, quit to menu, load | Log `Restored 1 dealer appointment(s)`; diagnostics shows it waiting, then reserved again |  | NOT TESTED |  |  |  |  |  | P06:DA01 |
| P04:DS13 | Dealer sales | As DS12, but remove the dealer's stock before saving | Load | Appointment waits; no reservation larger than the stock |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS14 | Dealer sales | Save A with an appointment; load save B | Check B's diagnostics | No appointments from A; back in A, the appointment is there |  | NOT TESTED |  |  |  |  |  | P06:SW02 |
| P04:DS15 | Dealer sales | Mod **OFF** for the save | Dealer delivers | Vanilla: only the exact product; log `uses vanilla rules: ModDisabled` |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS16 | Dealer sales | Appointment accepted with OG Kush permitted; untick OG Kush and save | Dealer delivers OG Kush | Delivered (the appointment keeps its snapshot) |  | NOT TESTED |  |  |  |  |  | P06:RS01 |
| P04:DS17 | Dealer sales | Three or more dealer deliveries in a row | Complete them | Each: one payment, one cut, exact units; diagnostics returns to 0 appointments |  | NOT TESTED |  |  |  |  |  |  |
| P04:DS18 | Dealer sales | Player sale (Phase 03) while a dealer appointment is open | Complete both | Both work; neither touches the other's items |  | NOT TESTED |  |  |  |  |  |  |

### Batch 05 — Offers and economy (13 scenarios; config: SP, priced strains (SD > OGK))

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P04:OF01 | Offers | Weed = Selected Only, only Sour Diesel ticked, several Weeds listed | Wait for a few player offers (texts) | Every Weed offer names Sour Diesel |  | NOT TESTED |  |  |  |  |  |  |
| P04:OF02 | Offers | Weed = **Disabled**, Meth listed | Wait for offers | No new Weed offers; Meth offers still arrive |  | NOT TESTED |  |  |  |  |  |  |
| P04:OF03 | Offers | Dealer customers, dealer holds OG Kush and Sour Diesel, only Sour Diesel ticked | Wait for dealer offers | Dealer offers only Sour Diesel |  | NOT TESTED |  |  |  |  |  |  |
| P04:OF04 | Offers | Mod **off** for the save | Wait for offers | Vanilla offers (any listed strain); `Customer offer filter` counters do not grow |  | NOT TESTED |  |  |  |  |  |  |
| P04:OF05 | Offers | Any offer from OF01 | Open the message, journal entry and handover screen | Same product, same quantity, same payment everywhere |  | NOT TESTED |  |  |  |  |  |  |
| P04:PR01 | Fair pricing | Sour Diesel priced above OG Kush, both ticked; accept a Sour Diesel order | Offer OG Kush at the handover | Refused before anything moves (*not allowed*) |  | NOT TESTED |  |  |  |  |  |  |
| P04:PR02 | Fair pricing | Same order | Hand over a ticked strain priced at least as high as Sour Diesel | Accepted; cash changes once by the offer's payment |  | NOT TESTED |  |  |  |  |  |  |
| P04:PR03 | Fair pricing | Same order, Mixed ON | Hand over 3 Sour Diesel + 2 of a pricier strain | Accepted, paid once |  | NOT TESTED |  |  |  |  |  |  |
| P04:PR04 | Fair pricing | Accept an OG Kush order (cheapest) | Hand over Sour Diesel | Accepted |  | NOT TESTED |  |  |  |  |  |  |
| P04:PR05 | Fair pricing | Dealer appointment for Sour Diesel; dealer holds only cheaper OG Kush | Let the deal time come | Nothing handed over; shortage text; OG Kush untouched |  | NOT TESTED |  |  |  |  |  |  |
| P05:OF501 | Offers | Weed Blocklist with one product blocked | Wait for player offers | The blocked product is never offered |  | NOT TESTED |  |  |  |  |  | P04:OF01 |
| P05:OF502 | Offers | Player sales OFF | Wait for player offers | Vanilla offers (filter counters do not grow for player offers) |  | NOT TESTED |  |  |  |  |  |  |
| P05:OF503 | Offers | Dealer X override narrows Weed to one product | Wait for X's customers' offers | Only that product (this also confirms that the offer hook's dealer argument identifies the dealer, PROBABLE) |  | NOT TESTED |  |  |  |  |  |  |

### Batch 06 — Advanced quality, effect, mixing, switches, overrides (31 scenarios; config: SP, 2+ quality tiers, dealer)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P05:BL01 | Blocklist | Weed = Blocklist, *Exact recipe*, one product blocked | Accept a Weed order, offer the blocked product | Refused: *This product is blocked by your Marijuana rules.* |  | NOT TESTED |  |  |  |  |  |  |
| P05:BL02 | Blocklist | Same | Offer another Weed | Accepted |  | NOT TESTED |  |  |  |  |  |  |
| P05:BL03 | Blocklist | Blocklist, *Base strain*, base strain blocked | Offer a mix of it | Refused (blocked) |  | NOT TESTED |  |  |  |  |  |  |
| P05:BL04 | Blocklist | After BL01 | Switch to Selected Only and back | Both lists are still there |  | NOT TESTED |  |  |  |  |  |  |
| P05:DO01 | Dealer overrides | Sales screen after dealers have ticked | Look at the dealer list | Hired dealers listed by name with *uses the global rules* |  | NOT TESTED |  |  |  |  |  |  |
| P05:DO02 | Dealer overrides | Global Weed = Selected Only (A); *Edit overrides* for dealer X, Weed = Any Unlocked; Save | X's Weed appointment; another dealer's appointment | X may use any Weed; the other dealer only A |  | NOT TESTED |  |  |  |  |  |  |
| P05:DO03 | Dealer overrides | After DO02 | Rules → *Use global rule* for Weed, Save | X back to A only |  | NOT TESTED |  |  |  |  |  |  |
| P05:DO04 | Dealer overrides | Overrides for X, dealer sales OFF | X's appointment | Vanilla (switch wins over the override) |  | NOT TESTED |  |  |  |  |  |  |
| P05:EF01 | Effects | Rules → Weed: one effect *Required*, *Require all* | Products list | Only products with that effect are *Permitted*; others *Missing effect* |  | NOT TESTED |  |  |  |  |  |  |
| P05:EF02 | Effects | One effect *Excluded* | Accept a Weed order, offer a product with it | Refused: *This product has an effect your rules exclude.* |  | NOT TESTED |  |  |  |  |  |  |
| P05:EF03 | Effects | *Require any* with two effects | Products list | Products with either effect are permitted |  | NOT TESTED |  |  |  |  |  |  |
| P05:EF04 | Effects | Make a rule that permits nothing (e.g. require an effect no discovered Weed has) | Overview / Products | Orange warning; Weed orders stay vanilla (`CategoryNotEligible`) |  | NOT TESTED |  |  |  |  |  |  |
| P05:EX01 | Exact recipe | Weed = Selected Only, *Exact recipe*, only the base strain ticked | Accept a Weed order, offer a mix of that strain | Refused before transfer: *This recipe isn't on your allowed list.* |  | NOT TESTED |  |  |  |  |  |  |
| P05:EX02 | Exact recipe | Same | Offer the base strain | Accepted, normal payment |  | NOT TESTED |  |  |  |  |  |  |
| P05:EX03 | Exact recipe | Only the mix ticked | Offer the base strain | Refused |  | NOT TESTED |  |  |  |  |  |  |
| P05:MX01 | Mixing | Mixing *Any permitted* | Deliver 3 of one Weed + 2 of another for 5 Weed | Accepted |  | NOT TESTED |  |  |  |  |  | P03:S2 |
| P05:MX02 | Mixing | *One product per order* | Same delivery | Refused: *Mixed deliveries are disabled for this order: use one product only.* |  | NOT TESTED |  |  |  |  |  | P03:S3 |
| P05:MX03 | Mixing | *One base strain per order* | Deliver a base strain + its mix | Accepted |  | NOT TESTED |  |  |  |  |  |  |
| P05:MX04 | Mixing | *One base strain per order* | Deliver products of two lineages | Refused: *limited to one base strain* |  | NOT TESTED |  |  |  |  |  |  |
| P05:MX05 | Mixing | *Requested product only* | Offer a different permitted product | Refused: *This order only accepts the product the customer asked for.* |  | NOT TESTED |  |  |  |  |  |  |
| P05:MX06 | Mixing | Any mode, Sour Diesel priced above OG Kush | Accept a Sour Diesel order, offer OG Kush | Refused (fair price) in every mixing mode |  | NOT TESTED |  |  |  |  |  | P04:PR01 |
| P05:QD01 | Quality (dealer) | Weed minimum *Premium+*; dealer holds only Standard Weed | Wait for a dealer appointment | `Dealer appointment created ... Premium or better ...: Waiting` (nothing reserved); dealer texts that permitted units are missing |  | NOT TESTED |  |  |  |  |  |  |
| P05:QD02 | Quality (dealer) | Give the dealer Premium Weed | Wait for the handover | Premium units handed over; Standard units stay in the dealer's inventory |  | NOT TESTED |  |  |  |  |  |  |
| P05:QD03 | Quality (dealer) | Minimum *Any*, customer requires Premium | Dealer holds Standard + Premium | Only Premium units are reserved and handed over |  | NOT TESTED |  |  |  |  |  |  |
| P05:QP01 | Quality (player) | Weed minimum *Premium+* | Accept a Weed order, offer Standard items | Refused: *The item quality is below your minimum (Premium or better).* Nothing leaves your inventory |  | NOT TESTED |  |  |  |  |  |  |
| P05:QP02 | Quality (player) | Same | Offer Premium or Heavenly items | Accepted; payment as vanilla for that quality |  | NOT TESTED |  |  |  |  |  |  |
| P05:QP03 | Quality (player) | Minimum *Any* | Offer low-quality items to a demanding customer | Same customer reaction as without the mod (quality still counts) |  | NOT TESTED |  |  |  |  |  |  |
| P05:SW01 | Player/dealer switches | Player sales OFF, Weed = Selected Only (one product) | Accept a player Weed order | `uses vanilla rules: SalesPathDisabled`; vanilla behaviour and offers |  | NOT TESTED |  |  |  |  |  |  |
| P05:SW02 | Player/dealer switches | Same | A dealer appointment | Managed as usual (`Dealer appointment created`) |  | NOT TESTED |  |  |  |  |  |  |
| P05:SW03 | Player/dealer switches | Dealer sales OFF | A dealer appointment | `Dealer contract ... uses vanilla rules: SalesPathDisabled`; no out-of-stock texts |  | NOT TESTED |  |  |  |  |  |  |
| P05:SW04 | Player/dealer switches | A managed order accepted, then switch its path OFF and Save | Complete the order | It keeps its accepted rules; dealer reservation kept |  | NOT TESTED |  |  |  |  |  | P06:RS01 |

### Batch 07 — Dealer reservations, strict fulfilment, quality lots, restock texts (24 scenarios; config: SP, hired dealer(s))

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P04:MSG01 | Restock texts | Load a save with hired dealers | Watch the phone for 1 minute | No burst of dealer texts on load |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG02 | Restock texts | Dealer holds permitted Weed | Let them sell it all (or take it back) | One text: *Yo boss, I'm out of Weed I can sell. Need you to restock me.* in that dealer's conversation, with the game's notification |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG03 | Restock texts | After MSG02 | Wait 2 minutes | No repeat |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG04 | Restock texts | After MSG02 | Give the dealer permitted Weed, then take it away again | A second out-of-stock text |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG05 | Restock texts | Dealer has 2 permitted Weed, appointment needs 5 | Wait a few seconds after acceptance | *Boss, I've got a customer waiting for 5 Weed, but only 2 permitted units are available. Need 3 more.* |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG06 | Restock texts | After MSG05 | Take one more unit away | A new text with *Need 4 more*; giving 1 back sends nothing |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG07 | Restock texts | Mixed OFF; dealer has 3 OG Kush + 2 Sour Diesel, appointment needs 5 | Wait | Text says *only 3 permitted units* |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG08 | Restock texts | Dealer holds only an unticked Weed | Wait | Counts as out of Weed (text if the dealer stocked permitted Weed before) |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG09 | Restock texts | After MSG02 | Save, quit, load | No repeat of the delivered text |  | NOT TESTED |  |  |  |  |  |  |
| P04:MSG10 | Restock texts | Weed Disabled | Dealer runs out | No Weed text |  | NOT TESTED |  |  |  |  |  |  |
| P04:ST01 | Strict fulfilment | Only OG Kush ticked; appointment (vanilla asked for Sour Diesel); dealer has 2 OG Kush and plenty of Sour Diesel | Let the deal time come | Log `nothing handed over`; Sour Diesel and OG Kush untouched; no payment, no cut, no XP. **Note what the customer does** (refuses, leaves, contract fails) |  | NOT TESTED |  |  |  |  |  | P08-EH01 |
| P04:ST02 | Strict fulfilment | After ST01, give the dealer enough OG Kush before the deadline | Wait | Appointment reserves; next attempt (if the game retries) delivers OG Kush; or the contract fails/expires cleanly |  | NOT TESTED |  |  |  |  |  |  |
| P04:ST03 | Strict fulfilment | ST01 left to its deadline | Wait | Contract expires/fails through the game; diagnostics back to 0 appointments; nothing removed |  | NOT TESTED |  |  |  |  |  | P08-EH01 |
| P04:ST04 | Strict fulfilment | Move a customer with an appointment to another dealer who has permitted stock | Deal time | The new dealer delivers from their own stock; the old dealer keeps theirs |  | NOT TESTED |  |  |  |  |  |  |
| P05:RS01 | Reservations | Minimum *Standard+*; dealer holds Poor + Standard Weed | Two appointments | Reservations only count Standard units; never more than held |  | NOT TESTED |  |  |  |  |  |  |
| P05:RS02 | Reservations | After RS01 | Take the Standard units away | Affected appointment invalidated and waits; shortage text |  | NOT TESTED |  |  |  |  |  |  |
| P05:RS03 | Reservations | Dealer holds only blocked Weed | Wait | Out-of-Weed text (if it had permitted Weed before) |  | NOT TESTED |  |  |  |  |  | P04:MSG08 |
| P06:NT01 | Restock texts | Dealer sent "out of Weed" | Restart, dealer still empty | No second text |  | NOT TESTED |  |  |  |  |  | P04:MSG09 |
| P06:NT02 | Restock texts | NT01 | Restock, empty again | One new text |  | NOT TESTED |  |  |  |  |  | P04:MSG04 |
| P06:QL01 | Quality lots | Weed minimum Premium; dealer has Poor and Premium stock | Restart with an appointment waiting | Only Premium units are reserved and handed over |  | NOT TESTED |  |  |  |  |  | P05:QD02 |
| P08-EH01 | Strict fulfilment | Dealer appointment with too little permitted stock (ST01 setup) | Let the dealer attend; observe for the full contract window | Record exactly: customer waits/leaves, contract fails/expires, money/XP/inventory changes, whether a later appointment works. Any completion, payment or stuck appointment = FAIL (P0/P1) |  | NOT TESTED |  |  |  |  |  |  |
| P08-QL01 | Quality lots | Dealer holds Standard and Premium OG Kush; appointment A reserves Standard, B reserves Premium | Let A be handed over first | A removes Standard only; B keeps its Premium reservation. Log `different quality tier than reserved` = FAIL (see blocker B-03) |  | NOT TESTED |  |  |  |  |  |  |
| P08-QL02 | Quality lots | Mixed-quality stock, three simultaneous appointments | Let all complete | Each removes its own reserved tier; totals = units delivered; no stuck appointment |  | NOT TESTED |  |  |  |  |  |  |
| P08-HD04 | Dealer handover (rc.2 rollback change) | Dealer with permitted stock; one appointment fully covered, one short | Let both hand over | Covered one completes; short one hands over nothing; dealer stock afterwards = before − delivered units; Diagnostics shows no `Items waiting to go back to dealers` line; no `could not give a removed item back` line |  | NOT TESTED |  |  |  |  |  |  |

### Batch 08 — Save, reload and recovery (33 scenarios; config: SP, two saves A/B, game closed for file edits)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P02-T23 | Phone app | Restart persistence | Quit game, relaunch, load same save | Same settings shown |  | NOT TESTED |  |  |  |  |  | P06:PR03 |
| P02-T25 | Phone app | Save isolation | Configure save A, load save B, back to A | B has its own (default) settings; A's settings return |  | NOT TESTED |  |  |  |  |  | P06:PR01 |
| P03-T27 | Player sales | — | Per PHASE_03_TESTING.md: §5.1 | Active order survives save/reload |  | NOT TESTED |  |  |  |  |  | P06:RS01 |
| P03-T28 | Player sales | — | Per PHASE_03_TESTING.md: §5.2 | Save switching does not leak orders |  | NOT TESTED |  |  |  |  |  | P06:SW01 |
| P06:CR01 | Recovery | Saved rules, then a second Save (so `<id>.json.bak` exists) | With the game closed, cut `<id>.json` in half; load the save | Warning that settings are damaged; the backup is shown as unsaved changes; mod sales off (vanilla) until Save |  | NOT TESTED |  |  |  |  |  |  |
| P06:CR02 | Recovery | CR01 | Press Save | Rules restored; `<id>.json.corrupt-<time>` holds the damaged bytes |  | NOT TESTED |  |  |  |  |  |  |
| P06:CR03 | Recovery | No `.bak` | Damage `<id>.json`, load | Defaults shown, mod sales off, nothing applied automatically |  | NOT TESTED |  |  |  |  |  |  |
| P06:CR04 | Recovery | — | Copy save A's `<id>.json` over save B's | B treats it as damaged ("different save"), sales off |  | NOT TESTED |  |  |  |  |  |  |
| P06:CR05 | Recovery | — | Damage `<id>.orders.json`, load | Orders of that save use vanilla rules; after the next order change a `.corrupt-` copy exists |  | NOT TESTED |  |  |  |  |  |  |
| P06:CR06 | Recovery | — | Damage `<id>.dealer-alerts.json` (with a `.bak`), load | History recovered from the backup; no repeated out-of-stock texts |  | NOT TESTED |  |  |  |  |  |  |
| P06:DA01 | Dealer reload | Dealer appointment reserved | Save, restart, load | Appointment restored waiting; reservation rebuilt only from the dealer's current inventory |  | NOT TESTED |  |  |  |  |  |  |
| P06:DA02 | Dealer reload | Dealer lost stock while the game was closed (load an older game save) | Load | No phantom reservation; appointment waits; shortage text at most once |  | NOT TESTED |  |  |  |  |  |  |
| P06:DA03 | Dealer reload | Dealer handover processed | Restart before the contract ends | Not paid, removed or counted twice |  | NOT TESTED |  |  |  |  |  |  |
| P06:NS01 | Schema versions | Edit `<id>.json` to `"schemaVersion": 3` | Load | "saved by a newer version"; Save refused; file byte-identical afterwards |  | NOT TESTED |  |  |  |  |  |  |
| P06:NS02 | Schema versions | Edit `<id>.orders.json` to `"schemaVersion": 2` | Load, accept an order | Diagnostics "orders newer version, kept read-only"; file unchanged |  | NOT TESTED |  |  |  |  |  |  |
| P06:NS03 | Schema versions | A Phase 01–04 settings file (schema 1) | Load, Save, Save again | Same permissions; `.schema1.bak` created once and never changed |  | NOT TESTED |  |  |  |  |  | P05:MG03 |
| P06:NS04 | Schema versions | Phase 05 files (no `save` field) | Load with the Phase 06 build | Everything loads as before; the field is added on the next write |  | NOT TESTED |  |  |  |  |  |  |
| P06:PR01 | Save profiles | Two saves A and B, GDP unlocked in A | In A: Weed → Selected Only → GDP, Save. Quit to menu, load B | B shows Weed *Any Unlocked*; Diagnostics shows a different profile id |  | NOT TESTED |  |  |  |  |  |  |
| P06:PR02 | Save profiles | PR01 done | Load A again | Weed is GDP only |  | NOT TESTED |  |  |  |  |  |  |
| P06:PR03 | Save profiles | PR01 done | Quit the game completely, start it, load B then A | Each keeps its own rules |  | NOT TESTED |  |  |  |  |  |  |
| P06:PR04 | Save profiles | A has a dealer override for one dealer | Load B | No overrides listed in B; the dealer list shows only B's dealers |  | NOT TESTED |  |  |  |  |  |  |
| P06:PR05 | Save profiles | B has not unlocked GDP | In B accept a weed order | GDP is never offered or accepted in B because of A's rule |  | NOT TESTED |  |  |  |  |  |  |
| P06:RS01 | Restart/quit | Accepted order (OG Kush allowed) | Change Weed to GDP only, Save, save the game, quit, restart, load | The accepted order still accepts OG Kush; a new order accepts GDP only |  | NOT TESTED |  |  |  |  |  |  |
| P06:RS02 | Restart/quit | Pending changes | Quit normally right after accepting an order | Log has no "could not be completed before quitting" warning; `.orders.json` contains the order |  | NOT TESTED |  |  |  |  |  |  |
| P06:RS03 | Restart/quit | — | End the game process from Task Manager during play | Next start loads; every mod file is either the previous or the newer complete version |  | NOT TESTED |  |  |  |  |  |  |
| P06:RT01 | Reset | Custom rules saved | Settings → Reset to defaults → confirm → Save | Defaults active; previous rules kept in `<id>.json.bak` |  | NOT TESTED |  |  |  |  |  | P02-T24 |
| P08-HD05 | Oversized file (rc.2) | Game closed; disposable save copy; back up `UserData\TakeItOrLeaveIt\saves\` | Replace that save's `<id>.json` with any file larger than 4 MB (e.g. a copy of a large log padded with text), start, load the save, open Diagnostics; then restore the backup | Settings shown as unreadable, mod sales off for that save, the large file is unchanged on disk and not renamed; no crash; after restore everything is back |  | NOT TESTED |  |  |  |  |  |  |
| P06:SW01 | Save switching | Active player order in A | Immediately quit to menu and load B | No order of A appears in B; log shows "Restored … for save-…" only for B's own orders |  | NOT TESTED |  |  |  |  |  |  |
| P06:SW02 | Save switching | Dealer appointment waiting in A | Switch to B, then back to A | A's appointment is back, reservations rebuilt from the dealer's real stock |  | NOT TESTED |  |  |  |  |  |  |
| P06:SW03 | Save switching | — | Switch saves 5 times quickly | No error, no file of one save containing another's id; loading screen not visibly slower |  | NOT TESTED |  |  |  |  |  |  |
| P06:WR01 | Write failures | Make `saves/<id>.orders.json` read-only | Accept an order | Log: "Could not write … retried"; Diagnostics shows a failing pending write; the game keeps running |  | NOT TESTED |  |  |  |  |  |  |
| P06:WR02 | Write failures | WR01 | Remove read-only | Log: "written after N failed attempt(s)"; pending writes 0 |  | NOT TESTED |  |  |  |  |  |  |
| P06:WR03 | Write failures | Make the settings file read-only | Change a rule, press Save | "Settings were NOT saved"; the rule is not shown as saved; nothing lost on disk |  | NOT TESTED |  |  |  |  |  |  |

### Batch 09 — Co-op and compatibility (40 scenarios; config: Co-op, 2 PCs / 2 Steam accounts)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P07:CD01 | Co-op dealers | Host's dealer with GDP and OG Kush | Host: Let a dealer appointment run / Client: Watch | Dealer fills the category order from permitted stock |  | NOT TESTED |  |  |  |  |  | P04:DS01 |
| P07:CD02 | Co-op dealers | Dealer stock below minimum quality | Host: Let an appointment run / Client: — | Waits / hands over nothing |  | NOT TESTED |  |  |  |  |  |  |
| P07:CD03 | Co-op dealers | Dealer short of permitted stock | Host: Let it run / Client: — | Restock text on the **host's** phone only |  | NOT TESTED |  |  |  |  |  |  |
| P07:CD04 | Co-op dealers | Client hired a dealer (if the game allows) | Host: — / Client: Let an appointment run | Managed on the host like any dealer |  | NOT TESTED |  |  |  |  |  |  |
| P07:CD05 | Co-op dealers | Appointment in progress | Host: Take stock from the dealer during the walk / Client: — | Replanned or waits; never sells an excluded product |  | NOT TESTED |  |  |  |  |  |  |
| P07:CM01 | Compatibility | Both modded, same version | Host: Host / Client: Join | As SA02–SA03 |  | NOT TESTED |  |  |  |  |  | P07:SA03 |
| P07:CM02 | Compatibility | Host modded, client vanilla | Host: Run CP01, CD01 / Client: Play normally | Host features work; client vanilla |  | NOT TESTED |  |  |  |  |  |  |
| P07:CM03 | Compatibility | Host vanilla, client modded | Host: Host / Client: Join, wait 30 s | "The host does not share Take It rules. Vanilla rules apply." |  | NOT TESTED |  |  |  |  |  |  |
| P07:CM04 | Compatibility | Different Take It versions | Host: Host / Client: Join | Same protocol: rules + note; other protocol: no rules, "update" message |  | NOT TESTED |  |  |  |  |  |  |
| P07:CM05 | Compatibility | Different game builds (if obtainable) | Host: Host / Client: Join | Diagnostics "Not a supported combination" |  | NOT TESTED |  |  |  |  |  |  |
| P07:CM06 | Compatibility | Any untested game build | Host: Start the game / Client: — | Startup warning "not a tested build"; hooks checked; vanilla if one is missing |  | NOT TESTED |  |  |  |  |  |  |
| P07:CP01 | Co-op player sales | Weed = GDP, OG Kush | Host: Accept a weed order, hand over OG Kush instead of the asked strain / Client: — | Accepted as a category order |  | NOT TESTED |  |  |  |  |  | P03:S1 |
| P07:CP02 | Co-op player sales | CP01 setup | Host: Accept a weed order / Client: Hand it over with a **different** permitted strain | Refused by the game (exact-product rule on the client) |  | NOT TESTED |  |  |  |  |  |  |
| P07:CP03 | Co-op player sales | CP01 setup | Host: Accept a weed order / Client: Hand over the exact product the customer asked for | Accepted by the game; paid once |  | NOT TESTED |  |  |  |  |  |  |
| P07:CP04 | Co-op player sales | Weed = GDP only | Host: Watch several customer offers / Client: Watch offers to the client | Only GDP is requested |  | NOT TESTED |  |  |  |  |  | P04:OF01 |
| P07:CP05 | Co-op player sales | Excluded Sour Diesel in client inventory | Host: — / Client: Try to hand over Sour Diesel for a GDP order | Refused; nothing removed |  | NOT TESTED |  |  |  |  |  |  |
| P07:CP06 | Co-op player sales | One active order | Host: Open the handover screen / Client: Open the handover screen for the same customer at the same time | The contract completes once |  | NOT TESTED |  |  |  |  |  |  |
| P07:CP07 | Co-op player sales | Host has an active category order | Host: Change the rules and Save / Client: — | The accepted order keeps its snapshot rules |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF01 | Co-op faults | Host has changed rules several times | Host: — / Client: Join in progress | Client shows the latest revision |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF02 | Co-op faults | SA03 | Host: — / Client: Leave the game | Client menu returns to the local rules; nothing written |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF03 | Co-op faults | SA03 | Host: Quit to menu / Client: — | Client shows "Disconnected"; no host migration |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF04 | Co-op faults | SA03 | Host: Quit to menu and load another save, re-host / Client: Rejoin | New session; client shows the new save's rules |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF05 | Co-op faults | Client mid-handover | Host: — / Client: Disconnect during the handover | Game decides; at most one settlement |  | NOT TESTED |  |  |  |  |  |  |
| P07:NF06 | Co-op faults | SA03 for 5 minutes | Host: Idle / Client: Idle | Heartbeat every 30 s; no new revisions |  | NOT TESTED |  |  |  |  |  |  |
| P07:PE01 | Co-op persistence | Active orders and appointments | Host: Save, quit, reload, re-host / Client: Rejoin | Host restores its snapshots; client writes nothing |  | NOT TESTED |  |  |  |  |  |  |
| P07:PE02 | Co-op persistence | Client has its own single-player save with custom rules | Host: — / Client: Join a host, leave, load the own save | Own rules unchanged |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA01 | Co-op session | Host alone | Host: Load a save / Client: — | Diagnostics "Session: SinglePlayerHost"; sales work as in Phase 03–06 |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA02 | Co-op session | SA01, invite open | Host: Wait for the client to join / Client: Join | Host becomes MultiplayerHostReady |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA03 | Co-op session | SA02 | Host: — / Client: Open the Take It app | Status bar "Host controls Take It settings."; Overview shows the host's rules |  | NOT TESTED |  |  |  |  |  |  |
| P08-HD06 | Co-op authority (rc.2) | Host has an accepted category order | Client joins, leaves and joins again while the host hands the order over between the changes | Host delivery judged by category every time; at most a brief `could not be checked right now` refusal during a transition, never an exact-product decision; no stuck order |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA04 | Co-op session | SA03 | Host: — / Client: Try to change a rule, Save, Discard, Reset | All refused; nothing written |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA05 | Co-op session | SA02 | Host: Change a rule and Save / Client: Watch Overview | Client shows the new rules within a few seconds (revision 2) |  | NOT TESTED |  |  |  |  |  |  |
| P07:SA06 | Co-op session | SA05 | Host: Change a rule, do **not** Save / Client: Watch Overview | Client keeps the saved rules (drafts are never shared) |  | NOT TESTED |  |  |  |  |  |  |
| P08-CH01 | Co-op player sales | Co-op, Weed = GDP + OG Kush, customer asks GDP | Client hands over OG Kush; then GDP | OG Kush refused by the game; GDP accepted and paid once (vanilla exact-product rule on the client) |  | NOT TESTED |  |  |  |  |  |  |
| P08-CH02 | Co-op player sales | Co-op, client unmodded (mode B) | Client completes a host-accepted category contract with the exact product | Paid once; host snapshot released; no double settlement |  | NOT TESTED |  |  |  |  |  |  |
| P08-MP01 | Co-op player sales | Co-op | Host and client sell to different customers at the same time | Each paid once; inventories change by exactly what each delivered |  | NOT TESTED |  |  |  |  |  |  |
| P08-MP02 | Co-op dealers | Co-op, dealer appointment open | Dealer fulfils while host and client sell | One payment/cut/XP per contract; dealer loses exactly the removed units |  | NOT TESTED |  |  |  |  |  |  |
| P08-UN01 | Compatibility | Mono DLL installed on IL2CPP (wrong runtime) | Launch | `Wrong mod build` error; no patches applied; game vanilla |  | NOT TESTED |  |  |  |  |  |  |
| P08-UN02 | Compatibility | S1API removed | Launch, load | `S1API is not installed` line; sales still work; no phone app |  | NOT TESTED |  |  |  |  |  |  |
| P08-UN03 | Compatibility | Settings file unreadable (see CR03) | Load | Mod sales off; nothing broadened |  | NOT TESTED |  |  |  |  |  |  |

### Batch 10 — Endurance and regression (13 scenarios; config: SP (+co-op where noted))

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P01-T21 | Discovery | — | PHASE_01_TESTING.md §2.2-2.6 | In-game compatibility |  | NOT TESTED |  |  |  |  |  |  |
| P03-T32 | Player sales | — | Per PHASE_03_TESTING.md | No mod crash, deadlock or unhandled exception |  | NOT TESTED |  |  |  |  |  |  |
| P04:RG01 | Regression |  | DS01, DS02, DS17 again | Unchanged results |  | NOT TESTED |  |  |  |  |  | P04:DS17 |
| P04:RG02 | Regression |  | A Phase 03 player sale with a permitted, equally priced substitute | Accepted, paid once |  | NOT TESTED |  |  |  |  |  | P04:PR04 |
| P04:RG03 | Regression |  | Load a save that has `*.orders.json` / `*.dealer-orders.json` from the previous Phase 04 build | Log `predates the fair-price rule`; those orders accept only their requested strain |  | NOT TESTED |  |  |  |  |  |  |
| P05:RG501 | Regression | Defaults | Run Phase 03 player sale and Phase 04 dealer appointment basics | Same results as described in PHASE_03/04 testing |  | NOT TESTED |  |  |  |  |  | P06:RG601 |
| P05:RG502 | Regression | Defaults | Complete one sale | Paid once; XP once; no cash or XP from the mod |  | NOT TESTED |  |  |  |  |  | P03-T20 |
| P05:RG503 | Regression | Any | Save, quit, reload mid-order | Accepted order keeps its rules (mixing, minimum quality) |  | NOT TESTED |  |  |  |  |  | P06:RS01 |
| P05:RG504 | Regression | Mod OFF | Any sale | Fully vanilla |  | NOT TESTED |  |  |  |  |  | P03:S12 |
| P06:RG601 | Regression |  | Re-run the Phase 03–05 core scenarios (player handover, dealer handover, offers, fair pricing, presets) | Unchanged behaviour |  | NOT TESTED |  |  |  |  |  |  |
| P06:RG602 | Regression |  | Check the game's own save folder after all tests | No file added or changed by the mod |  | NOT TESTED |  |  |  |  |  |  |
| P08-EN01 | Regression | SP save | Play ≥ 60 min mixing menu use, offers, player + dealer sales, save/load, scene changes | Record measured duration, exceptions, log volume, frame-rate feel, pending writes at end (0) |  | NOT TESTED |  |  |  |  |  |  |
| P08-EN02 | Regression | Co-op | Play ≥ 30 min with joins/leaves and sales | Record duration and observations; no duplicate settlement |  | NOT TESTED |  |  |  |  |  |  |

### Batch 11 — Final installation-package smoke test (3 scenarios; config: Clean install from ZIP)

| ID | Subsystem | Setup | Actions | Expected | Actual | Status | RC | Game | Evidence | Issue / fix | Retest | Reuses |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P08-PK01 | Package | Fresh game folder with only MelonLoader + S1API | Extract the RC ZIP into the game folder | Exactly Mods/TakeItOrLeaveIt.Il2Cpp.dll and UserLibs/TakeItOrLeaveIt.Core.dll added |  | NOT TESTED |  |  |  |  |  |  |
| P08-PK02 | Package | After PK01 | Launch, load, open app, change+Save one rule, one player sale, one dealer sale, restart, check rule | All work; rule persists |  | NOT TESTED |  |  |  |  |  |  |
| P08-PK03 | Package | After PK02 | Uninstall per INSTALLATION.md, load the save | Vanilla behaviour; save loads; no errors |  | NOT TESTED |  |  |  |  |  |  |

## 6. Automated and process acceptance IDs (Phases 01–04)

These earlier checklist IDs were build, test, documentation or Git-process checks, not in-game scenarios. Their recorded results are copied unchanged; the physical parts of those checklists are rows in §5.


| ID | Test | Recorded result |
| --- | --- | --- |
| P01-T01 | Git branch setup | PASS |
| P01-T06 | Locked products | PASS |
| P01-T09 | Any Unlocked | PASS |
| P01-T10 | Selected Only | PASS |
| P01-T11 | Disabled | PASS |
| P01-T12 | Empty selection | PASS |
| P01-T13 | Category safety | PASS |
| P01-T14 | Single-stack allocation | PASS |
| P01-T15 | Mixed-stack allocation | PASS |
| P01-T16 | Invalid inventory | PASS |
| P01-T17 | Non-mutating behaviour | PASS |
| P01-T18 | Error handling | PASS (core) |
| P01-T19 | Automated tests | PASS |
| P01-T20 | Build verification | PASS |
| P01-T22 | Documentation | PASS |
| P01-T23 | CI verification | see PR |
| P01-T24 | GitHub delivery | see PR |
| P02-T01 | Feature branch | PASS (automated suite) |
| P02-T02 | Phase 01 integration | PASS (automated suite) |
| P02-T03 | Build | PASS (automated suite) |
| P02-T31 | Automated tests | PASS (automated suite) |
| P02-T32 | GitHub Actions | PASS (automated suite) |
| P02-T33 | Documentation | PASS (automated suite) |
| P02-T34 | Pull request | PASS (automated suite) |
| P03-T01 | Feature branch created correctly | PASS (`feature/phase-03-player-sales` from `develop` at the Phase 02 merge) |
| P03-T02 | Phase 01 and 02 integration confirmed | PASS (code review: validation uses `SaleEligibilityPolicy` + `InventoryAllocationPlanner`; the menu's saved policy feeds the snapshots) |
| P03-T03 | Customer lifecycle hooks investigated | PASS (static call graph, [PHASE_03_PATCH_ANALYSIS.md](PHASE_03_PATCH_ANALYSIS.md)) |
| P03-T33 | Existing automated tests pass | PASS (all Phase 00-02 tests unchanged and passing) |
| P03-T34 | New transaction tests pass | PASS (suites A-E) |
| P03-T35 | Both targets build | PASS (IL2CPP with and without local references, Mono; 0 warnings) |
| P03-T36 | GitHub Actions passes | see the pull request checks |
| P03-T37 | Documentation updated | PASS |
| P03-T38 | PR targets `develop`, not merged | see the pull request |
| P04-T01 | Dealer contract becomes a category appointment with a snapshot | PASS (automated suite) |
| P04-T02 | Player and dealer factories never cross | PASS (automated suite) |
| P04-T03 | Mod disabled keeps dealers vanilla | PASS (automated suite) |
| P04-T04 | Unloaded settings keep dealers vanilla | PASS (automated suite) |
| P04-T05 | Clients and multiplayer keep dealers vanilla | PASS (automated suite) |
| P04-T06 | Disabled or empty category keeps dealers vanilla | PASS (automated suite) |
| P04-T07 | Mixed-category contract keeps dealers vanilla | PASS (automated suite) |
| P04-T08 | Unidentified dealer keeps the contract vanilla | PASS (automated suite) |
| P04-T09 | Repeated acceptance keeps one snapshot and one reservation | PASS (automated suite) |
| P04-T10 | Snapshot survives later policy changes | PASS (automated suite) |
| P04-T11 | Reserves from one product | PASS (automated suite) |
| P04-T12 | Mixed reservation is deterministic | PASS (automated suite) |
| P04-T13 | Mixed off uses one product with enough stock | PASS (automated suite) |
| P04-T14 | Mixed off never combines products | PASS (automated suite) |
| P04-T15 | Two appointments never share units | PASS (automated suite) |
| P04-T16 | Each dealer only promises their own stock | PASS (automated suite) |
| P04-T17 | Repeated reserve is idempotent | PASS (automated suite) |
| P04-T18 | A contract cannot hold two dealers' stock | PASS (automated suite) |
| P04-T19 | Release frees units once | PASS (automated suite) |
| P04-T20 | Ineligible products are never reserved | PASS (automated suite) |
| P04-T21 | Lost stock invalidates the newest reservations first | PASS (automated suite) |
| P04-T22 | Commit releases the hold exactly once; state machine | PASS (automated suite) |
| P04-T23 | Short stock waits instead of failing | PASS (automated suite) |
| P04-T24 | Waiting appointment reserves when stock arrives | PASS (automated suite) |
| P04-T25 | Waiting appointments served in acceptance order, bounded per reconcile | PASS (automated suite) |
| P04-T26 | Dealer delivers a permitted substitute | PASS (automated suite) |
| P04-T27 | Mixed delivery removes exactly the plan | PASS (automated suite) |
| P04-T28 | Short stock withholds the handover and keeps waiting (strict) | PASS (automated suite) |
| P04-T29 | Second handover removes nothing | PASS (automated suite) |
| P04-T30 | Lost stock replans to another product | PASS (automated suite) |
| P04-T31 | Repeated stock loss keeps the appointment managed (strict) | PASS (automated suite) |
| P04-T32 | Contract moved to another dealer follows the new dealer (strict) | PASS (automated suite) |
| P04-T33 | Expire/cancel/fail release the stock | PASS (automated suite) |
| P04-T34 | Reconcile is skipped until something changes | PASS (automated suite) |
| P04-T35 | One dealer sale settles exactly once (payment, cut, XP, units) | PASS (automated suite) |
| P04-T36 | Repeated settlement callbacks are ignored | PASS (automated suite) |
| P04-T37 | Refused delivery hands items back and pays nothing | PASS (automated suite) |
| P04-T38 | Unmanaged dealer contract is exactly vanilla | PASS (automated suite) |
| P04-T39 | Forbidden products are never substituted | PASS (automated suite) |
| P04-T40 | Customer judges the dealer delivery with the snapshot | PASS (automated suite) |
| P04-T41 | Appointments round-trip without reservations | PASS (automated suite) |
| P04-T42 | Reload rebuilds reservations from live stock only (no phantom stock) | PASS (automated suite) |
| P04-T43 | Damaged files never create appointments | PASS (automated suite) |
| P04-T44 | Save switching isolates appointments | PASS (automated suite) |
| P04-T45 | Prune drops vanished contracts and their holds | PASS (automated suite) |
| P04-T46 | Status reports only what is really active | PASS (automated suite) |
| P04-T47 | 3000 random interleavings across 3 dealers keep every invariant (**simulated**) | PASS (automated suite) |
| P04-T48 | 50 dealers × 20 appointments, 5 reconcile rounds (**simulated** benchmark) | PASS (automated suite) |
