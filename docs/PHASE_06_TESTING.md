# Phase 06 — In-game test plan (to run in Phase 08)

> **Every scenario below is NOT TESTED.** Physical in-game acceptance is deferred to Phase 08 by project policy.
> Phase 06 is verified only by automated tests (612 Core tests, simulation and fault injection), static review and
> builds. Nothing here may be marked PASS without in-game evidence (log excerpt, screenshot or file listing).

Build under test: the Phase 06 IL2CPP package (`TakeItOrLeaveIt.Il2Cpp.dll` + `TakeItOrLeaveIt.Core.dll`) on
Schedule I v0.4.7f12 IL2CPP. Mod files: `<game>/UserData/TakeItOrLeaveIt/saves/`. The phone app's **Diagnostics**
screen has a "Save data (Phase 06)" line and "Runtime files" / "Pending writes" lines under "Player and dealer sales".

**Before starting:** copy the whole `UserData/TakeItOrLeaveIt` folder and the game's save folder somewhere safe. Never
delete the files the mod creates (`*.bak`, `*.schema1.bak`, `*.corrupt-*`, older `*.unreadable.bak`).

Status values: PASS / FAIL / BLOCKED / NOT TESTED.

## PR — Per-save profiles

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| PR01 | Two saves A and B, GDP unlocked in A | In A: Weed → Selected Only → GDP, Save. Quit to menu, load B | B shows Weed *Any Unlocked*; Diagnostics shows a different profile id | Screenshot of both Diagnostics lines | NOT TESTED |
| PR02 | PR01 done | Load A again | Weed is GDP only | Screenshot | NOT TESTED |
| PR03 | PR01 done | Quit the game completely, start it, load B then A | Each keeps its own rules | Screenshots, `saves/` listing (two `.json` profiles) | NOT TESTED |
| PR04 | A has a dealer override for one dealer | Load B | No overrides listed in B; the dealer list shows only B's dealers | Screenshot of Sales screen | NOT TESTED |
| PR05 | B has not unlocked GDP | In B accept a weed order | GDP is never offered or accepted in B because of A's rule | Log + handover screen | NOT TESTED |

## SW — Save switching and stale work

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| SW01 | Active player order in A | Immediately quit to menu and load B | No order of A appears in B; log shows "Restored … for save-…" only for B's own orders | Log | NOT TESTED |
| SW02 | Dealer appointment waiting in A | Switch to B, then back to A | A's appointment is back, reservations rebuilt from the dealer's real stock | Log, Diagnostics reservation count | NOT TESTED |
| SW03 | — | Switch saves 5 times quickly | No error, no file of one save containing another's id; loading screen not visibly slower | Log, `saves/` listing | NOT TESTED |

## RS — Restart and quit

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| RS01 | Accepted order (OG Kush allowed) | Change Weed to GDP only, Save, save the game, quit, restart, load | The accepted order still accepts OG Kush; a new order accepts GDP only | Handover screen for both | NOT TESTED |
| RS02 | Pending changes | Quit normally right after accepting an order | Log has no "could not be completed before quitting" warning; `.orders.json` contains the order | Log, file | NOT TESTED |
| RS03 | — | End the game process from Task Manager during play | Next start loads; every mod file is either the previous or the newer complete version | Log, files | NOT TESTED |

## WR — Write failures (needs Windows file permissions)

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| WR01 | Make `saves/<id>.orders.json` read-only | Accept an order | Log: "Could not write … retried"; Diagnostics shows a failing pending write; the game keeps running | Log, screenshot | NOT TESTED |
| WR02 | WR01 | Remove read-only | Log: "written after N failed attempt(s)"; pending writes 0 | Log | NOT TESTED |
| WR03 | Make the settings file read-only | Change a rule, press Save | "Settings were NOT saved"; the rule is not shown as saved; nothing lost on disk | Screenshot | NOT TESTED |

