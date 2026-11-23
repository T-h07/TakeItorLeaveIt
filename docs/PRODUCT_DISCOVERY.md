# Product Discovery and Sale Eligibility (Phase 01)

Status: **implemented and unit-tested; in-game behaviour pending** (see [PHASE_01_TESTING.md](PHASE_01_TESTING.md)).
Phase 01 is **strictly observational**. It reads game data and logs what it would allow. It installs no
Harmony patches and does not change orders, customers, dealers, inventory, money, XP or unlocks.

## 1. Overview

```text
 Game (read-only)                         Core (pure, unit-tested)
 ─────────────────                        ───────────────────────────────────────────────
 GameSaveProbe ──SaveObservation──► SaveLifecycleCoordinator ──► ProductCatalogService
   LoadManager (1 poll/s)                                          │ BeginSave / Refresh / EndSave
 GameProductDiscoverySource ◄──────── IProductDiscoverySource ◄────┘
   ProductManager.DiscoveredProducts                               ▼
                                                     ProductCatalogSnapshot (immutable, per save)
                                                                   ▼
 IPolicyProvider ── PolicyConfiguration ──► SaleEligibilityPolicy ──► EligibilityDecision / CategoryEligibility
                                                                   ▼
                       InventoryStack[] ──► InventoryAllocationPlanner ──► AllocationPlan (no mutation)
```

| Type | Project / folder | Role |
| --- | --- | --- |
| `ProductKey`, `CategoryKey` | Core `Products/` | Validated identity values. Product IDs are case-sensitive game IDs; categories are lowercased |
| `ProductDescriptor`, `CategoryDescriptor`, `UnlockState` | Core `Products/` | One product: key, category, display name (label only), unlock state, effect IDs, declared base product, recipe fingerprint |
| `ProductCatalogSnapshot` | Core `Discovery/` | Immutable catalog of **one** save session at one generation |
| `ProductCatalogService` | Core `Discovery/` | Owns the catalog lifecycle and states (`NoSave`, `Loading`, `Ready`, `Failed`, `Unsupported`) |
| `SaveLifecycleCoordinator`, `SaveObservation`, `SaveIdentity` | Core `Discovery/` | Turns save observations into lifecycle calls; hashes the save folder path |
| `CatalogReport` | Core `Discovery/` | Diagnostic log lines |
| `CategoryPolicy`, `PolicyConfiguration`, `IPolicyProvider` | Core `Policies/` | Validated, immutable, revisioned configuration |
| `SaleEligibilityPolicy` | Core `Policies/` | **The** eligibility rule set, with machine-readable `EligibilityReason`s |
| `ProductMatcher` | Core `Matching/` | Category and identity matching; never by display name |
| `InventoryAllocationPlanner` | Core `Inventory/` | Deterministic, non-mutating stack allocation plan |
| `GameReflection`, `GameProductDiscoverySource`, `GameSaveProbe`, `ProductDiscoveryRuntime` | Game `Discovery/` | Read-only game adapter |

## 2. Product identity

- **Identity is the game's product definition ID** (`ItemDefinition.ID`, probable; PATCH_MAP PRD-10). Mixes get
  their own ID from the game (`FinishAndNameMix(productID, mixID, …)`, PRD-05). `ProductKey` trims the ID,
  rejects empty, overlong (>256) or control-character IDs, and compares it **ordinally and case-sensitively**.
- **Display names are labels only.** Two products with the same name stay distinct, and nothing is ever matched by name or substring.
- **Recipe fingerprint** = `category:effect1+effect2…` (effect IDs distinct and sorted). This is a grouping aid
  for later recipe rules and is never used as identity.
- **Base product** is only known when declared. Phase 01 never infers it from names. The game adapter does
  not set it yet, because the game member for it is unverified.
- Duplicate IDs in one discovery: identical entries are merged (Unlocked beats Locked, Unknown never upgrades).
  Entries that conflict (different category or effects) are **excluded** and reported as issues.

## 3. Categories

