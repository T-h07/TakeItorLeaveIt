# Architecture

Status: Phase 01 (product discovery & rule engine). This file separates what exists in code from what is **planned**.

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
| **UI** (Phase 02: `Game/PhoneMenu` folder, not a project) | Core, Unity UI, S1API 3.2.3 | business rules (asks `ModMenuController`) |
| **Sales hooks** (Phase 03/04: `Game/Sales` folder) | Core `Sales/` and `Dealers/`, HarmonyX, Assembly-CSharp interop | sales decisions (asks `PlayerSaleCoordinator` / `DealerSaleCoordinator`) |
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
| `PatchManager`, `IPatchModule` | Game | Explicit, **all-or-nothing** patch application (`[assembly: HarmonyDontPatchAll]`). On failure it calls `UnpatchSelf()` so the game never runs half-patched. **Zero modules registered (Phase 00 and Phase 01)** |

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

## 5. Central eligibility engine (implemented in Phase 01)

Full description: [PRODUCT_DISCOVERY.md](PRODUCT_DISCOVERY.md).

| Component | Location | Responsibility |
| --- | --- | --- |
| `ProductKey`, `CategoryKey`, `ProductDescriptor` | Core `Products/` | Stable identities (game ID, never display name), unlock state, recipe fingerprint |
| `ProductCatalogService`, `ProductCatalogSnapshot` | Core `Discovery/` | Per-save immutable catalog; `NoSave / Loading / Ready / Failed / Unsupported`; stale results discarded |
| `SaveLifecycleCoordinator`, `SaveIdentity` | Core `Discovery/` | Save transitions from observations; hashed save identity |
| `PolicyConfiguration`, `CategoryPolicy`, `IPolicyProvider` | Core `Policies/` | Validated, immutable, revisioned rules (AnyUnlocked default) |
| `SaleEligibilityPolicy` | Core `Policies/` | The single rule set with machine-readable `EligibilityReason`s |
| `InventoryAllocationPlanner` | Core `Inventory/` | Deterministic, non-mutating stack allocation |
| `GameProductDiscoverySource`, `GameSaveProbe`, `ProductDiscoveryRuntime` | Game `Discovery/` | Read-only reflection adapter, polled about once per second |

Everything in Core is pure and deterministic: no game calls and no side effects, so it is fully unit-testable.
The Phase 01 game adapter uses **runtime reflection** (`Il2Cpp`-prefixed name first, then the Mono name),
which compiles without game assemblies and fails closed when a member is missing. Later phases apply
results only through **vanilla** completion paths (exactly-once payment/XP/inventory).

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

### Outcome (Phase 02, implemented)

Option 1 was built. The spike was replaced by compiling against the owner's real files: reference assemblies
exported from Schedule I v0.4.7f12 (IL2CPP) with **S1API 3.2.3.0** (`S1API.Il2Cpp.MelonLoader.dll`, built against
MelonLoader 0.7.3 and Il2CppInterop 1.5.1, the same versions as our NuGet references). No `UI` project was
created: the phone app is a folder (`Game/PhoneMenu`) compiled only into the IL2CPP DLL, and **only** when
`S1RefsDir` is set locally (`PHONE_APP` define). CI and the Mono DLL build without it. In-game behaviour is
**not yet verified** (see [PHASE_02_TESTING.md](PHASE_02_TESTING.md)). No hotkey fallback was built.

## 6a. Mod menu (Phase 02)

```text
 game state ──reflection──► ProductDiscoveryRuntime ──► ProductCatalogService (Core, per-save snapshot)
                                                              │ StateChanged
                                                              ▼
 JsonPolicyStore (UserData/…/saves/<id>.json) ◄──Load/Save── ModMenuController (Core) ──► InMemoryPolicyProvider
                                                              ▲   ViewVersion            (saved policy → player sales)
                                                              │ actions / views
                                    TakeItOrLeaveItPhoneApp (Game/PhoneMenu, S1API + Unity UI)
```

