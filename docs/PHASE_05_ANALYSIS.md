# Phase 05 analysis: game data, rule design and settings migration

> **Status: design verified against the installed build's assemblies (Schedule I v0.4.7f12, IL2CPP).** Runtime
> behaviour marked PROBABLE is confirmed by the scenarios in [PHASE_05_TESTING.md](PHASE_05_TESTING.md).
> Behaviour for players: [ADVANCED_RULES.md](ADVANCED_RULES.md).

Evidence levels as in the earlier analyses: **VERIFIED** (signature or call edge in the interop assemblies or the
xref scan), **SOURCE-SUPPORTED** (S1API source), **PROBABLE** (consistent with the call graph, not observed in
game), **DECISION** (a design choice).

## 1. Product ancestry (base strain)

| Fact | Level |
| --- | --- |
| `List<StationRecipe> ProductManager.mixRecipes` (instance field on the `ProductManager` singleton) | VERIFIED |
| `ProductManager.CreateMixRecipe(conn, string product, string mixer, string output)` builds a `StationRecipe` (`ScriptableObject.CreateInstance`, `Registry.GetItem`, `StationRecipe+IngredientQuantity..ctor`) | VERIFIED (edges) |
| `StationRecipe.Ingredients : List<StationRecipe+IngredientQuantity>`; `IngredientQuantity.Item / Items / Quantity`; `StationRecipe.Product : StationRecipe+ItemQuantity`; `ItemQuantity.Item / Quantity` | VERIFIED |
| `ProductManager.GetRecipe(string product, string mixer)` is what the mixing station uses to find the output of a product + mixer | VERIFIED (caller `MixingStation.TryCreateOutputItems`) |
| `MixRecipeData(string product, string mixer, string output)` is the saved form of the same triple | VERIFIED |
| `GetKnownProduct(EDrugType, List<Effect>)`: a mix whose effects equal an existing product reuses that product | VERIFIED (signature, callers `MixOperation.IsOutputKnown`, `MixingStationInterface.UpdatePreview`) |
| `ProductDescriptor.BaseProduct` was never filled before Phase 05 | Code |

**Decision.** Ancestry comes only from the game's recorded mix recipes: every recipe gives the edge
*input product → output product* (the mixer ingredient is not a product and is ignored). Names, prefixes and
effect similarity are never used.

- Ancestry is **transitive and may have several parents**: Granddaddy Purple → mix A → mix B makes B a
  descendant of both A and Granddaddy Purple; because the game reuses an existing product when a mix produces
  the same effects, a product can descend from two base strains. That is what the game recorded, so both apply.
- A product's **base strains** are its ancestors (including itself) that have no recorded parent.
- If the recipe list cannot be read, every product is marked *ancestry unknown*. Base-strain rules then match
  only the selected product itself (a verified identity match) and the Diagnostics screen says why.
- A recipe whose input or output has no valid product id is skipped and reported.

Runtime contents of `mixRecipes` after loading a save (recipes of discovered mixes) are PROBABLE; scenario
BS01 checks them through the Products screen's "from …" lines.

## 2. Quality

