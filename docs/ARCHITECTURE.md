# Architecture

Status: Phase 00 foundation. This file separates what exists in code from what is **planned**.

## 1. Repository layout

```text
TakeItorLeaveIt/
├── .github/workflows/build.yml        CI: restore, build (warnings = errors), test, package
├── src/
│   ├── TakeItOrLeaveIt.Core/          netstandard2.0, no Unity/MelonLoader/game references   [exists]
│   └── TakeItOrLeaveIt.Game/          MelonLoader entry point, Harmony infrastructure          [exists]
│                                       net6.0 -> TakeItOrLeaveIt.Il2Cpp.dll, net472 -> TakeItOrLeaveIt.Mono.dll
├── tests/TakeItOrLeaveIt.Core.Tests/  xUnit, net10.0                                          [exists]
├── tools/
│   ├── TakeItOrLeaveIt.Inspector/     read-only game-assembly metadata inspector              [exists]
│   ├── Probe-ScheduleOne.ps1          read-only environment probe                             [exists]
│   └── Package-Mod.ps1                lays out Mods/ + UserLibs/ per runtime                  [exists]
├── docs/                              ENVIRONMENT, PATCH_MAP, ARCHITECTURE, BUILDING, INSTALLATION, MOD_MENU_SPEC
├── Directory.Build.props              shared compiler settings, version 0.0.1-dev
├── local.build.props.example          template for the git-ignored machine-specific settings
├── global.json                        .NET SDK 10.0.1xx+ (latestFeature)
└── TakeItOrLeaveIt.sln
```

**Deviation from the proposed structure:** `TakeItOrLeaveIt.UI`, `.Persistence` and `.Multiplayer` were
**not** created in Phase 00. They would have been empty projects, and their target frameworks and
references depend on decisions made later: the UI depends on S1API/Unity UI (§6), and Persistence may need
no game references at all. Each is added in the phase that introduces real code (02, 06, 07), following the
boundaries in §2.

## 2. Project boundaries and dependency flow

```text
               (no game refs)                          (game + loader refs)
 Core.Tests ──► Core ◄──────────── Persistence* ◄──┐
                 ▲                                 │
                 ├──────────── Multiplayer* ◄──────┤
                 │                                 │
                 └──────────────────────────── Game ──► UI*  (*planned)
                                                   │
                                         MelonLoader / HarmonyX / Schedule I assemblies / (S1API?)
```

| Project | May reference | Must not reference |
| --- | --- | --- |
| **Core** | .NET Standard 2.0 only | Unity, MelonLoader, Harmony, game assemblies |
| **Game** | Core, MelonLoader, HarmonyX, game assemblies (Phase 01+) | UI internals |
| **UI** (Phase 02) | Core, Game adapters, Unity UI, S1API (if adopted) | business rules (asks Core) |
| **Persistence** (Phase 06) | Core; ideally nothing game-specific | Harmony |
| **Multiplayer** (Phase 07) | Core, Game, FishNet (via game assemblies) | UI |

Core targets **netstandard2.0** because it must load in both MelonLoader runtimes (.NET 6 for IL2CPP and
Unity Mono for the `alternate` branch). net472 cannot consume netstandard2.1.

## 3. What exists in Phase 00

| Component | Location | Responsibility |
| --- | --- | --- |
| `ModInfo` | Core | Name `TakeItOrLeaveIt`, author `T-h07`, version `0.0.1-dev`. Constants feed `MelonInfo`; a test keeps them in sync with `Directory.Build.props` |
| `IModLog` | Core | Logging abstraction (info / warning / error / exception) |
| `GameRuntimeDetector` | Core | Mono vs IL2CPP from on-disk markers; reports *Unknown* instead of guessing |
| `LoaderVersion`, `CompatibilityChecker` | Core | Wrong-build detection, loader version policy (built against 0.7.3, min 0.7.0, 0.7.1 + IL2CPP warning) |
| `StartupSequence` | Core | Runs named init steps; separates **unsupported environment** from **defect**; never throws to the loader |
| `TakeItOrLeaveItMod` | Game | `MelonMod` entry: logs environment, runs compatibility checks, initializes Harmony, logs scene loads (once per load, never per frame) |
| `MelonModLog` | Game | `IModLog` → `MelonLogger.Instance` (lines appear as `[TakeItOrLeaveIt] …`) |
| `PatchManager`, `IPatchModule` | Game | Explicit, **all-or-nothing** patch application (`[assembly: HarmonyDontPatchAll]`). On failure it calls `UnpatchSelf()` so the game never runs half-patched. **Zero modules registered in Phase 00** |

