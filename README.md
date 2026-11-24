# Take It Or Leave It

A **Schedule I** quality-of-life mod for configurable customer and dealer sales (in development).

Customers request a **category and quantity** rather than an exact recipe. Players choose which discovered products can fulfill those orders using an **in-game mod menu**, with category-specific rules for the player and hired dealers.

> **Status: Phase 07 (Multiplayer and compatibility), implementation complete; simulation-tested only.** Real
> co-op and all in-game testing of Phases 03-07 are deferred to Phase 08. Phase 06 made save data safe
> (per-save profiles, safe writes, recovery). Phase 07 makes the host the only authority in co-op, shows the
> host's rules read-only to modded clients and checks the running game build
> ([docs/MULTIPLAYER.md](docs/MULTIPLAYER.md), [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)).
>
> Earlier status:
> Phase 00 (foundation) is verified in game on Schedule I v0.4.7f12 (IL2CPP). Phases 01 (product discovery and
> rule engine), 02 (the **Take It** phone app) and 03 (player sales) are merged; Phases 02's layout fix and 03
> await in-game tests. Phase 04 applies the same category orders to **hired dealers**: a dealer reserves
> eligible stock for each appointment and hands over any permitted product, while the game keeps paying,
> taking the dealer's cut, awarding XP and moving items itself. The Phase 04 critical enhancements add
> policy-filtered customer offers, a fair-price rule for substitutes, strict dealer fulfilment and dealer
> restock texts ([docs/PHASE_04_ENHANCEMENTS.md](docs/PHASE_04_ENHANCEMENTS.md)). Phase 05 adds base-strain and
> exact-recipe lists, blocklists, quality and effect rules, mixing modes, separate player/dealer switches, dealer
> overrides and presets ([docs/ADVANCED_RULES.md](docs/ADVANCED_RULES.md)). Not yet tested in game. No release exists.

## What works now (Phase 07)