Categories come from the data, not from a hard-coded list. The game adapter reads `ProductDefinition.DrugType`
(enum `EDrugType`, PRD-06/07) and uses the enum name, lowercased, as the key: `marijuana`, `methamphetamine`, `cocaine`,
`shrooms`, …. Only drug types that occur among the discovered products become categories. A request for any
other category returns `UnsupportedCategory`.

## 4. Unlock detection

- **Source:** `ProductManager.DiscoveredProducts` (PRD-01, probable). Every product in it is `Unlocked`.
- Products the save has not discovered are **absent**, so they can never be eligible. `Locked` and `Unknown`
  exist in the model for adapters that can enumerate locked products; the policy treats both as not
  eligible (`ProductLocked`, `UnlockStateUnknown`).
- **Owning is not unlocking.** Inventory stacks are checked against the catalog. Holding an item never
  makes it eligible.

## 5. Save lifecycle and isolation

`ProductDiscoveryRuntime` polls `LoadManager` **about once per second** from `OnUpdate`. When it is not due,
the only per-frame cost is a Stopwatch read. It polls again immediately on every scene load.
`SaveLifecycleCoordinator` decides:

| Observation | Action |
| --- | --- |
| No `LoadManager` instance, or not loaded and not loading | `EndSave` → `NoSave` |
| Loading starts, or a different save identity is seen | `BeginSave` → new session, `Loading`, no snapshot |
| Same save, loading finished | `Refresh` → `Ready` (or `Failed`/`Unsupported`) |
| Same save, discovered-product count changed | `Refresh` → new generation |
| Loaded but no save identity | `EndSave` (fail closed) |
| `LoadManager` type or members missing | `MarkUnsupported` |

- **Save identity** is a SHA-256 hash of the normalised `LoadedGameFolderPath`, for example `save-3fa2c19b04de`.
  The raw path is never logged or stored.
- Every `BeginSave` creates a new session ID and drops the previous snapshot. A result captured for an older
  session is discarded (`StaleDiscarded`), so **unlocks never merge across saves**. Reloading the same save
  also starts a new session.
- A failed refresh leaves **no snapshot**. Stale products are never shown as current, and a failure never
  looks like an empty catalog.

## 6. Policy modes and the evaluation order

`SaleEligibilityPolicy.Evaluate(policy, snapshot, requestedCategory, product)` applies these rules in order, and the first one that fails decides:

| # | Rule | Reason |
| --- | --- | --- |
| 1 | Global switch off | `ModDisabled` |
| 2 | Invalid category/product key | `InvalidRequest` |
| 3 | No Ready snapshot | `CatalogNotReady` |
| 4 | Category not in catalog | `UnsupportedCategory` |
| 5 | Product not in catalog | `ProductUnknown` |
| 6 | Product belongs to another category | `WrongCategory` |
| 7 | Category mode `Disabled` | `CategoryDisabled` |
| 8 | Not unlocked | `ProductLocked` / `UnlockStateUnknown` |
| 9 | `SelectedOnly` with an empty selection | `NoProductsSelected` (**never** "all") |
| 10 | `SelectedOnly` and not selected | `ProductNotSelected` |
| — | Otherwise | `Allowed` |

- **AnyUnlocked** (the default for every category): every unlocked product of the category.
- **SelectedOnly:** selected **and** unlocked. A selection that is unknown, locked or in the wrong
  category is **inactive**: it is kept (it may become valid later) and reported in
  `CategoryEligibility.InactiveSelections`, but it never allows anything.
- **Disabled:** nothing in the category is eligible. Other categories are unaffected.
- `EvaluateCategory` / `EvaluateAll` summarise a category: eligible products, unlocked count, selection count and inactive selections.
  Decisions carry the catalog generation and policy revision so later phases can revalidate them.

## 7. Configuration

`PolicyConfiguration` is immutable, and every change increments `Revision`. `FromSettings(PolicySettings)` accepts
untrusted input (the shape in [MOD_MENU_SPEC.md](MOD_MENU_SPEC.md)) and never throws. Problems are reported as `ConfigurationIssue`s:

