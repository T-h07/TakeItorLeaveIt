# Take It Or Leave It

A **Schedule I** quality-of-life mod for configurable customer and dealer sales (in development).

Customers request a **category and quantity** rather than an exact recipe. Players choose which discovered products can fulfill those orders using an **in-game mod menu**, with category-specific rules for the player and hired dealers.

> **Status: Phase 03 (Player sales), implementation complete; physical acceptance deferred.**
> Phase 00 (foundation) is verified in game on Schedule I v0.4.7f12 (IL2CPP). Phases 01 (product discovery and
> rule engine) and 02 (the **Take It** phone app) are merged; Phase 02's first in-game test passed except the
> layout, which was fixed and awaits a retest. Phase 03 applies the menu's rules to **customer orders**:
> an accepted order becomes "N units of a category" and the handover accepts any permitted product of that
> category, while the game keeps paying, awarding XP and moving items itself. Not yet tested in game.
> Dealers stay vanilla until Phase 04. No release exists.

## What works now (Phase 03)

- **Category orders** ([docs/PLAYER_SALES.md](docs/PLAYER_SALES.md)): customer contracts accepted while the mod is on take any product the Take It settings permit for their category, in exactly the ordered quantity, optionally mixed. Validation happens before anything leaves your inventory; the recipe-mismatch penalty is removed, quality still counts. Single player, IL2CPP build with local game references.

- **Take It** app on the in-game phone (IL2CPP + S1API 3.2.3): Overview, Products, Settings, Diagnostics.
- Per category: *Any Unlocked*, *Selected Only*, *Disabled*; only products unlocked in the active save can be selected.
- Search (name, effect, ID), multi-select, select all unlocked / shown, clear; warning when a selection leaves nothing eligible.
- A live eligibility preview computed by the same rule engine the sales use; a *Mixed deliveries* switch (Settings).
- Save / Discard / Reset to defaults; settings stored per save in `UserData/TakeItOrLeaveIt/saves/`, never in game saves.

## Planned features

- Customers order "N of a category" (e.g. 5 weed) instead of a specific recipe or effect mix.
- An in-game menu (phone app, Phase 02) that shows only products **already unlocked in the active save**: per-category *Any unlocked / Selected only / Disabled*, multi-select strains and recipes, quality and effect rules, mixed stacks, presets.
- The same rules applied to hired dealers and their appointments, with separate player and dealer settings.
- Per-save settings stored separately from vanilla saves; host-authoritative co-op.
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
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Project boundaries, adapter design, eligibility engine, S1API evaluation |
| [docs/BUILDING.md](docs/BUILDING.md) | Build, test, package, local deploy |
| [docs/INSTALLATION.md](docs/INSTALLATION.md) | Installing and verifying the development DLL |

## Current limitations

- Player sales are untested in game, single player only, and exist only in the IL2CPP build made with local game references (not in the CI artifact, not in the Mono build). The customer's phone message still names the game's chosen product; the handover screen shows the real requirement.
- Dealers are unchanged until Phase 04; presets and quality/effect rules come later; settings migration/backup polish is Phase 06.
- The phone menu is IL2CPP-only, needs S1API, and is not in the CI artifact; its layout fix awaits an in-game retest.
- The customer-sale hooks are verified against the installed build's call graph ([PHASE_03_PATCH_ANALYSIS.md](docs/PHASE_03_PATCH_ANALYSIS.md)); dealer stock selection is still UNVERIFIED (Phase 04).

## License

To be decided before public distribution.
