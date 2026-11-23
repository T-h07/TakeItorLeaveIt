# Mod Menu — Functional Specification

**Status:** Proposed UI and settings contract. No code has been built or tested yet.

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

The UI framework is chosen after Phase 00 verifies runtime compatibility. Consider S1API for a native phone app and MelonPreferences for basic settings, with an independent policy editor for dynamic product lists. Avoid claiming that a third-party mod manager can natively edit our custom product allowlists without an explicit integration.

Refs: https://ifbars.github.io/S1API/ and https://github-wiki-see.page/m/Prowiler/schedule1-mod-manager-wiki/wiki/Mod-Authors
