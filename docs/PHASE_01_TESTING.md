# Phase 01 Testing

Automated tests cover the Core logic. In-game tests confirm that the read-only adapter finds real data
without changing gameplay. Real-game tests stay **PENDING** until game logs are provided. Unit tests
use synthetic data and never count as in-game evidence.

## 1. Automated tests

```powershell
dotnet build TakeItOrLeaveIt.sln -c Release
```

```powershell
dotnet test TakeItOrLeaveIt.sln -c Release --no-build
```

| Group | File | Covers |
| --- | --- | --- |
| A Identity | `ProductIdentityTests.cs` | Key equality/case, invalid and overlong IDs, same display name ≠ same product, fingerprints, declared base product only |
| B Discovery | `ProductDiscoveryTests.cs` | States, unlocked vs locked vs unknown, refresh generations, failures, exceptions, duplicates, categories |
| C Modes | `EligibilityPolicyTests.cs` | AnyUnlocked, SelectedOnly, Disabled, empty selection, global disable, unknown product, reasons |
| D Categories | `CategoryRestrictionTests.cs` | Wrong category, cross-category selections, unsupported category |
| E Allocation | `InventoryAllocationTests.cs` | Single/mixed stacks, restrictions, invalid/duplicate stacks, no mutation, order independence, overflow |
| F Saves | `SaveIsolationTests.cs`, `SaveLifecycleTests.cs` | Save A → B replacement, stale results discarded, reloads, menu exit, lifecycle decisions, anonymised identity |
| G Configuration | `PolicyConfigurationTests.cs` | Defaults, validation, unknown modes, newer schema, stale IDs inactive, round trip, provider |

## 2. In-game test (on the gaming PC)

No dependencies changed: still only MelonLoader 0.7.3, and S1API is not needed. Use a **test save, or
a copy of one**. The mod never writes to saves, but a copy is good practice.

### 2.1 Install

1. Get the build: the CI artifact `TakeItOrLeaveIt-<sha>` from the pull request's *Checks* tab, or run
   `pwsh tools/Package-Mod.ps1 -Configuration Release`, which writes `artifacts/package/`.
2. From the **`Il2Cpp`** folder (your game is IL2CPP), copy the files into the game folder (Steam → Schedule I → Manage → Browse local files), replacing the Phase 00 copies:
   - `Mods/TakeItOrLeaveIt.Il2Cpp.dll` → `<Schedule I>/Mods/`
   - `UserLibs/TakeItOrLeaveIt.Core.dll` → `<Schedule I>/UserLibs/` (**this changed in Phase 01, so replace it too**)

### 2.2 Startup (P01-T02, P01-T21)

Launch Schedule I from Steam. Open `<Schedule I>/MelonLoader/Latest.log` (or watch the MelonLoader console). Expect:

```text
[TakeItOrLeaveIt] TakeItOrLeaveIt 0.0.1-dev by T-h07 - phase 01 - Product discovery and rule engine; build target: Il2Cpp.
...
[TakeItOrLeaveIt] Product discovery initialized (read-only; polling save state about once per second).
[TakeItOrLeaveIt] Initialization completed.
[TakeItOrLeaveIt] Version 0.0.1-dev initialized successfully.
[TakeItOrLeaveIt] Phase 01 running in read-only mode: ...
```

At the main menu there should be **no** `Product catalog` warnings. If you see
`Product catalog: Unsupported ... Type ScheduleOne.Persistence.LoadManager not found`, send the log: it means the
type name differs on your build.

### 2.3 Discovery (P01-T03, T04, T05)

Load **save A**. Within a few seconds after loading finishes you should see:

