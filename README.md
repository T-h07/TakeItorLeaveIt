# Take It Or Leave It

A planned **Schedule I** quality-of-life mod for configurable customer and dealer sales.

Customers request a **category and quantity** rather than an exact recipe. Players choose which discovered products can fulfill those orders using an **in-game mod menu**, with category-specific rules for the player and hired dealers.

> **Status:** Planning. No playable mod or verified patches have been released yet.

## Project documents

- [Development roadmap](ROADMAP.md) — development phases, branch names, architecture, acceptance criteria, and release gates.
- [Mod menu specification](docs/MOD_MENU_SPEC.md) — unlock-aware product filtering, strain/recipe selection, dealer rules, presets, persistence, and user experience.

## Development workflow

- `develop` — active development; all `feature/phase-XX-...` branches start here, merge back by PR, then are deleted.
- `release` — release stabilization and version tags.
- `main` — stable approved releases only.

## Planned technical approach

C#, MelonLoader/Harmony, and potential S1API integration for a native in-game phone menu. The actual game runtime, API signatures, and supported build matrix must be verified in Phase 00 before implementation.

## License

To be decided before public distribution.