## CR — Damaged files and recovery

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| CR01 | Saved rules, then a second Save (so `<id>.json.bak` exists) | With the game closed, cut `<id>.json` in half; load the save | Warning that settings are damaged; the backup is shown as unsaved changes; mod sales off (vanilla) until Save | Screenshot, log | NOT TESTED |
| CR02 | CR01 | Press Save | Rules restored; `<id>.json.corrupt-<time>` holds the damaged bytes | File listing | NOT TESTED |
| CR03 | No `.bak` | Damage `<id>.json`, load | Defaults shown, mod sales off, nothing applied automatically | Screenshot | NOT TESTED |
| CR04 | — | Copy save A's `<id>.json` over save B's | B treats it as damaged ("different save"), sales off | Log | NOT TESTED |
| CR05 | — | Damage `<id>.orders.json`, load | Orders of that save use vanilla rules; after the next order change a `.corrupt-` copy exists | Log, listing | NOT TESTED |
| CR06 | — | Damage `<id>.dealer-alerts.json` (with a `.bak`), load | History recovered from the backup; no repeated out-of-stock texts | Log, phone messages | NOT TESTED |

## NS — Newer and older versions

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| NS01 | Edit `<id>.json` to `"schemaVersion": 3` | Load | "saved by a newer version"; Save refused; file byte-identical afterwards | Screenshot, file hash | NOT TESTED |
| NS02 | Edit `<id>.orders.json` to `"schemaVersion": 2` | Load, accept an order | Diagnostics "orders newer version, kept read-only"; file unchanged | Screenshot, file hash | NOT TESTED |
| NS03 | A Phase 01–04 settings file (schema 1) | Load, Save, Save again | Same permissions; `.schema1.bak` created once and never changed | Files | NOT TESTED |
| NS04 | Phase 05 files (no `save` field) | Load with the Phase 06 build | Everything loads as before; the field is added on the next write | Files | NOT TESTED |

## DA / QL / NT — Dealers after reload

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| DA01 | Dealer appointment reserved | Save, restart, load | Appointment restored waiting; reservation rebuilt only from the dealer's current inventory | Diagnostics, log | NOT TESTED |
| DA02 | Dealer lost stock while the game was closed (load an older game save) | Load | No phantom reservation; appointment waits; shortage text at most once | Log | NOT TESTED |
| DA03 | Dealer handover processed | Restart before the contract ends | Not paid, removed or counted twice | Cash, inventory, log ("reported again; ignored") | NOT TESTED |
| QL01 | Weed minimum Premium; dealer has Poor and Premium stock | Restart with an appointment waiting | Only Premium units are reserved and handed over | Inventory before/after | NOT TESTED |
| NT01 | Dealer sent "out of Weed" | Restart, dealer still empty | No second text | Phone | NOT TESTED |
| NT02 | NT01 | Restock, empty again | One new text | Phone | NOT TESTED |

## RT / DG — Reset and diagnostics

| ID | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| RT01 | Custom rules saved | Settings → Reset to defaults → confirm → Save | Defaults active; previous rules kept in `<id>.json.bak` | Files | NOT TESTED |
| DG01 | — | Open Diagnostics in each state above | "Save data" and "Runtime files" lines match the state; no raw folder paths shown or logged | Screenshots, log search for the save folder path | NOT TESTED |

## RG6 — Regression

| ID | Action | Expected | Status |
| --- | --- | --- | --- |
| RG601 | Re-run the Phase 03–05 core scenarios (player handover, dealer handover, offers, fair pricing, presets) | Unchanged behaviour | NOT TESTED |
| RG602 | Check the game's own save folder after all tests | No file added or changed by the mod | NOT TESTED |

## Assumptions only the game can confirm

See [PHASE_06_ANALYSIS.md](PHASE_06_ANALYSIS.md) §9 (save folder path stability, NoSave between loads, flush timing,
`OnDeinitializeMelon` on quit, pruning of contracts missing after loading an older game save, dealer id stability).