| Fact | Level |
| --- | --- |
| `EQuality QualityItemInstance.Quality` (base of `ProductItemInstance`) | VERIFIED |
| `EQuality` tiers Trash 0, Poor 1, Standard 2, Premium 3, Heavenly 4 | SOURCE-SUPPORTED (S1API `Quality` maps one-to-one) |
| `ProductList.Entry.Quality` is the customer's required quality (already used by the Phase 03 match) | VERIFIED (compiled) |
| `Dealer.RemoveAndReturnProductFromInventory(string id, int qty, EQuality minQuality)` removes only items at or above `minQuality` | VERIFIED (signature); slot order PROBABLE |
| `ProductItemInstance.GetSimilarity(ProductDefinition, EQuality)` (the quality part of the customer's match) | VERIFIED |

**Decisions.**

- Quality belongs to the **physical stack**, not to the product. It is checked on handover items and on dealer
  stock lots, never in the catalog. Offers are not filtered by quality (the game picks the required quality
  after the candidate list), so a quality rule can never make an offer impossible.
- The rule is a **minimum** tier (*Any*, or *at least* Poor/Standard/Premium/Heavenly). A set with an upper
  bound (for example "Standard only") is deliberately not offered: the customer's own standard could require a
  higher tier than the rule allows, which would make the order impossible to fill.
- The customer's own quality requirement keeps working as in vanilla: for the player it is part of the
  customer's satisfaction (Phase 03 match), for dealers the game's removal enforces it. Dealer appointments
  therefore only count stock at or above **max(rule minimum, contract quality)**; this also closes a Phase 04 gap
  where low-quality dealer stock could be counted as reservable.
- A stack whose quality cannot be read is rejected when a minimum is set (fail closed) and accepted otherwise.

## 3. Effects

| Fact | Level |
| --- | --- |
| `PropertyItemDefinition.Properties : List<Effect>`; `Effect.ID`, `Effect.Name` | VERIFIED |
| Discovery already reads effect ids (Phase 01) | Code |

**Decisions.** Rules use effect **ids** only (names are display text). Per category: required effects with
*all* or *any*, and excluded effects. A product whose effect list could not be read never passes an active effect
rule. Effects have no separate unlock state in the game; the menu lists the effects that occur on discovered
products of the category, and an effect id in the settings that no discovered product has is shown as inactive.
A rule that can never match (an effect both required-all and excluded, or every required-any effect excluded)
is reported as contradictory and permits nothing, so no impossible order is created.

## 4. Settings schema 2 and migration

```text
{
  "schemaVersion": 2,
  "enabled": true,
  "defaultMode": "anyUnlocked",
  "mixedDeliveries": true,                 // kept for older readers; equals mixing == "anyPermitted"
  "mixing": "anyPermitted" | "singleProduct" | "singleBaseStrain" | "requestedOnly",
  "playerSales": true,
  "dealerSales": true,
  "categories": { "<category>": <rule> },
  "dealers": { "<dealer npc id>": { "categories": { "<category>": <rule> } } }
}
<rule> = {
  "mode": "anyUnlocked" | "selectedOnly" | "blocklist" | "disabled",   // selectedOnly = allowlist
  "match": "exact" | "baseStrain",
  "selectedProducts": [ids], "blockedProducts": [ids],
  "effects": { "match": "all" | "any", "required": [ids], "excluded": [ids] },
  "minQuality": null | "poor" | "standard" | "premium" | "heavenly"
}
```

| Input | Result |
| --- | --- |
| Schema 1 file (Phases 01-04) | Loaded unchanged: every category keeps its mode and selections as **exact-product** allowlists, mixing = `mixedDeliveries`, player and dealer sales on, no effect/quality rules, no dealer overrides. Same permissions as before |
| First save over a schema 1 file | The old file is copied once to `<save id>.json.schema1.bak`, then schema 2 is written |
| Schema 1 `selectedOnly` ids | Never reinterpreted as base strains (`match` defaults to `exact`) |
| Unknown future schema (> 2) | Every category Disabled, nothing editable is lost from the file (it is not overwritten until you save) |
| Unknown `match`, `mixing`, quality or effect mode | The restrictive choice: exact match, single product, unknown quality → the rule permits nothing for that category (reported) |
| Missing `playerSales` / `dealerSales` | On |
| Dealer override for an id that is not a valid token | Dropped (reported) |

Order snapshots (`*.orders.json`, `*.dealer-orders.json`) gain optional `mixing`, `groups` and `minQuality`.
Older snapshots load with the mixing their `mixedDeliveries` flag implies and no quality minimum; nothing in an
accepted order is reinterpreted. Alert files are unchanged.

## 5. Rule precedence (one pipeline)

Every path asks the same engine (`SaleEligibilityPolicy`), with the policy resolved for its scope:

1. Runtime and authority (single-player host) - integration gates.
2. Save settings loaded and catalog ready.
3. Global mod switch.
4. Sales path switch (player sales / dealer sales).
5. Effective policy for the scope: **dealer override → global category rule → default mode**.
6. Category exists; category Disabled.
7. Product known, right category, unlocked.
8. Selection: allowlist or blocklist, by exact product or base strain (ancestry).
9. Effect rule.
10. Quality minimum (physical stacks only: handover items, dealer lots).
11. Mixing mode (snapshot).
12. Quantity and packaging (handover) / reservation (dealer).
13. Fair price (Phase 04 fixed-quote rule) at acceptance; basket check at handover.
14. Revalidation at handover against the acceptance snapshot; dealer removal through the game's method.

Steps 1-9 decide offers and the products a new order allows; 10-14 judge physical items. An accepted order is a
snapshot of steps 5-9 and 11 (and the quality minimum), so later setting changes never change it.

## 6. Scopes

| Scope | Uses |
| --- | --- |
| Player | Global categories. Offers without a dealer, player orders, handovers |
| Dealer *X* | Global categories overlaid with *X*'s overrides. Offers to *X*, *X*'s appointments, reservations, stock monitoring and restock texts |

**Overrides replace** the global rule of that category for that dealer (they may be broader or narrower). They
never bypass the global switch, the dealer-sales switch, unlock state, category checks or fair pricing.
Dealer identity is the game's NPC id (`Dealer.ID`, already used by Phase 04). Overrides are kept per save; a
dealer that is not hired has nothing to apply them to, and the Sales screen shows such overrides as inactive.

## 7. Toggles

| Switch off | New offers | New orders/appointments | Accepted orders | Reservations | Restock texts |
| --- | --- | --- | --- | --- | --- |
| Mod | vanilla | vanilla | keep their snapshot | kept for existing appointments | off |
| Player sales | player offers vanilla | player contracts vanilla | keep their snapshot | - | - |
| Dealer sales | dealer offers vanilla | dealer contracts vanilla (no excluded-product guard) | keep their snapshot and reservation | existing only | out-of-stock off; shortage texts for existing appointments continue |

No Harmony patch is added or removed by a switch: the hooks stay installed and each one checks the switch.

## 8. Presets

| Preset | Sets | Leaves alone |
| --- | --- | --- |
| Anything Goes | mod, player and dealer sales on; default and every category *Any Unlocked*; effect and quality rules cleared; mixing *any permitted*; dealer overrides removed | saved product selections (kept inside the rules) |
| Selected Products Only | mod, player and dealer sales on; default and every category *Selected only* | selections, match mode, effects, quality, mixing, overrides; categories with nothing selected are listed |
| Vanilla Preferences | player and dealer sales off (mod stays on, so the phone app and diagnostics keep working) | every rule (ready for switching back) |

Presets change the draft only; Save keeps them, Discard drops them. The menu shows the matching preset or
*Custom*.

## 9. Hooks

No new Harmony target. The offer filter (O1) receives the dealer argument it already has; quality is read from
items the existing hooks already see; ancestry is read at discovery through reflection. Patch count stays
13 methods across 3 modules.