## 4. Game adapter design (Phase 01+)

- Game-facing code lives in `TakeItOrLeaveIt.Game` under one folder per subsystem (`Products/`, `Customers/`,
  `Dealers/`). Each adapter converts game objects into **Core value types** (for example a product snapshot with
  ID, category, effect IDs and quality), so Core never sees Unity or IL2CPP types.
- Runtime differences (the `Il2Cpp` namespace prefix and `Il2CppSystem` collections) are isolated in the adapters with
  `#if IL2CPP_BUILD` / `#if MONO_BUILD`. Code above the adapters is runtime-neutral.
- Each Harmony integration is an `IPatchModule` that **resolves its targets first** (with `AccessTools`) and
  throws if any are missing. The `PatchManager` rolls back and the feature reports "unavailable on this
  build" (see the Diagnostics screen in `MOD_MENU_SPEC.md`).
- Only targets listed in [PATCH_MAP.md](PATCH_MAP.md) with confidence `verified` may be patched.

## 5. Planned central eligibility engine (Phase 01)

One `SaleEligibilityPolicy` in Core, used by the menu preview, player handovers and dealer stock selection:

```text
Inputs:  policy (per category: AnyUnlocked | SelectedOnly | Disabled, selections, flags)
         discovered-product set (from the save, via adapter)
         requested order (category + quantity [+ min quality])
         offered/available stacks (product snapshot + quantity)
Output:  decision (Accept / Reject) + reason (WrongCategory, NotUnlocked, ExcludedByPolicy,
         InsufficientQuantity, CategoryDisabled, UnsupportedBuild) + allocation (which stacks, how many)
```

It is pure and deterministic: no game calls and no side effects, so it is fully unit-testable. The game adapters only
supply snapshots and apply the result through **vanilla** completion paths (exactly-once payment/XP/inventory).

## 6. In-game menu: S1API evaluation (Phase 02 recommendation)

### Findings (from S1API source `e14b1d1`, 2026-11-20, and NuGet/Thunderstore metadata)

| Aspect | Finding | Status |
| --- | --- | --- |
| License / activity | MIT; actively maintained fork (ifBars) of KaBooMa's original; frequent releases (3.0 → 3.2.3 on NuGet) | Confirmed from repo/NuGet |
| Runtimes | Built twice (`MonoMelon` netstandard2.1, `Il2CppMelon` net6.0). `S1APILoader` plugin picks the right build at startup with `MelonUtils.IsGameIl2Cpp()` | Confirmed from source |
| NuGet package | `S1API.Forked` 3.2.3 contains **only** `lib/netstandard2.1/S1API.dll` (Mono build) | Confirmed by downloading the package |
| Phone apps | `abstract class PhoneApp`: `AppName`, `AppTitle`, `IconLabel`, `IconFileName`, optional `IconSprite`, `Orientation`, `OnCreatedUI(GameObject container)`, `OnPhoneClosed()`; handles registration, icon spawning, open/close | Confirmed from source |
| Lifecycle events | `GameLifecycle.OnPreLoad`, `OnLoadComplete`, `OnPreSceneChange`, `OnSaveInfoLoaded`, `OnSaveStart`, `OnSaveComplete` (wrapping `LoadManager`/`SaveManager`) | Confirmed from source |
| Products | `ProductManager.DiscoveredProducts`, `MethDiscovered`, `CocaineDiscovered`, `ShroomsDiscovered`, product definitions with `DrugType` and effect `Properties` | Confirmed from source |
| Compatibility with the owner's game build | Unknown: depends on the installed game version | **UNVERIFIED** |
| Known issue | S1APILoader ships a workaround for MelonLoader 0.7.1 + IL2CPP interop | Confirmed from source |

