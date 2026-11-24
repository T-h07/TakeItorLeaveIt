# Mod Menu — Functional Specification

**Status:** Phase 02 implemented the subset described in "Implemented in Phase 02" below (not yet tested in game).
The rest of this document is the original concept for later phases.

## Implemented in Phase 02

Entry point: the **Take It** app on the in-game phone (S1API 3.2.3 `PhoneApp`, IL2CPP builds made with local
references). Landscape layout: top bar (title, status/feedback, **Discard**, **Save**), left navigation, content.

| Screen | Implemented |
| --- | --- |
| Overview | Mod ON/OFF for the save; active save; catalog status (*Waiting for active save...*, *Waiting for product data...*, *Unable to load product catalog.*); "Sales integration is planned for Phases 03 and 04"; one card per discovered category with mode, eligible and unlocked counts, warnings and inactive selections; tapping a card opens it in Products |
| Products | Tabs for every discovered category (no fixed list); mode buttons with descriptions; search (case-insensitive, all terms, over name, effect IDs and product ID); *Select all unlocked*, *Select shown* (current search), *Clear* (current category only); 40 rows per page with check box, name, detail (Locked / effects / ID when names repeat) and status; hypothetical preview "Hypothetical order: 5 <category>" from `SaleEligibilityPolicy` |
| Settings | Save / Discard, Reset to defaults with confirmation (then Save), storage location |
| Diagnostics | Mod, game, Unity, MelonLoader and runtime versions; catalog generation, counts and issues |

Rules: only unlocked products can be ticked (locked rows are disabled; previously saved selections of locked
products are kept but counted as inactive); *Selected Only* with nothing eligible shows "No products selected.
This category currently has no eligible products."; *Disabled* shows 0 eligible. Edits change a draft;
"Settings saved." appears only after the file was written. Settings: one JSON file per save (see ARCHITECTURE §7).

## Added in Phase 05

| Screen | Implemented |
| --- | --- |
| Products | Fourth mode *Blocklist*; the list buttons edit the blocked list in that mode (*Block all unlocked*, *Block shown*); rows show lineage (*base strain*, *from ...*, *lineage unknown*); the summary names the scope when a dealer is edited |
| Rules | Per category: list matching (exact recipe / base strain), minimum quality (Any, Poor+ ... Heavenly+), effects (tap to cycle Any/Required/Excluded, *Require all/any*, Clear) with conflict and inactive-effect markers; *Use global rule* in dealer scope |
| Sales | Player sales and dealer sales switches, mixing mode, dealer list with *Edit overrides* / *Remove overrides* and inactive overrides |
| Presets | Current preset or *Custom*; *Preview changes* → change list and warnings → *Apply to draft* / *Cancel* |
| Overview / Diagnostics | Preset, switches and mixing line; per-category rule detail; rule engine status (schema, scopes, lineage coverage) |

Navigation has seven entries at 34 px so it fits the 450 px minimum height; advanced options are on their own
screens so the Products header keeps its fixed height. Player guide: [ADVANCED_RULES.md](ADVANCED_RULES.md).

### Changes from the concept below, and why