- **Co-op** ([docs/MULTIPLAYER.md](docs/MULTIPLAYER.md)): the host runs Take It (offers, dealers, the host's own handovers); modded clients see "Host controls Take It settings" and the host's rules read-only; a client's own handover keeps the game's exact-product rule. Not tested in a real co-op game.
- **Compatibility** ([docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)): the running game build is classified (only v0.4.7f12 is build verified) and untested builds warn.

- **Advanced rules** ([docs/ADVANCED_RULES.md](docs/ADVANCED_RULES.md)): allowed or blocked lists by exact recipe or by base strain (lineage read from the game's recorded mix recipes, never from names); minimum quality checked on handover items and dealer stock; required/excluded effects with conflict warnings; four mixing modes; player and dealer sales switches; per-dealer overrides; presets with preview. Settings migrate to schema 2 with a one-time backup.

- **Category orders** ([docs/PLAYER_SALES.md](docs/PLAYER_SALES.md)): customer contracts accepted while the mod is on take any product the Take It settings permit for their category, in exactly the ordered quantity, optionally mixed. Validation happens before anything leaves your inventory; the recipe-mismatch penalty is removed, quality still counts. Single player, IL2CPP build with local game references.
- **Dealer appointments** ([docs/DEALER_SALES.md](docs/DEALER_SALES.md)): hired dealers fill the same category orders from their own eligible stock. Units are reserved per dealer so two appointments never share them; short appointments wait for stock; lost stock is replanned; nothing is reserved across reloads. Payment and commission stay vanilla.
- **Policy-based offers, fair pricing, strict dealers, restock texts** ([docs/PHASE_04_ENHANCEMENTS.md](docs/PHASE_04_ENHANCEMENTS.md)): customers only ask for products your settings permit; a substitute must be worth at least the quoted unit price (no cheap swaps for an expensive quote); a dealer short of permitted stock hands over nothing instead of selling an excluded product; hired dealers text you in the game's phone when they run out or cannot cover a waiting customer.

- **Take It** app on the in-game phone (IL2CPP + S1API 3.2.3): Overview, Products, Rules, Sales, Presets, Settings, Diagnostics.
- Per category: *Any Unlocked*, *Selected Only*, *Blocklist*, *Disabled*; only products unlocked in the active save can be allowed.
- Search (name, effect, ID), multi-select, select all unlocked / shown, clear; warning when a selection leaves nothing eligible.
- A live eligibility preview computed by the same rule engine the sales use; mixing and sales switches on the Sales screen.
- Save / Discard / Reset to defaults; settings stored per save in `UserData/TakeItOrLeaveIt/saves/`, never in game saves.

## Planned features

- Customers order "N of a category" (e.g. 5 weed) instead of a specific recipe or effect mix.
- An in-game menu (phone app, Phase 02) that shows only products **already unlocked in the active save**: per-category *Any unlocked / Selected only / Disabled*, multi-select strains and recipes, quality and effect rules, mixed stacks, presets.
- The same rules applied to hired dealers and their appointments, with separate player and dealer settings.
- Per-save settings stored separately from vanilla saves; host-authoritative co-op (Phase 07, simulation-tested).
- Never unlocks products, never creates items, pays exactly once.

Full plan: **[ROADMAP.md](ROADMAP.md)** · menu design: **[docs/MOD_MENU_SPEC.md](docs/MOD_MENU_SPEC.md)**

## Architecture at a glance

```text
src/TakeItOrLeaveIt.Core   game-independent logic (netstandard2.0, unit-tested):
                           product catalog, eligibility policy, allocation planner,
                           menu controller (state, draft/saved settings), per-save JSON settings store,
                           Sales/ = category orders, delivery validation, transaction state, order snapshots
src/TakeItOrLeaveIt.Game   MelonLoader entry point, Harmony infrastructure, read-only discovery adapter,
                           PhoneMenu/ = S1API phone app (IL2CPP, built only with local references)
                           Sales/ = Harmony hooks on the customer-contract code (IL2CPP, local references)
                           -> TakeItOrLeaveIt.Il2Cpp.dll (default Steam branch, net6.0)
                           -> TakeItOrLeaveIt.Mono.dll   ("alternate" beta branch, net472)
tests/                     xUnit tests (no game required)
tools/                     read-only game inspector + environment probe, packaging script
```

Technical approach: C#, MelonLoader 0.7.3 + HarmonyX; the menu is an S1API 3.2.3 `PhoneApp` built with Unity UI. Details and rationale: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Requirements

- Schedule I (Steam) with **MelonLoader 0.7.3** (0.7.0+ accepted; avoid 0.7.1 on IL2CPP)
- For the menu: **S1API 3.2.3** (IL2CPP). Without it the mod runs without a menu.
- To build: Windows and the .NET 10 SDK. No game files are needed for a normal build; the phone app additionally needs reference assemblies exported from the game (see [docs/BUILDING.md](docs/BUILDING.md#phase-02-phone-app-local-references)). The CI artifact has no menu.

## Build

```powershell
dotnet build TakeItOrLeaveIt.sln -c Release
```

```powershell
dotnet test TakeItOrLeaveIt.sln -c Release --no-build
```

```powershell
pwsh tools/Package-Mod.ps1 -Configuration Release
```

See [docs/BUILDING.md](docs/BUILDING.md) (output locations, optional auto-deploy) and
[docs/INSTALLATION.md](docs/INSTALLATION.md) (install, expected log lines, uninstall).

## Development workflow

- `main`: stable approved releases only.
- `release`: release stabilization and version tags.
- `develop`: active development. Every `feature/phase-XX-...` branch starts here, merges back by PR after review and in-game testing, and is then deleted.
- CI ([build.yml](.github/workflows/build.yml)) builds, tests and packages on every push to `develop`/`feature/**` and on PRs.

## Documentation

| Document | Contents |
| --- | --- |
| [ROADMAP.md](ROADMAP.md) | Phases, branch names, acceptance criteria, release gates |
| [docs/MOD_MENU_SPEC.md](docs/MOD_MENU_SPEC.md) | Unlock-aware product filtering, strain/recipe selection, dealer rules, presets, persistence, UX |
| [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md) | Verified toolchain, Mono/IL2CPP findings, how to collect evidence on the gaming PC |
| [docs/PATCH_MAP.md](docs/PATCH_MAP.md) | Researched game integration points with evidence and confidence |
| [docs/PRODUCT_DISCOVERY.md](docs/PRODUCT_DISCOVERY.md) | Product identity, unlock catalog, save isolation, policy modes, allocation |
| [docs/PHASE_01_TESTING.md](docs/PHASE_01_TESTING.md) | Phase 01 automated tests, in-game test steps, acceptance status |
| [docs/PHASE_02_TESTING.md](docs/PHASE_02_TESTING.md) | Phase 02 install, opening the menu, P02-T01..T34 acceptance steps, evidence to collect |
| [docs/PLAYER_SALES.md](docs/PLAYER_SALES.md) | Phase 03 category orders: validation, mixed deliveries, transaction, snapshots, limitations |
| [docs/PHASE_03_PATCH_ANALYSIS.md](docs/PHASE_03_PATCH_ANALYSIS.md) | Verified customer-sale call graph of the installed build and the hooks used |
| [docs/PHASE_03_TESTING.md](docs/PHASE_03_TESTING.md) | Phase 03 in-game test sales, lifecycle checks, P03-T01..T38 status |
| [docs/DEALER_SALES.md](docs/DEALER_SALES.md) | Phase 04 dealer appointments: reservations, waiting for stock, handover, recovery, limitations |
| [docs/PHASE_04_PATCH_ANALYSIS.md](docs/PHASE_04_PATCH_ANALYSIS.md) | Verified dealer call graph of the installed build and the hooks used |
| [docs/PHASE_04_ENHANCEMENTS.md](docs/PHASE_04_ENHANCEMENTS.md) | Phase 04 enhancements: offer filter, fair pricing, strict dealer fulfilment, restock messages |
| [docs/PHASE_04_TESTING.md](docs/PHASE_04_TESTING.md) | Phase 04 in-game scenarios DS01..DS18 and OF/PR/MSG/ST/RG, P04-T01..T48 status |
| [docs/ADVANCED_RULES.md](docs/ADVANCED_RULES.md) | Phase 05 player guide: lists, lineage, quality, effects, mixing, switches, overrides, presets |
| [docs/PHASE_05_ANALYSIS.md](docs/PHASE_05_ANALYSIS.md) | Phase 05 game data evidence, rule precedence, schema 2 and migration |
| [docs/PHASE_05_TESTING.md](docs/PHASE_05_TESTING.md) | Phase 05 in-game scenarios (17 areas), all NOT TESTED |
| [docs/PHASE_06_ANALYSIS.md](docs/PHASE_06_ANALYSIS.md) | Phase 06 persistence audit, safe writes, recovery and compatibility rules, Phase 08 assumptions |
| [docs/PHASE_06_TESTING.md](docs/PHASE_06_TESTING.md) | Phase 06 in-game scenarios for Phase 08, all NOT TESTED |
| [docs/PHASE_07_NETWORK_ANALYSIS.md](docs/PHASE_07_NETWORK_ANALYSIS.md) | Phase 07 call-graph evidence: where each sale step runs, authority, rules transport, blockers |
| [docs/MULTIPLAYER.md](docs/MULTIPLAYER.md) | Co-op: sessions, rules sharing, the read-only client menu, sales in co-op |
| [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) | Compatibility matrix, co-op modes A-E, startup checks |
| [docs/PHASE_07_TESTING.md](docs/PHASE_07_TESTING.md) | Phase 07 co-op scenarios for Phase 08, all NOT TESTED |
| [docs/PHASE_08_RELEASE_AUDIT.md](docs/PHASE_08_RELEASE_AUDIT.md) | Phase 08 release-readiness audit: baseline, assumptions, client-handover investigation, decisions needed |
| [docs/PHASE_08_TESTING.md](docs/PHASE_08_TESTING.md) | Phase 08 master acceptance tracker (all Phase 01–07 IDs), release candidates, batch instructions |
| [docs/PHASE_08_RELEASE_BLOCKERS.md](docs/PHASE_08_RELEASE_BLOCKERS.md) | Release blockers P0–P3 and their status |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Project boundaries, adapter design, eligibility engine, S1API evaluation |
| [docs/BUILDING.md](docs/BUILDING.md) | Build, test, package, local deploy |
| [docs/INSTALLATION.md](docs/INSTALLATION.md) | Installing and verifying the development DLL |

## Current limitations

- Co-op (Phase 07) is simulation-tested only. A client's own handover keeps the game's exact-product rule (the host cannot check it without patching a generated RPC); dealer restock texts reach the host only; no host migration.
- Player and dealer sales are untested in game and exist only in the IL2CPP build made with local game references (not in the CI artifact, not in the Mono build). The customer's phone message names the game's chosen product, which is now always a permitted one; the handover screen shows the real requirement.
- A dealer short of permitted stock still goes to the customer and hands over nothing; what the customer then does is the game's own behaviour (PROBABLE until tested).
- Dealers accept contracts with the game's own stock check. Phase 05 rules are untested in game; the dealer list for overrides fills as dealers update in the session; whether the offer hook's dealer argument identifies the dealer is PROBABLE.
- Phase 06 save-data handling (per-save profiles, safe writes, backups and recovery) is verified by automated tests only; in-game confirmation is scheduled for Phase 08.
- The phone menu is IL2CPP-only, needs S1API, and is not in the CI artifact; its layout fix awaits an in-game retest.
- The customer and dealer hooks are verified against the installed build's call graph ([PHASE_03_PATCH_ANALYSIS.md](docs/PHASE_03_PATCH_ANALYSIS.md), [PHASE_04_PATCH_ANALYSIS.md](docs/PHASE_04_PATCH_ANALYSIS.md)); the behaviour marked PROBABLE there is what the in-game tests confirm.

## License

[MIT](LICENSE) © 2026 Taulant Haxhiu. The license covers this mod's own code only. Schedule I, MelonLoader,
S1API, Steamworks, FishNet and Unity belong to their respective owners; none of them is included in this repository
or in the mod package.
