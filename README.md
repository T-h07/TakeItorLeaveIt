# Take It Or Leave It

**Take It Or Leave It** is a configurable sales mod for **Schedule I**. Instead of forcing every customer order to use one exact recipe, it lets you choose which legitimately unlocked products can satisfy eligible category-based orders. The **Take It** in-game phone app controls rules for personal sales and hired dealers.

**v1.0.0 target:** Windows · **Schedule I v0.4.7f12 (IL2CPP)** · single-player. The owner reports the RC2 build working in game; individual historical acceptance-test scenarios have not all been recorded. See [compatibility](docs/COMPATIBILITY.md) and [release checklist](docs/RELEASE_CHECKLIST.md) for the evidence boundary.

## Download and install

Use the **reference-enabled IL2CPP release ZIP** from [GitHub Releases](https://github.com/T-h07/TakeItorLeaveIt/releases) once the asset is published. **Do not install the GitHub Actions CI artifact**: it is intentionally built without Schedule I assemblies and therefore omits the phone app and gameplay sales hooks.

**Requirements:** Schedule I v0.4.7f12 IL2CPP (Steam default branch), Windows, [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) 0.7.3 (recommended), and S1API Forked 3.2.3 for the in-game phone menu. Install the external prerequisites according to their own instructions. The mod ZIP does not bundle those dependencies.

1. **Close the game** and back up your Schedule I save plus `UserData/TakeItOrLeaveIt/` (if present).
2. Extract the release ZIP **into the game's root folder**. It should place `TakeItOrLeaveIt.Il2Cpp.dll` in `Mods/` and `TakeItOrLeaveIt.Core.dll` in `UserLibs/` (plus the `LICENSE` text, which the game ignores).
3. Remove any outdated duplicate TakeItOrLeaveIt mod DLL from `Mods/`; do not install the Mono and IL2CPP variants together.
4. Launch Schedule I, load a save, open the phone, and choose **Take It**.
5. Select products and rules, then press **Save**. Changes are saved per game save, without editing vanilla save files.

See [full installation, update, removal and troubleshooting instructions](docs/INSTALLATION.md).

## Features

- **Category rules:** Any Unlocked, Selected Only, Blocklist, or Disabled for each discovered product category.
- **Granular filters:** exact recipes, base-strain lineage, quality floors, required/excluded effects, and mixing modes.
- **Player sales:** fulfill managed customer contracts with permitted items of the requested category and quantity; accepted orders retain their rule snapshots.
- **Hired dealers:** dealer-specific overrides, inventory reservations, eligible-stock handling, and restock/shortage notifications.
- **Customer offers and fair pricing:** filter future offers to permitted products and prevent cheaper substitutions from exploiting an expensive quote.
- **Presets and previews:** quickly configure policies, review unsaved changes, Save, Discard or Reset.
- **Per-save data safety:** separated JSON profiles, atomic replacement with recovery copies, invalid-data safeguards, ordered write retries and cross-save isolation.
- **Diagnostics:** check game/loader compatibility, patch availability, catalog and session status.

The mod uses the game's own systems for cash, experience, commission and actual item handovers; it does not create a separate currency or XP system.

## A quick example

Open **Take It → Products → Weed**, select **Selected Only**, and permit OG Kush. Save the rules. Newly accepted *managed* weed orders are restricted to the configured products and other applicable quality/effect/mixing checks. A product not on the allowlist must not qualify for a managed delivery.

## Compatibility and important limits

| Environment | v1.0.0 status |
| --- | --- |
| Schedule I **v0.4.7f12 IL2CPP**, Windows, single-player | Intended and owner-reported working; inspect the exact release's compatibility log |
| MelonLoader 0.7.3 + S1API Forked 3.2.3 | Recommended integration versions |
| Multiplayer/co-op | **Experimental, not a supported v1.0.0 guarantee**: client handovers are judged by vanilla client logic, not independently enforced by the host; real co-op acceptance is not documented |
| Mono / alternate game branch | **Not supported for gameplay features**: current Mono build omits the phone app and sales hooks |
| Other game versions | **Not tested**; game updates may break hooks. Treat warnings seriously |

**Known risk:** The game's dealer removal API selects by minimum quality rather than an exact reserved lot. Mixed-quality dealer stock across competing appointments may consume a better-quality lot reserved for another appointment. This behavior remains under investigation. Avoid relying on simultaneous mixed-quality appointments until verified. See [release blockers](docs/PHASE_08_RELEASE_BLOCKERS.md).

For co-op specifically, host-side offers/dealer processing and lobby rule display exist in code, but have only simulation and compile-time evidence. They are **not advertised as fully secure host-validated player handovers**. See [multiplayer limitations](docs/MULTIPLAYER.md).

## Troubleshooting and uninstall

Look in `<Schedule I>/MelonLoader/Latest.log` for the mod's initialization, compatibility, and patch messages. If the phone app or sales hooks are absent, verify you installed the reference-enabled release ZIP rather than the CI artifact, and check S1API and MelonLoader. For removal, close the game and delete only `Mods/TakeItOrLeaveIt.Il2Cpp.dll` and `UserLibs/TakeItOrLeaveIt.Core.dll`. The optional `UserData/TakeItOrLeaveIt/` folder contains your mod settings and recovery files; back it up before deleting. See [installation](docs/INSTALLATION.md) for safe downgrade details.

## Development

The source code is in `src/TakeItOrLeaveIt.Core` (game-independent logic) and `src/TakeItOrLeaveIt.Game` (MelonLoader, Harmony and S1API integration). `tests/` holds xUnit tests. The latest verified CI run before release preparation passed **805 Core tests**, with zero warnings/errors; this does **not** independently verify in-game behavior.

- `develop` — ongoing development and intended default branch.
- `release` — approved release line.
- `main` — stable published code.
- `feature/*` — temporary changes reviewed through pull requests.

Read [BUILDING](docs/BUILDING.md), [ARCHITECTURE](docs/ARCHITECTURE.md), [ROADMAP](ROADMAP.md), [CHANGELOG](CHANGELOG.md), [SECURITY](docs/SECURITY.md), and the [release checklist](docs/RELEASE_CHECKLIST.md).

## License

[MIT](LICENSE) © 2026 Taulant Haxhiu. Applies only to this mod's own code; Schedule I, Unity, Steamworks, FishNet, MelonLoader and S1API are separate projects with their own licenses and distribution terms.