- **Customer Sales, Dealer Sales, Profiles/presets, quality/effect rules, base-strain mode:** not built; they configure behaviour that does not exist until Phases 03-05. Showing them now would promise effects the mod cannot deliver.
- **Sales preview** is labelled *hypothetical* and checks eligibility only (no stock), because no sale path is hooked yet.
- **Debug logging toggle, hook status:** not built; there are no hooks yet. Diagnostics shows versions and catalog state.
- **Hotkey/pause-menu fallback:** not built. The phone app compiled against the real S1API 3.2.3; a fallback is only worth building if it fails in game.
- **Paging (40 rows)** instead of one long list keeps Unity UI rebuilds small for catalogs with hundreds of products.
- **Mod ON/OFF** is per save (part of the save's settings), not global.

## Experience

The menu should feel native to Schedule I: phone app as the primary entry, compact navigation, category tabs, dark styling, no gameplay-blocking overlay while closed. Use a configurable shortcut/pause-menu fallback if a native phone app is incompatible with the installed build. Support keyboard and mouse; avoid a required external mod manager.

## Navigation

```text
Take It Or Leave It
├── Overview
│   ├── Enable mod
│   ├── Active save / policy summary
│   └── Sales preview: "5 weed -> allowed / blocked"
├── Products
│   ├── Weed
│   ├── Meth
│   ├── Cocaine
│   └── Other discovered game categories
│       ├── Any unlocked / Selected only / Disabled
│       ├── Filter by base strain OR exact discovered product/recipe
│       ├── Search, select all, select none, reset
│       ├── Permitted list (only unlocked entries)
│       └── Effective sale rule preview
├── Customer Sales
│   ├── Ignore requested strain / recipe
│   ├── Ignore preferred effects
│   ├── Ignore quality restrictions
│   ├── Allow combined eligible stacks
│   ├── Permit partial order fulfillment (off by default)
│   └── Prefer vanilla economy / satisfaction handling
├── Dealer Sales
│   ├── Enable dealer substitution
│   ├── Match selected products
│   ├── Combine stacks / split reservation
│   ├── Allow configurable dealer-specific overrides (later)
│   └── Show reason when inventory cannot fulfill
├── Profiles
│   ├── Save-specific profile / reset to defaults
│   ├── Quick preset: Anything Goes
│   ├── Quick preset: Selected Products Only
│   ├── Quick preset: Vanilla Preferences
│   └── Export/import policy file (advanced, later)
└── Diagnostics
    ├── Runtime/game build
    ├── Hook status and compatibility warnings
    └── Debug logging toggle
```

## Discovery vs inventory

- **Unlocked/discovered:** determines what the player *may configure*; read the actual save progression, not a fixed list of names.
- **Owned/in stock:** determines whether the player or dealer *can currently fulfill* an order; does not control menu visibility.
- **Base strain:** selecting Granddaddy Purple in this mode permits its descended variants, only if the variant itself meets the applicable unlock rules. Do not infer ancestry from substring matching.
- **Exact recipe/product:** select by stable internal identifier plus effect/mix fingerprint as needed; same display name does not imply identical recipe.
- Refresh on save load and discovery changes, and provide a safe manual refresh only if necessary.
- Never unlock, discover, spawn, or grant a product simply because the user selects it in this menu.

## Proposed config shape (illustrative, not a game API)

```json
{
  "schemaVersion": 1,
  "enabled": true,
  "playerSales": true,
  "dealerSales": true,
  "ignoreVanillaRecipePreference": true,
  "ignoreEffectPreference": true,
  "ignoreQualityRestriction": true,
  "combineEligibleStacks": true,
  "applyChangesToExistingOrders": false,
  "categories": {
    "weed": {
      "mode": "selectedOnly",
      "allowBaseStrains": ["game:granddaddy_purple"],
      "allowExactProducts": []
    },
    "meth": { "mode": "anyUnlocked" },
    "cocaine": { "mode": "anyUnlocked" }
  }
}
```

**Note:** `game:granddaddy_purple` is a *placeholder* identity for documentation, not a verified Schedule I product ID. Production code must read actual stable IDs from the installed version.

## Policy precedence

> Phase 05 implements the full order in [PHASE_05_ANALYSIS.md §5](PHASE_05_ANALYSIS.md#5-rule-precedence-one-pipeline).

1. Unsupported game state or disabled global mod -> vanilla behavior / feature-safe fallback.
2. Valid category required; reject cross-category swaps.
3. Disabled category -> no new orders and no substitution allowed.
4. Active accepted order uses its captured eligibility policy by default; new orders use the edited policy.
5. Selected-only mode requires at least one selection and a verified unlock; a stale or unrecognized ID is never considered unlocked.
6. Per-player/per-dealer switch controls whether that delivery path may substitute; disabled path uses documented vanilla behavior.
7. Quantity must equal the required amount unless a separate explicit partial-fulfillment feature is enabled.
8. Vanilla financial lifecycle and inventory changes remain authoritative; never pre-credit or double-consume.

## UX acceptance

- Search/filter/list remains usable with hundreds of discovered recipes.
- The player can see a simple explanation for every reject: wrong category, not unlocked, excluded by policy, insufficient quantity, incompatible build.
- Buttons and changes save consistently; reset requires confirmation.
- The menu is read-only until the save is loaded and authenticated/authorized multiplayer host state is known.
- Changes are reflected in the preview immediately; persisted policies are applied in game only after safe validation.
- Discovered products appear automatically as the save progresses; save switches never display another save's unlocks.
- If restrictions leave zero permitted products, warn prominently and stop generating affected category offers.

## Testing and verification gates

Phase 02 chose an S1API phone app with our own per-save JSON (no MelonPreferences). Physical acceptance steps: [PHASE_02_TESTING.md](PHASE_02_TESTING.md). Third-party mod managers cannot edit these product allowlists.

Refs: https://ifbars.github.io/S1API/ and https://github-wiki-see.page/m/Prowiler/schedule1-mod-manager-wiki/wiki/Mod-Authors