| Component | Location | Responsibility |
| --- | --- | --- |
| `ModMenuController` | Core `Menu/` | All menu state: screen, selected category, search, paging, feedback, reset confirmation; **draft** vs **saved** `PolicySettings`; views (category summaries, product pages, preview) computed with `SaleEligibilityPolicy`; session switching per save |
| `MenuTypes`, `MenuText`, `ProductSearch` | Core `Menu/` | View models, user-facing strings, case-insensitive multi-term search over name / effects / ID |
| `JsonPolicyStore`, `PolicySettingsJson`, `MiniJson` | Core `Persistence/` | Per-save JSON, schema version 1, atomic write (temp file + replace), unreadable files backed up and replaced by defaults with a warning |
| `TakeItOrLeaveItPhoneApp`, `UiKit`, `Theme`, `AppIcon` | Game `PhoneMenu/` | S1API `PhoneApp` (public parameterless ctor, discovered by S1API); builds Unity UI in `OnCreatedUI`; renders controller views; runtime-drawn icon |
| `PhoneMenuHost` | Game `PhoneMenu/` | Narrow, `NoInlining` boundary called from `OnUpdate` inside try/catch |

**UI lifecycle.** S1API creates a new app instance each time the phone home screen starts in the `Main` scene
and calls `OnCreatedUI(container)`. The newest instance becomes `Current`; `OnDestroyed` clears it. The mod's
`OnUpdate` calls `PhoneMenuHost.Tick()`; the app rebuilds only when it is open **and**
`ModMenuController.ViewVersion` changed. The app never subscribes to controller events, so scene reloads
cannot accumulate handlers. While the search field has focus the app sets `S1API.Input.Controls.IsTyping`, and
it releases it when focus is lost, the app closes, the phone closes or the app is destroyed.

**Layout.** `MenuLayout` (Core, unit-tested) turns the measured container size into rectangles for the
header, sidebar, body, Products header, list, pager and preview (two columns, or stacked when narrow). The app
re-measures the container every frame it is open and re-places regions when the size changes. Scroll lists
are built by `UiKit.ScrollList` (not S1API's `ScrollableVerticalList`, whose content is 100 units wider than
its viewport). `LayoutProbe` logs a bounds check per screen and size.

**Draft vs saved.** Every edit changes the draft only (the preview shows "Showing unsaved changes"). **Save**
validates, writes the file and only then publishes the policy to `InMemoryPolicyProvider` and shows
"Settings saved."; a failed write keeps the draft and shows the error. **Discard** restores the saved
settings; **Reset** needs confirmation and then still needs Save.

**Catalog refresh.** The controller listens to `ProductCatalogService.StateChanged` (polled about once per
second by discovery). A new snapshot of the same save keeps the draft; a different save loads that save's
file (unsaved edits of the previous save are discarded with a warning); no save → *Waiting for active save*.
Selections of products that are not unlocked stay in the file but count as inactive.

**Failure handling.** S1API missing → one warning, no menu, rest of the mod unaffected. Any exception from the
phone app → logged once, menu disabled for the session. UI build failure → an error panel inside the app.
Menu initialization failure → the app shows "unavailable".

## 6b. Player sales (Phase 03)

Behaviour and limitations: [PLAYER_SALES.md](PLAYER_SALES.md). Hook evidence: [PHASE_03_PATCH_ANALYSIS.md](PHASE_03_PATCH_ANALYSIS.md).

```text
 QuestManager.ContractAccepted ─postfix─► PlayerSaleCoordinator.OnContractAccepted ─► CategoryOrderFactory
                                                    │ snapshot (CategoryOrder)               (policy + catalog)
                                                    ▼
                                             ActiveOrderBook ◄──► ActiveOrderStore (<save-id>.orders.json)
                                                    ▲
 Contract.GetProductListMatch ─prefix─┐             │
 HandoverScreenContractMode.GetError ─postfix─► GameSales.Validate ─► DeliveryValidator ─► InventoryAllocationPlanner
                                      └──────────► DeliveryMatch (quality only)               └─► SaleEligibilityPolicy
 Customer.ProcessHandover ─postfix─► ObserveCommit (once)
 Contract.Complete/End/Expire/Fail ─postfix─► End (snapshot removed)
```