```text
[TakeItOrLeaveIt] Save detected (save-xxxxxxxxxxxx); building product catalog.
[TakeItOrLeaveIt] Product catalog: Ready [session N (save save-xxxxxxxxxxxx)]
[TakeItOrLeaveIt]   Generation G: P product(s) in C category(ies).
[TakeItOrLeaveIt]   [marijuana] ... known, ... unlocked; mode anyUnlocked -> ... eligible (Allowed)
[TakeItOrLeaveIt]     - <id> "<name>" Unlocked effects: ...
```

Compare the listed products with the game's own product list (the phone's product app) for that save.
Every product you have discovered should appear, and nothing you have not discovered should. Note any
missing or extra entries, and any `issue:` lines.

### 2.4 Refresh (P01-T07)

In the same save, discover a **new** product (for example, create and name a new mix at a mixing station).
Within about a second of it appearing in your product list, a new `Product catalog: Ready` block with a
higher generation should list it. If no new block appears, the count trigger missed it. Report that.

### 2.5 Save isolation (P01-T08)

Exit to the main menu. Expect `Product catalog: NoSave`. Load a **different** save B with different
discoveries. The new block must show a different `save-…` identity and **only** save B's products.
Then reload save A and confirm its list matches step 2.3 (plus the product from 2.4).

### 2.6 Gameplay unchanged (P01-T21)

Play for a few minutes: accept a customer order, hand it over, and let a dealer sell if you have one. Behaviour
must be exactly vanilla. The mod only logs. There are no `Harmony`/patch lines other than
`No gameplay patches enabled (Phase 01 is read-only).`

### 2.7 What to send back

1. `<Schedule I>/MelonLoader/Latest.log` after steps 2.2–2.6. Product names and save identities in the log are
   fine to share, and no folder paths are logged.
2. A screenshot of the in-game product list for save A (to compare with the log).
3. One line per step: what you did and whether it matched.

## 3. Acceptance status

| ID | Test | How verified | Status |
| --- | --- | --- | --- |
| P01-T01 | Git branch setup | `feature/phase-01-policy-engine` from `develop` @ `ac89755` | PASS |
| P01-T02 | Existing foundation | Phase 00 tests still pass; startup lines in game | PASS (build/tests) · in-game PENDING |
| P01-T03 | Category discovery | In-game log §2.3 | PENDING |
| P01-T04 | Product identity | Group A tests; real IDs in §2.3 | PASS (core) · in-game PENDING |
| P01-T05 | Unlock detection | In-game log §2.3 | PENDING |
| P01-T06 | Locked products | Groups B/C/E tests | PASS |
| P01-T07 | Catalog refresh | Group B tests; in-game §2.4 | PASS (core) · in-game PENDING |
| P01-T08 | Save isolation | Group F tests; in-game §2.5 | PASS (core) · in-game PENDING |
| P01-T09 | Any Unlocked | Group C | PASS |
| P01-T10 | Selected Only | Group C | PASS |
| P01-T11 | Disabled | Group C | PASS |
| P01-T12 | Empty selection | Group C/E | PASS |
| P01-T13 | Category safety | Group D | PASS |
| P01-T14 | Single-stack allocation | Group E | PASS |
| P01-T15 | Mixed-stack allocation | Group E | PASS |
| P01-T16 | Invalid inventory | Group E | PASS |
| P01-T17 | Non-mutating behaviour | Group E | PASS |
| P01-T18 | Error handling | Groups B/F tests (adapter fail-closed paths are not exercised in game yet) | PASS (core) |
| P01-T19 | Automated tests | `dotnet test` | PASS |
| P01-T20 | Build verification | `dotnet build` (0 warnings, 0 errors, both runtimes) | PASS |
| P01-T21 | In-game compatibility | §2.2–2.6 | PENDING |
| P01-T22 | Documentation | This file, PRODUCT_DISCOVERY.md, README/ARCHITECTURE/PATCH_MAP/BUILDING/INSTALLATION updates | PASS |
| P01-T23 | CI verification | GitHub Actions on the PR | see PR |
| P01-T24 | GitHub delivery | PR to `develop` | see PR |
