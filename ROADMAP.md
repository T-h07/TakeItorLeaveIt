# Take It Or Leave It — Development Roadmap

> **Status:** Planning | **Target:** v1.0.0 | **Primary branch:** `develop`
>
> Schedule I quality-of-life mod: category-based customer orders, player/dealer product substitution, and an in-game menu that lets players control exactly which **unlocked** products may be sold.

## Product vision

Customers request a drug **category and quantity** rather than a specific strain, mix, quality, or effect combination. Players and dealers can fulfill with eligible products from that category, including multiple allowed stacks when possible. A new in-game mod menu defines what counts as eligible, based only on products and recipes legitimately discovered in the active save.

Example: customer orders 5 weed; the player's weed allowlist contains **Granddaddy Purple** only. Delivering 5 eligible Granddaddy Purple units succeeds. Other weed fails the player-defined restriction. When multiple products are selected, any quantity-completing combination of those eligible products succeeds.

## Non-negotiable behavior

1. **Unlock-aware:** never display or enable undiscovered products/recipes as selectable. A previously saved selection that is no longer unlocked is inert, not an unlock exploit.
2. **Category-safe:** Weed never substitutes for meth/cocaine/etc. Categories are discovered from game data where practical; unknown categories fail closed.
3. **One source of truth:** player deals, dealer deals, offer generation, inventory matching, UI, and satisfaction reference the same central `SaleEligibilityPolicy`.
4. **Correct inventory/economy:** actual eligible units consumed exactly once, prices/commissions/XP handled exactly once; no duplicate transactions or free items.
5. **No destructive game changes:** do not overwrite product definitions, progression, or vanilla save files. Persist mod settings separately and safely.
6. **Safe policy transitions:** edits affect newly created orders by default. Existing accepted orders retain their validation-policy snapshot unless the player explicitly chooses to migrate them; clearly explain the distinction.
7. **Zero-eligible-product rule:** disable new offers for the affected category, show why, and never silently permit disallowed inventory. Existing vanilla orders must remain recoverable.
8. **Multiplayer:** host-authoritative policy, inventory changes, and payout; do not trust a client to authorize sales.
9. **Graceful compatibility:** detect unsupported methods/builds, log why, and disable affected features instead of applying unsafe patches.

## Planned phases and branches

| Phase | Feature branch (from `develop`) | Deliverables | Exit criteria |
| --- | --- | --- | --- |
| 00 — Recon & foundation | `feature/phase-00-foundation` | Inspect installed game build/runtime (IL2CPP/Mono); document verified method/field patch map; C# solution, MelonLoader/Harmony bootstrap; CI for game-independent logic; README and install notes | Clean build for verified local environment; load/unload diagnostics; no speculative game method signatures |
| 01 — Unlock discovery & rule engine | `feature/phase-01-policy-engine` | Discover category IDs, base strains, recipes and actual save unlock states; canonical product keys; pure matcher with policies `AnyUnlocked`, `SelectedOnly`, `Disabled`; config schema | Tests prove correct category/product matching, discovery refresh, save isolation, zero selections |
| 02 — In-game menu MVP | `feature/phase-02-mod-menu` | Dedicated phone app where supported; pause-menu/hotkey fallback; category tabs; product search; selectable unlocked entries; live previews; save/reset | Menu works in game, shows only discovered products, persists a selected-only Granddaddy Purple policy, refreshes on new unlock |
| 03 — Player customer sales | `feature/phase-03-player-sales` | Generic category/quantity offers; player delivery acceptance; allowed mixed stacks; bypass unwanted recipe/effect/quality preference checks; preserve financial/relationship flows | 5 weed accepts any 5 permitted weed, rejects disallowed or wrong categories; normal cash/XP, no duplication |
| 04 — Dealer sales | `feature/phase-04-dealer-sales` | Dealer appointment matching and stock choice across eligible stacks, reservations, cancellations and commits; same rules as player | Dealers fulfill generic category orders using only selected products, even with stock split over multiple stacks; cancellations safe |
| 05 — Advanced customization | `feature/phase-05-advanced-rules` | Base-strain vs exact-recipe selection; include/exclude list; quality options; mix/effect rules; independent player/dealer switches; sale behavior and pricing safeguards; presets | All settings compose predictably; UI shows effective result; deterministic precedence and tests |
| 06 — Persistence & resilience | `feature/phase-06-persistence` | Per-save profiles, schema migrations, atomic writes, unlock/relock behavior, reload consistency, conflict-safe fallback, log diagnostics, compatibility tests | No policy bleed between saves; safely migrates old config; game save data unchanged |
| 07 — Multiplayer & compatibility | `feature/phase-07-multiplayer` | Host-authoritative sync, join/leave behavior, player/dealer parity, Mono/IL2CPP matrices as feasible, compatibility with mod managers | Co-op tests show no divergence, inventory double-spend or duplicate payouts; unsupported setups fail safely |
| 08 — Release | `feature/phase-08-release` | Full regression suite, installer/packaging, changelog, MIT license if approved, documentation, version tagging | Signed-off game acceptance run; tested artifacts; merge to `release` then stable `main`; tag approved release commit `v1.0.0` |