| Component | Location | Responsibility |
| --- | --- | --- |
| `CategoryOrder`, `CategoryOrderFactory` | Core `Sales/` | Acceptance-time snapshot; decides managed vs vanilla (dealer, disabled, empty, mixed categories, catalog/policy not ready) |
| `DeliveryValidator`, `DeliveredItem`, `DeliveryValidation` | Core `Sales/` | Pre-transfer validation against the snapshot; reuses the planner/eligibility policy; exact units; mixed rule; player-facing reason |
| `DeliveryMatch` | Core `Sales/` | Match value from per-item quality ratings (recipe term removed) |
| `SaleTransaction`, `ActiveOrderBook` | Core `Sales/` | State machine (Pending → Validated/Rejected → CommitObserved → Completed; Failed/Canceled/Expired), exactly-once commit observation, cleanup, pruning |
| `ActiveOrderStore` | Core `Sales/` | Per-save orders JSON (schema 1, atomic write, invalid entries dropped → vanilla) |
| `PlayerSaleCoordinator` | Core `Sales/` | Single entry point for the hooks; authority gate (single player host); policy-ready gate |
| `PlayerSalesStatus` | Core `Sales/` | Status report ("Offer hook: Active" ...); "Enabled" only when everything is |
| `PlayerSalesHost` | Game | Binds the coordinator to the active save, background persistence, status refresh |
| `PlayerSalesPatches`, `GameSales`, `SalesSession` | Game `Sales/` (`GAME_PATCHES` only) | Exact-signature Harmony hooks, item/contract adapters with a validation cache, authority and pruning timers |

`GAME_PATCHES` is defined only for the IL2CPP build when `S1RefsDir` contains `Assembly-CSharp.dll`. The
patch module is registered through `PatchManager` (all-or-nothing): if any target is missing, every mod patch
is removed and the status report says *Unavailable*.

## 6c. Dealer sales (Phase 04)

Behaviour and limitations: [DEALER_SALES.md](DEALER_SALES.md). Hook evidence: [PHASE_04_PATCH_ANALYSIS.md](PHASE_04_PATCH_ANALYSIS.md).

```text
 QuestManager.ContractAccepted (dealer set) ─postfix─► DealerSaleCoordinator.OnContractAccepted
        │  CategoryOrderFactory.CreateForDealer (same snapshot rules)      │ DealerStock (GameDealers.Stock)
        ▼                                                                  ▼
 DealerAppointment (Waiting / Reserved / Committing / CommitObserved) ◄─► ReservationLedger (per dealer, per product)
        ▲                                     └─ TryReserve → InventoryAllocationPlanner → SaleEligibilityPolicy
 Dealer.OnTick ─postfix (D2, throttled)─► Reconcile: invalidate lost stock, retry waiting (bounded)
 Dealer.RemoveContractItems ─prefix (D1)─► PrepareHandover → game's RemoveAndReturnProductFromInventory per line
                                            → CompleteHandover (hold released)
 Contract.GetProductListMatch / Customer.ProcessHandover / Contract.Complete... ─ shared Phase 03 hooks
 DealerOrderStore (<save-id>.dealer-orders.json: snapshots only, never reservations)
```