| Input problem | Result |
| --- | --- |
| No settings | Defaults (enabled, AnyUnlocked) |
| Schema newer than supported | Every category `Disabled` |
| Unknown mode (default or per category) | `Disabled` |
| Invalid category key / product key | Entry dropped |
| Duplicate category (case variants) / duplicate product | Later entry ignored / counted once |
| Well-formed but stale product ID | Kept, but inactive until the active save has it unlocked |

Phase 01 uses `InMemoryPolicyProvider` with defaults. Persistence and the menu come in Phases 06 and 02.

## 8. Inventory allocation

`InventoryAllocationPlanner.Plan(category, quantity, stacks, policy, snapshot)` is pure. It returns an
`AllocationPlan` and **never removes, reserves or changes** stacks, the snapshot or the policy (tested).

1. `quantity <= 0` → `InvalidQuantity`. If the category has nothing eligible → `CategoryNotEligible` with the category reason.
2. Stacks that are null, have a blank ID or an invalid product, have a non-positive quantity, or are **duplicated** are rejected with a reason.
   Every occurrence of a duplicated stack ID is rejected, so a stack can never be counted twice.
3. Each remaining stack runs through `SaleEligibilityPolicy.Evaluate`. Ineligible stacks are rejected with the eligibility reason.
4. Eligible stacks are **sorted by stack ID (ordinal)**, so input order never changes the result.
5. The eligible quantity is summed as 64-bit with overflow checks. Short → `NoEligibleStock` or `InsufficientEligibleQuantity`.
6. Otherwise the planner takes `min(remaining, stack quantity)` from each stack in order. Allocations sum exactly to the request, and mixed eligible products are combined.

Later phases must revalidate a plan against live inventory, `CatalogGeneration` and `PolicyRevision` before acting on it.

## 9. Performance and failure handling

- Reflection members and types are resolved once and cached (`GameReflection`). Discovery runs only on
  save load and when the discovered-product count changes, never per frame.
- Snapshots are immutable. Readers never see a half-built catalog, and old snapshots stay consistent.
- Every adapter miss fails closed: a missing type or member → `Unsupported`, a null list → `Failed`.
  An exception inside a poll is logged. After 3 consecutive errors, discovery stops and the catalog
  is marked `Unsupported`. A faulty `StateChanged` subscriber is contained.
- The policy and planner are deterministic and have no global state.

## 10. Diagnostics (log lines)

```text
[TakeItOrLeaveIt] Product discovery initialized (read-only; polling save state about once per second).
[TakeItOrLeaveIt] Phase 01 running in read-only mode: products are discovered and evaluated, nothing in the game is changed.
[TakeItOrLeaveIt] Product catalog: Loading [session 1 (save save-3fa2c19b04de)]
[TakeItOrLeaveIt]   No products are eligible until the catalog is Ready (fail-closed).
[TakeItOrLeaveIt] Save detected (save-3fa2c19b04de); building product catalog.
[TakeItOrLeaveIt] Product catalog: Ready [session 1 (save save-3fa2c19b04de)]
[TakeItOrLeaveIt]   Generation 1: 4 product(s) in 1 category(ies).
[TakeItOrLeaveIt]   [marijuana] 4 known, 4 unlocked; mode anyUnlocked -> 4 eligible (Allowed)
[TakeItOrLeaveIt]     - ogkush "OG Kush" Unlocked effects: calming
```

(Product names and counts above are illustrative, not observed game data.)

## 11. Unknowns (to settle in game)

- Whether all members above exist with these names on v0.4.7f12 IL2CPP (all **probable**, none verified locally).
- Whether `DiscoveredProducts` holds base strains as well as mixes, and when it is filled during loading.
- Whether `DrugType` is the right category source for every product type.
- Whether the discovered-product count is a sufficient refresh trigger (a later phase may use the
  `FinishAndNameMix` postfix, PRD-05).