**Milestones:** early prototype after Phase 02; player-sales playable build after Phase 03; solo/dealer beta after Phase 04; full v1.0.0 after Phase 08. Dates are not committed.

## Architecture (proposed; actual game bindings TBD)

```text
src/
  TakeItOrLeaveIt.Core/         # pure policy, IDs, matching, allocation
  TakeItOrLeaveIt.Game/         # MelonLoader entry and Harmony adapters
  TakeItOrLeaveIt.UI/           # dynamic phone/menu controls and previews
  TakeItOrLeaveIt.Persistence/  # per-save config and migrations
  TakeItOrLeaveIt.Multiplayer/  # host-authoritative synchronization
tests/
  TakeItOrLeaveIt.Core.Tests/
docs/
  MOD_MENU_SPEC.md
  PATCH_MAP.md                 # populated in Phase 00 from actual assemblies
```

S1API is a candidate for phone-app, lifecycle, and cross-runtime support, but the specific dependency choice must follow actual install/build inspection. We must not claim compiled support or invent signatures before this inspection.

## Rules in brief

- **Any unlocked:** any product/recipe that the player has discovered in the chosen category.
- **Selected only:** product must match at least one explicit allowlist rule (exact product ID and/or base strain ancestry).
- **Disabled:** no new offers for that category and no permission to fulfill using that category.
- **Player vs dealer:** separate enable/disable switches; both use the same allowlist by default, with optional later overrides.
- **Mixed stacks:** combinations of eligible items within the same category may sum to the contract quantity; never consume more than requested.
- **Pricing:** respect vanilla economics by default; customization must never silently award higher rewards.
- **Existing orders:** preserve validation-policy snapshot for contracts generated by the mod. Pre-existing vanilla contracts need explicit compatibility logic rather than destructive rewriting.

See [Mod menu specification](docs/MOD_MENU_SPEC.md).

## Acceptance scenarios

1. Unlock OG Kush only -> Granddaddy Purple is absent, cannot be enabled.
2. Unlock Granddaddy Purple -> item appears without restarting; choose Selected Only -> Granddaddy Purple.
3. Order 5 weed -> 5 Granddaddy Purple succeeds; 5 OG Kush is rejected under selected-only rule.
4. Select OG Kush and Granddaddy Purple -> 2 OG Kush + 3 Granddaddy Purple succeeds if mixed stacks enabled.
5. Exact-recipe mode disallows another Granddaddy Purple-derived mix unless its exact discovered recipe is selected.
6. Order 5 cocaine -> offering weed fails, regardless of any weed settings.
7. Dealer with 2 + 3 eligible units fulfills a five-unit order; dealer with disallowed stock does not.
8. Inventory, money, dealer commission, XP and satisfaction update once; no double callbacks or excess consumption.
9. Switching save slots reloads that save's own policy; a previous save's unlocks cannot leak.
10. No eligible products -> UI warning and no new category offers; no fallback to forbidden items.
11. Mid-appointment policy edit behaves according to documented snapshot rule and does not corrupt inventory.
12. Co-op client attempts to fulfill a disallowed product -> host rejects without payout or inventory desynchronization.
13. Missing game hooks / incompatible game update -> specific feature disables cleanly, with actionable log message.

## Git workflow

- `main`: stable approved release code.
- `develop`: integration branch; every feature starts from it and merges back by PR after tests.
- `release`: release candidate/stabilization; tag the approved release commit (`vX.Y.Z`) and bring the identical commit into `main` (fast-forward when possible).
- Delete merged feature branches. No direct feature commits to `main`.

## Reference material

- [S1API documentation](https://ifbars.github.io/S1API/)
- [Schedule I modding / MelonLoader installation](https://s1modding.github.io/docs/modusers/install_melonloader/)
- [Existing mod manager integration notes](https://github-wiki-see.page/m/Prowiler/schedule1-mod-manager-wiki/wiki/Mod-Authors)
- [Product manager/discovery mod reference](https://www.nexusmods.com/schedule1/mods/3022)

> All phases above describe planned functionality, not features already implemented.