### Options compared

| | 1. S1API phone app | 2. Custom Unity UI on game systems | 3. Hotkey settings panel |
| --- | --- | --- | --- |
| Player experience | Native-feeling phone app (spec's primary choice) | Native-feeling if done well | Functional, clearly "mod-like" |
| Implementation effort | Low–medium (UI built in `OnCreatedUI` with Unity UI) | High (reverse-engineer phone app registration ourselves, per runtime) | Low–medium |
| Version coupling | S1API must support the game build (maintained by its authors) | We absorb every game update ourselves | Low (only Unity input/UI) |
| Runtime support | Both, via S1APILoader | Both, but double the reverse-engineering | Both |
| Extra install for players | Yes: S1API (+ loader plugin) | No | No |
| Maintenance risk | Third-party release cadence; IL2CPP-specific calls in our UI lose S1API's cross-compat | Highest | Lowest |

### Recommendation

**Phase 02: build the menu as an S1API `PhoneApp` (option 1), behind a thin `IMenuHost` boundary, and keep
option 3 (hotkey panel) as the documented fallback.** Options 2 and 3 cost more for a worse result. Before
starting, Phase 02 needs a time-boxed spike on the gaming PC: install the current S1API from Thunderstore,
confirm that it loads with the owner's game build and MelonLoader 0.7.3, and register an empty app. If the spike
fails, use option 3. S1API is **not** a dependency of Phase 00.

Option 1 also gives Phase 01 well-tested lifecycle events and product discovery wrappers. Phase 01 may still read
`ProductManager` directly (PATCH_MAP PRD-01) to avoid taking the dependency before the spike.

## 7. Configuration and persistence strategy (Phase 02 / 06)

- Settings are stored as **mod-owned JSON**, never inside vanilla save files: one global file plus one profile
  per save (keyed by the save's identity, to be determined in Phase 06 from `LoadManager`/save info).
- A schema version field and forward migrations (`schemaVersion` in `MOD_MENU_SPEC.md`).
- Selections reference stable product IDs. Unknown or locked IDs are treated as inactive and never unlocked.
- MelonPreferences may hold simple global toggles. Dynamic product allowlists need our own file because
  MelonPreferences is not designed for per-save lists.

## 8. Multiplayer considerations (Phase 07)

- Schedule I networking is **probably FishNet** (PATCH_MAP §7). The host must stay authoritative for
  eligibility decisions, inventory, payment and dealer sales.
- Clients receive the host's effective policy; client-side menus are read-only for host-owned rules.
- RPC method names contain build-specific hashes, so they are never patch targets.
- None of this is verified yet; OBS-05 in the patch map defines the first test.

## 9. Key decisions

| Decision | Rationale |
| --- | --- |
| Build **both** IL2CPP (net6.0) and Mono (net472) DLLs | Owner's runtime unknown; default branch is IL2CPP; costs nothing in Phase 00 |
| Reference MelonLoader via **NuGet** (`LavaGang.MelonLoader` 0.7.3), compile-only | Reproducible CI builds without game files; compiler checks every loader API we use |
| `HarmonyDontPatchAll` + `PatchManager` | Patches are explicit, logged and all-or-nothing; nothing is applied by accident |
| Core = netstandard2.0, no game refs | Testable without the game; loads in both runtimes |
| Core shipped as `UserLibs/TakeItOrLeaveIt.Core.dll` | MelonLoader's location for shared libraries; keeps `Mods/` to the single mod DLL |
| No fake game API stubs | Explicit project requirement; game-facing code is built only against real assemblies |
| Warnings as errors; nullable enabled | Catch defects early |
