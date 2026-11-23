# Take It Or Leave It

A **Schedule I** quality-of-life mod for configurable customer and dealer sales (in development).

Customers request a **category and quantity** rather than an exact recipe. Players choose which discovered products can fulfill those orders using an **in-game mod menu**, with category-specific rules for the player and hired dealers.

> **Status: Phase 01 (Product discovery & rule engine), implemented; in-game acceptance pending.**
> Phase 00 (foundation) is accepted and verified in game on Schedule I v0.4.7f12 (IL2CPP). Phase 01 adds a
> read-only product catalog for the active save and the central sale-eligibility rule engine. The mod
> still does **not** change any gameplay: it only discovers products and logs what it would allow. No release exists.

## Planned features

- Customers order "N of a category" (e.g. 5 weed) instead of a specific recipe or effect mix.
- An in-game menu (planned as a phone app) that shows only products **already unlocked in the active save**: per-category *Any unlocked / Selected only / Disabled*, multi-select strains and recipes, quality and effect rules, mixed stacks, presets.
- The same rules applied to hired dealers and their appointments, with separate player and dealer settings.
- Per-save settings stored separately from vanilla saves; host-authoritative co-op.
- Never unlocks products, never creates items, pays exactly once.

Full plan: **[ROADMAP.md](ROADMAP.md)** · menu design: **[docs/MOD_MENU_SPEC.md](docs/MOD_MENU_SPEC.md)**

## Architecture at a glance

```text
src/TakeItOrLeaveIt.Core   game-independent logic (netstandard2.0, unit-tested):
                           product catalog, eligibility policy, allocation planner
src/TakeItOrLeaveIt.Game   MelonLoader entry point, Harmony infrastructure, read-only discovery adapter
                           -> TakeItOrLeaveIt.Il2Cpp.dll (default Steam branch, net6.0)
                           -> TakeItOrLeaveIt.Mono.dll   ("alternate" beta branch, net472)
tests/                     xUnit tests (no game required)
tools/                     read-only game inspector + environment probe, packaging script
```

Technical approach: C#, MelonLoader 0.7.3 + HarmonyX, with S1API recommended (pending a compatibility spike) for the native phone menu in Phase 02. Details and rationale: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Requirements

- Schedule I (Steam) with **MelonLoader 0.7.3** (0.7.0+ accepted; avoid 0.7.1 on IL2CPP)
- To build: Windows and the .NET 10 SDK. **No game files are needed to build** (game data is read by reflection at runtime).

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
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Project boundaries, adapter design, eligibility engine, S1API evaluation |
| [docs/BUILDING.md](docs/BUILDING.md) | Build, test, package, local deploy |
| [docs/INSTALLATION.md](docs/INSTALLATION.md) | Installing and verifying the development DLL |

## Current limitations

- No gameplay features yet, by design: Phase 01 only observes and logs. There is no menu yet (Phase 02), and settings are not saved yet (Phase 06).
- The patch map comes from public S1API source. **No entry is verified against a local game build yet**; Phase 01's in-game test will confirm the product and save members it reads. The development machine has no Schedule I install; the tools in `tools/` collect that evidence on the gaming PC.
- The player-sale handover/acceptance path and dealer stock selection are not identified yet (marked UNVERIFIED).

## License

To be decided before public distribution.
