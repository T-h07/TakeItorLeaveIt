# Patch Map — Schedule I integration points

> **Status (Phase 00):** research map. **Nothing in this file has been verified against an installed
> Schedule I build yet.** The development machine used for Phase 00 has no Schedule I installation
> (see [ENVIRONMENT.md](ENVIRONMENT.md)). Every entry is therefore **probable** (strong public evidence)
> or **UNVERIFIED / unresolved** (no evidence). Nothing here is patched by the mod in Phase 00.

## 1. How to read this map

### Evidence sources

| Code | Source | Strength |
| --- | --- | --- |
| **E1** | [S1API](https://github.com/ifBars/S1API) source, commit `e14b1d1` (2026-11-20; NuGet `S1API.Forked` 3.2.3 = commit `c81076d`). The member is accessed **directly in compiled code**, so it existed in the game build S1API was compiled against. S1API builds the same source for a Mono (`MonoMelon`) and an IL2CPP (`Il2CppMelon`) configuration. | Strong for the name; **game version unknown** |
| **E2** | S1API Harmony patch target written as `nameof(...)`/typed signature (compile-checked). | Strong |
| **E3** | S1API accesses the member **by reflection string** (`GetField("...")`, `TryGetFieldOrProperty`). S1API guards these, so the member may be absent or renamed in some builds. | Weak/medium |
| **E4** | FishNet code-generation naming (`RpcLogic___…`, `sync___get_value_…`) seen in S1API, implying networked members. | Medium |
| **—** | No public evidence found. | — |

**Confidence:** `verified` (seen in a local game build: none yet) · `probable` (E1/E2) · `unresolved` (E3 only, or no evidence: **UNVERIFIED**).

**Runtime availability:** "Both*" means S1API compiles the reference for Mono and IL2CPP. Under IL2CPP the namespace carries an `Il2Cpp` prefix (`Il2CppScheduleOne.Economy.Customer`) and collections are `Il2CppSystem.Collections.Generic.List<T>`. *Not verified locally.*

**Assembly:** S1API references `Assembly-CSharp.dll` and `ScheduleOne.Core.dll` (Mono), and `Assembly-CSharp.dll` and `Il2CppScheduleOne.Core.dll` (IL2CPP, MelonLoader-generated). Source code cannot show which of the two declares a type, so the assembly column says `Assembly-CSharp?` until the inspector confirms it.

### Turning "probable" into "verified"

On the gaming PC (read-only; see [ENVIRONMENT.md §4](ENVIRONMENT.md#4-collecting-evidence-on-the-gaming-pc)):

```powershell
dotnet run --project tools/TakeItOrLeaveIt.Inspector -- --game "<folder containing Schedule I.exe>"
```

`inspection-output/candidates.md` reports every ID below as FOUND / MEMBER MISSING / TYPE MISSING, with
the real signature and assembly. `keywords.md` lists methods that S1API does not expose (handover,
acceptance, payment...), which is how the **UNVERIFIED** rows get filled in.

---

## 2. Customers (`ScheduleOne.Economy`)

| ID | Assembly | Type | Member / signature | Purpose | Call flow | Evidence | Harmony approach (proposal) | Risk | Runtime | Confidence | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CUS-01 | Assembly-CSharp? | `ScheduleOne.Economy.Customer` | `CustomerData CustomerData` (property) + private field `customerData` | Static customer profile (preferences, standards, spend) | Read by order generation (UNVERIFIED) | E1 (property), E3 (field) | Read-only access | Low | Both* | probable | 01, 03 |
| CUS-02 | Assembly-CSharp? | `Customer` | `OfferContract(ContractInfo)` | Offers a contract (order) to the player | S1API → `Customer.OfferContract` | E1 | Prefix to rewrite the requested product list to "category + quantity" **or** leave it and relax validation instead (decide in Phase 03) | Medium | Both* | probable | 03 |
| CUS-03 | Assembly-CSharp? | `Customer` | `RequestProduct(Player)` | Customer asks a player for product (direct request) | S1API → `RequestProduct(Player.Local)` | E1 | Observe only (postfix logging) to find the downstream order/handover path | Low | Both* | probable | 03 |
| CUS-04 | Assembly-CSharp? | `Customer` | `CompletedDeliveries` (property) | Count of completed deliveries | Probably updated on successful handover | E3 | Read-only; a good *runtime observation* hook for "a sale completed exactly once" | Low | UNVERIFIED | unresolved | 03 |
| CUS-05 | Assembly-CSharp? | `Customer` | field `currentAffinityData` (`CustomerAffinityData`) | Live per-drug-type affinity | UNVERIFIED | E3 | Read-only | Low | UNVERIFIED | unresolved | 03 |
| CUS-06 | Assembly-CSharp? | `ScheduleOne.Economy.CustomerData` (ScriptableObject) | `PreferredProperties` : `List<ScheduleOne.Effects.Effect>` | **Preferred effects**: the main cause of recipe pickiness | Consumed by enjoyment/acceptance scoring (**UNVERIFIED**) | E1 | Do **not** mutate the shared ScriptableObject. Patch the *scoring* method that reads it (not yet identified) | High if mutated | Both* | probable | 03, 05 |
| CUS-07 | Assembly-CSharp? | `CustomerData` | `DefaultAffinityData` : `CustomerAffinityData`; `Standards` : `ECustomerStandard` (VeryLow…VeryHigh); `MinWeeklySpend`, `MaxWeeklySpend`, `MinOrdersPerWeek`, `MaxOrdersPerWeek`, `PreferredOrderDay`, `OrderTime`, `GuaranteeFirstSampleSuccess`, `CanBeDirectlyApproached` | Quality standard, demand frequency, order timing | Inputs to order generation (UNVERIFIED) | E1 | Read-only. `Standards` = **quality restriction** source (Phase 05) | Low | Both* | probable | 01, 05 |
| CUS-08 | Assembly-CSharp? | `CustomerAffinityData` / `ProductTypeAffinity` | `ProductAffinities` : `List<ProductTypeAffinity>`; `ProductTypeAffinity.DrugType` : `EDrugType`, `.Affinity` : `float` | Per-category liking: category-level demand | UNVERIFIED | E1 | Read-only; useful for category-based order generation | Low | Both* | probable | 03 |
| CUS-09 | — | UNVERIFIED | **Handover acceptance / offer evaluation** (where the customer decides to accept the delivered items) | Core of Phase 03 | UNVERIFIED | — | Expected: prefix/postfix on the evaluation method; must keep vanilla payment path | **High** | UNVERIFIED | unresolved | 03 |
| CUS-10 | — | UNVERIFIED | **Satisfaction / enjoyment calculation** after a deal | Penalties for "wrong" recipe | UNVERIFIED | — | Postfix to neutralise penalties caused only by recipe/effect mismatch | High | UNVERIFIED | unresolved | 03 |
| CUS-11 | — | `ScheduleOne.NPCs.Relation.NPCRelationData` | Relationship change method(s): only `Unlock(EUnlockType)` seen | Relationship progression | UNVERIFIED for deltas | E1 (`Unlock` only) | Do not patch; must stay vanilla | Medium | Both* | unresolved | 03 |

## 3. Orders / contracts (`ScheduleOne.Quests`, `ScheduleOne.Product`)

| ID | Assembly | Type | Member / signature | Purpose | Call flow | Evidence | Harmony approach (proposal) | Risk | Runtime | Confidence | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ORD-01 | Assembly-CSharp? | `ScheduleOne.Quests.Contract` | `ProductList ProductList` | **Requested products**: the exact-recipe requirement lives here | Created from `ContractInfo` | E1 | Read in validation; Phase 03 decides whether to rewrite entries or relax matching | High | Both* | probable | 03, 04 |
| ORD-02 | Assembly-CSharp? | `Contract` | `float Payment` | Agreed price | Paid on completion (UNVERIFIED where) | E1 | Never modify (exactly-once payment rule) | High | Both* | probable | 03 |
| ORD-03 | Assembly-CSharp? | `Contract` | `DeliveryWindow` : `QuestWindowConfig` (`WindowStartTime`, `WindowEndTime` : `int`, `IsEnabled`) | Delivery time window / expiry | UNVERIFIED | E1 | Read-only | Low | Both* | probable | 03, 06 |
| ORD-04 | Assembly-CSharp? | `ScheduleOne.Quests.ContractInfo` | ctor `(float payment, ProductList products, string deliveryLocationGUID, QuestWindowConfig deliveryWindow, bool expires, int expiresAfter, int pickupScheduleIndex, bool isCounterOffer)` (parameter names from S1API named arguments) | Serializable order description passed to `Customer.OfferContract` | `ContractInfo` → `Customer.OfferContract` → `Contract` | E1 | Natural point to tag mod-generated orders (Phase 03) | Medium | Both* | probable | 03 |
| ORD-05 | Assembly-CSharp? | `ScheduleOne.Product.ProductList` | field `entries` : `List<ProductList.Entry>` | Line items of an order | — | E1 | Read-only | Low | Both* | probable | 03, 04 |
| ORD-06 | Assembly-CSharp? | `ProductList.Entry` | `string ProductID`, `int Quantity`, `EQuality Quality` | **Orders reference an exact product ID**, a quantity and a minimum quality | — | E1 | Category matching = map `ProductID` → `ProductDefinition.DrugType` | Low | Both* | probable | 03 |
| ORD-07 | — | UNVERIFIED | Contract completion / failure / expiry / cancellation methods | Order lifecycle end states | UNVERIFIED | — | Postfix observation first | High | UNVERIFIED | unresolved | 03, 06 |
| ORD-08 | — | UNVERIFIED | Contract save/restore (`ScheduleOne.Persistence.Datas.*`) | Serialization of active orders | UNVERIFIED | — | Read-only; mod-generated orders must round-trip | Medium | UNVERIFIED | unresolved | 06 |

## 4. Products, recipes and unlocks (`ScheduleOne.Product`, `ScheduleOne.Effects`, `ScheduleOne.ItemFramework`)

| ID | Assembly | Type | Member / signature | Purpose | Call flow | Evidence | Harmony approach (proposal) | Risk | Runtime | Confidence | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| PRD-01 | Assembly-CSharp? | `ScheduleOne.Product.ProductManager` | `static List<ProductDefinition> DiscoveredProducts` | **Products discovered in the active save**: primary unlock source | Populated on load / discovery (UNVERIFIED) | E1 | Read-only, refreshed on `LoadManager.onLoadComplete` | Low | Both* | probable | 01, 02 |
| PRD-02 | Assembly-CSharp? | `ProductManager` | `static List<ProductDefinition> ListedProducts`, `FavouritedProducts` | Products listed for sale / favourites | — | E1 | Read-only (UI hint) | Low | Both* | probable | 02 |
| PRD-03 | Assembly-CSharp? | `ProductManager` | `static bool MethDiscovered`, `CocaineDiscovered`, `ShroomsDiscovered`; `static bool IsAcceptingOrders` | **Category-level unlock flags** | — | E1 | Read-only | Low | Both* | probable | 01 |
| PRD-04 | Assembly-CSharp? | `ProductManager` | `static ProductManager Instance`; `float GetPrice(ProductDefinition)`; `static float CalculateProductValue(ProductDefinition, float)` | Pricing | — | E1 | Never patch (economy safety) | — | Both* | probable | 03 (read) |
| PRD-05 | Assembly-CSharp? | `ProductManager` | `FinishAndNameMix(string, string, string, string)`, `SendFinishAndNameMix(string, string, string, string)`, FishNet RPC `RpcLogic___FinishAndNameMix_4237212381` | **Creates/discovers a new mixed product** (networked) | Mixing station → Send… (client) → RPC → FinishAndNameMix | E2, E4 | Postfix = "new product discovered" event for menu refresh | Medium (RPC hash is build-specific) | Both* | probable | 01 |
| PRD-06 | Assembly-CSharp? | `ScheduleOne.Product.ProductDefinition` (+ `WeedDefinition`, `MethDefinition`, `CocaineDefinition`, `ShroomDefinition`) | `EDrugType DrugType`; `List<DrugTypeContainer> DrugTypes`; `List<Effect> Properties`; `float Price`, `BasePrice`, `MarketValue`; `GetDefaultInstance(int)` | **Category + effect recipe of a product** | — | E1 | Read-only; identity source for the policy engine | Low | Both* | probable | 01 |
| PRD-07 | Assembly-CSharp? | `ScheduleOne.Product.EDrugType` | `Marijuana=0, Methamphetamine=1, Cocaine=2, MDMA=3, Shrooms=4, Heroin=5` (as mirrored by S1API) | Drug categories | — | E1 (mirror enum) | — | — | Both* | probable | 01 |
| PRD-08 | Assembly-CSharp? | `ScheduleOne.ItemFramework.EQuality` | `Trash, Poor, Standard, Premium, Heavenly` (as mirrored) | Quality tiers | — | E1 | — | — | Both* | probable | 05 |
| PRD-09 | Assembly-CSharp? | `ScheduleOne.Product.ProductItemInstance` | `Quality` (via quality item base) | An owned stack of product (inventory) | — | E1 | Read-only; **owning ≠ discovered** | Low | Both* | probable | 03, 04 |
| PRD-10 | — | UNVERIFIED | Stable unique product ID property on `ItemDefinition` (`ID`?) | Identity for allowlists | — | (S1API uses IDs, exact member not traced) | — | — | UNVERIFIED | unresolved | 01 |

> **Phase 01 usage (not new verification):** the read-only adapter in `src/TakeItOrLeaveIt.Game/Discovery/`
> reads PRD-01 (`DiscoveredProducts`), PRD-06 (`DrugType`, `Properties[].ID`), PRD-10 (`ID`, `Name`) and LIF-04 by
> reflection. These rows stay **probable/unresolved** until the Phase 01 in-game test
> ([PHASE_01_TESTING.md](PHASE_01_TESTING.md)) logs real data from v0.4.7f12 IL2CPP.

### Answer: how can the mod tell whether a product is unlocked in the active save?

- **Proposed primary source:** `ProductManager.DiscoveredProducts` (PRD-01). Category gates are
  `MethDiscovered` / `CocaineDiscovered` / `ShroomsDiscovered` (PRD-03); weed has no flag, which suggests it
  is always available (UNVERIFIED). A product counts as **unlocked** only if its definition is in
  `DiscoveredProducts` **after** `LoadManager.onLoadComplete` (LIF-01) for the active save.
- **Owning is not unlocking.** Inventory holds `ProductItemInstance`s (PRD-09) that reference a definition.
  The policy engine must check discovery and ownership separately, as `ROADMAP.md` requires.
- **Refresh triggers:** save load (LIF-01), new mix (PRD-05 postfix), and scene change / exit to menu (LIF-02/03) to clear the previous save's data.
- **Still UNVERIFIED:** whether `DiscoveredProducts` holds *every* discovered product (including base
  strains bought from suppliers) or only mixes, and when it is filled during loading. Runtime observation
  **OBS-01** below settles this.

### Answer: can two products with the same display name differ?

Likely yes. Mixes are named by the player through `FinishAndNameMix(…, name, …)`, so a display name is
user-chosen and not an identity. **Proposed strategy (Phase 01/05):** identify a product by its stable
definition ID (PRD-10, to be confirmed). Use a recipe fingerprint `DrugType + sorted(Properties IDs)` (PRD-06)
only as a secondary check and for "base strain" grouping. **Never** match on display name or substrings
(matches `MOD_MENU_SPEC.md`).

## 5. Dealers (`ScheduleOne.Economy.Dealer`)

| ID | Assembly | Type | Member / signature | Purpose | Call flow | Evidence | Harmony approach (proposal) | Risk | Runtime | Confidence | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DLR-01 | Assembly-CSharp? | `ScheduleOne.Economy.Dealer` (an `NPC`) | `AssignedCustomers` : `List<Customer>` | Customers served by this dealer | — | E1 (direct) + E3 | Read-only | Low | Both* | probable | 04 |
| DLR-02 | Assembly-CSharp? | `Dealer` | `ActiveContracts` (list of `Contract`) | **Dealer appointments** | UNVERIFIED how contracts reach the dealer | E3 | Read-only first | Medium | UNVERIFIED | unresolved | 04 |
| DLR-03 | Assembly-CSharp? | `Dealer` | `Cut` (float, default 0.2 in S1API builder); `DealerData.SalesCutPercentage` | **Commission** | Applied on sale (UNVERIFIED) | E3 / E1 (save data) | Never modify | High | Both* | unresolved | 04 |
| DLR-04 | Assembly-CSharp? | `Dealer` | `Cash`; non-public `CollectCash()` | Dealer's collected revenue | Player collects cash | E3 | Never modify | High | UNVERIFIED | unresolved | 04 |
| DLR-05 | Assembly-CSharp? | `Dealer` | `Action onContractAccepted` (public field) | Event when a dealer accepts a contract | Fired on acceptance | E1 (+E3 lookup) | Subscribe (no patch) for diagnostics | Low | Both* | probable | 04 |
| DLR-06 | Assembly-CSharp? | `Dealer` | `Load(DynamicSaveData, NPCData)` | Restores dealer state from save | Load pipeline | E2 | Postfix only if dealer reservations need restoring | Medium | Both* | probable | 06 |
| DLR-07 | Assembly-CSharp? | `ScheduleOne.UI.Phone.Messages.DealerManagementApp` | `SetOpen(…)` (parameters UNVERIFIED), `Refresh()` | Vanilla dealer management phone UI | — | E2 | Not needed | — | Both* | probable | 02 (UI reference) |
| DLR-08 | — | UNVERIFIED | **Dealer stock search** for a contract (exact-product match) | Where a dealer looks for matching inventory | UNVERIFIED | — | Prefix/replace matching with the shared eligibility engine | **High** | UNVERIFIED | unresolved | 04 |
| DLR-09 | — | UNVERIFIED | Inventory **reservation**, sale completion, missing-stock handling, cancellation | Dealer sale lifecycle | UNVERIFIED | — | Observe first | High | UNVERIFIED | unresolved | 04 |

## 6. Economy, XP and lifecycle

| ID | Assembly | Type | Member / signature | Purpose | Call flow | Evidence | Harmony approach (proposal) | Risk | Runtime | Confidence | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ECO-01 | Assembly-CSharp? | `ScheduleOne.Money.MoneyManager` | `static Instance`; `ChangeCashBalance(float, bool, bool)`; field `cashBalance`; `GetNetWorth()`; `onNetworthCalculation`; online balance SyncVar (`sync___get_value_onlineBalance`) | Player cash / bank (networked) | Called by sale completion (UNVERIFIED) | E1, E4 | **Observe only** (count calls per sale → exactly-once test) | High | Both* | probable | 03 (observe) |
| ECO-02 | Assembly-CSharp? | `ScheduleOne.Levelling.LevelManager` | `AddXP(int)`; rank-change event | XP / progression | Called on sale (UNVERIFIED) | E1 | Observe only | High | Both* | probable | 03 (observe) |
| LIF-01 | Assembly-CSharp? | `ScheduleOne.Persistence.LoadManager` | `onLoadComplete`, `onPreLoad`, `onSaveInfoLoaded` (UnityEvents); `StartGame`, `LoadAsClient`, `ExitToMenu`, `QueueLoadRequest` | Save-load lifecycle | — | E1, E2 | `AddListener` (no patch) to refresh unlocks | Low | Both* | probable | 01, 06 |
| LIF-04 | Assembly-CSharp? | `LoadManager` | `static Instance` (singleton); `bool IsGameLoaded`, `bool IsLoading`, `string LoadedGameFolderPath` | Is a save loaded, and which one | Polled ~1/s by the Phase 01 adapter (reflection, read-only) | E1 | No patch (read-only polling) | Low | Both* | probable | 01 |
| LIF-02 | Assembly-CSharp? | `LoadManager` | `onPreSceneChange` | Leaving a save | — | E1 | `AddListener`; clear per-save state | Low | Both* | probable | 06 |
| LIF-03 | Assembly-CSharp? | `ScheduleOne.Persistence.SaveManager` | `Save(string)` | Game save | — | E2 | Postfix to save per-save mod profile (separate file) | Medium | Both* | probable | 06 |

## 7. Multiplayer (transaction authority)

- **Probable:** Schedule I uses **FishNet** networking. S1API references `FishNet.Runtime` / `Il2CppFishNet.Runtime`,
  and generated names such as `RpcLogic___FinishAndNameMix_4237212381` and `sync___get_value_onlineBalance`
  are FishNet's RPC/SyncVar code generation (E4).
- **UNVERIFIED:** whether handover acceptance, payment, inventory removal and dealer sales run on the
  host/server only. Generated RPC suffixes are hashes that can change between game builds, so patches must
  target the logic methods, never the `RpcLogic___…_<hash>` names (or must resolve them dynamically).

## 8. Flow maps and missing links

### Player sale

```text
CustomerData (PreferredProperties, Standards, affinities)          CUS-06/07/08  probable
  → order generation                                               UNVERIFIED (missing link)
  → ContractInfo(payment, ProductList{ProductID, Quantity, Quality}, …)   ORD-04/05/06  probable
  → Customer.OfferContract(ContractInfo) → Contract                CUS-02, ORD-01..03  probable
  → player meets customer, hands over items (handover UI)          UNVERIFIED (missing link)
  → acceptance / satisfaction evaluation                           CUS-09/10 UNVERIFIED  ← Phase 03 core hook
  → inventory removal                                              UNVERIFIED (missing link)
  → payment MoneyManager.ChangeCashBalance? / XP LevelManager.AddXP?   ECO-01/02 names probable, call site UNVERIFIED
  → relationship / CompletedDeliveries update                      CUS-04/11 unresolved
```

### Dealer sale

```text
Dealer.AssignedCustomers → contract assigned to dealer (ActiveContracts)   DLR-01 probable, DLR-02 unresolved
  → onContractAccepted                                             DLR-05 probable
  → stock selection (exact match)                                  DLR-08 UNVERIFIED  ← Phase 04 core hook
  → reservation / completion / cancellation                        DLR-09 UNVERIFIED
  → payment split by Cut, Dealer.Cash, CollectCash()               DLR-03/04 unresolved
```

### Smallest safe runtime observations (for a later, explicitly approved diagnostics build)

All observations are **read-only Harmony postfixes that only log**, run in a debug build, and are disabled by default:

| ID | Observe | Answers |
| --- | --- | --- |
| OBS-01 | Log `ProductManager.DiscoveredProducts` (IDs, names, `DrugType`) at `onLoadComplete`, then again after buying a new base strain and after naming a mix | What exactly counts as "discovered"; base strains vs mixes |
| OBS-02 | Postfix-log each candidate from `keywords.md` matching *Handover/Offer/Evaluate/Accept* during one ordinary sale | Locates CUS-09/10 and the handover call chain |
| OBS-03 | Count calls to `MoneyManager.ChangeCashBalance` and `LevelManager.AddXP` during one sale and one dealer sale | Exactly-once payment/XP baseline (ECO-01/02) |
| OBS-04 | Log `Dealer.ActiveContracts` and dealer inventory before and after an appointment | DLR-02/08/09 lifecycle |
| OBS-05 | Repeat OBS-03 as host and as client in co-op | Host authority (section 7) |

## 9. Risks discovered

1. **Game version coupling:** S1API's evidence corresponds to whichever Schedule I build S1API 3.2.x targeted. Field names and RPC hashes can change with game updates, so every Phase 03/04 hook needs a startup existence check (PatchManager already rolls back on failure).
2. **Shared ScriptableObjects:** `CustomerData` is a shared asset. Mutating `PreferredProperties` would leak across customers and saves; patch the scoring instead.
3. **IL2CPP specifics:** IL2CPP collections (`Il2CppSystem.Collections.Generic.List<T>`) and the `Il2Cpp` namespace prefix mean the Game layer needs per-runtime adapters (`#if IL2CPP_BUILD`) for every game type it touches.
4. **Handover/acceptance path is unknown.** It is the single most important missing link for Phase 03 and cannot be guessed.