| Component | Location | Responsibility |
| --- | --- | --- |
| `DealerStock` | Core `Dealers/` | Immutable per-dealer stock snapshot in units; invalid/duplicate stacks ignored; change signature |
| `ReservationLedger`, `Reservation` | Core `Dealers/` | Atomic, idempotent, per-owner holds; states Proposed/Reserved/Committing/CommitObserved/Released/Expired/Invalidated; reconcile (newest first); invariant check |
| `DealerAppointment`, `DealerSaleCoordinator` | Core `Dealers/` | Appointment state machine incl. WaitingForEligibleStock (strict: no return to vanilla); handover plan, withhold and rollback; dealer re-homing; exactly-once settlement observation; bounded replanning |
| `DealerOrderStore` | Core `Dealers/` | Per-save appointment JSON (shares the order snapshot format with `ActiveOrderStore`) |
| `DealerSalesStatus` | Core `Dealers/` | "Dealer sales: Active/Unavailable/Disabled", counts, last error |
| `DealerSalesPatches`, `GameDealers` | Game `Sales/` (`GAME_PATCHES` only) | D1/D2 hooks by exact signature; dealer id and inventory adapter; reconcile and notify throttle |
| `OfferPolicy`, `OfferFilter` | Core `Sales/` | Offer decision per candidate with `SaleEligibilityPolicy` (no second engine); counters |
| `FairPriceRule`, `ProductPrices`, `IProductPriceSource` | Core `Sales/` | Fixed-quote rule at acceptance; price snapshot stored with the order |
| `DealerStockMonitor`, `DealerStockAlert`, `DealerAlertState`/`DealerAlertStore`, `DealerRestockNotificationCoordinator` | Core `Dealers/` | Restock decisions (Type A/B), dedup, retry, per-save alert state |
| `OfferPatches`, `GamePrices`, `GameDealerMessagingAdapter` | Game `Sales/` (`GAME_PATCHES` only) | O1 offer-pool postfix; `ProductDefinition.Price` reader; native `MSGConversation.SendMessage` |

Enhancements: [PHASE_04_ENHANCEMENTS.md](PHASE_04_ENHANCEMENTS.md).

## 7. Configuration and persistence strategy (Phase 02 / 06)

- Implemented in Phase 02: **mod-owned JSON**, never inside vanilla save files, one file per save at
  `<game>/UserData/TakeItOrLeaveIt/saves/<save-id>.json`, where `<save-id>` is the Phase 01 hashed
  `SaveIdentity` of the save folder. Schema: `{schemaVersion, enabled, defaultMode, mixedDeliveries, categories:{key:{mode, selectedProducts[]}}}`
  (`mixedDeliveries` added in Phase 03, optional, default true).
- Phase 03 adds `<save-id>.orders.json` next to it: the active category-order snapshots
  (`{schemaVersion, orders:[{contract, category, requiredUnits, allowedProducts[], requestedProducts[], mixedDeliveries, policyRevision, committed}]}`).
- Phase 04 adds `<save-id>.dealer-orders.json`: managed dealer appointments
  (`{schemaVersion, appointments:[{contract, dealer, sequence, category, requiredUnits, allowedProducts[], requestedProducts[], mixedDeliveries, policyRevision, committed}]}`).
  Reservations are deliberately not persisted; they are rebuilt from live dealer inventory after loading.
- Phase 04 enhancements add optional `pricing`, `quotedValue` and `unitPrices{}` to each order/appointment entry
  (schema stays 1; entries without them load through the legacy path) and `<save-id>.dealer-alerts.json`, the
  restock-message dedup state (`{schemaVersion, categories:[...], shortages:[...]}`).
- Deferred to Phase 06: migrations beyond schema 1, global (non-save) settings, export/import, backups/rotation.
- Selections reference stable product IDs. Unknown or locked IDs are treated as inactive and never unlocked.
- MelonPreferences is not used: dynamic per-save product lists need our own file.

## 8. Multiplayer considerations (Phase 07)

- Schedule I networking is **probably FishNet** (PATCH_MAP §7). The host must stay authoritative for
  eligibility decisions, inventory, payment and dealer sales.
- Clients receive the host's effective policy; client-side menus are read-only for host-owned rules.
- RPC method names contain build-specific hashes, so they are never patch targets.
- None of this is verified yet; OBS-05 in the patch map defines the first test.
- Phase 03 verified (call graph) that customer handovers settle through a FishNet server RPC
  (`ProcessHandoverServerSide`) and that acceptance checks `InstanceFinder.IsServer`. Until Phase 07, player
  sales are active only on a server with at most one player; otherwise every hook defers to vanilla.
- Phase 04 verified that the dealer handover routine checks `InstanceFinder.IsServer` and that the dealer's
  payment is a server RPC (`SubmitPayment`); dealer sales use the same single-player-host gate.

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
| Menu logic in Core, phone app only renders | Unit-testable without the game; the UI can be replaced (e.g. hotkey fallback) without touching rules |
| Phone app compiled only with local references | Game/S1API assemblies are proprietary or not on NuGet for IL2CPP; CI stays reproducible |
