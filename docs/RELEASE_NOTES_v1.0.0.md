# Take It Or Leave It v1.0.0

First stable release of the configurable sales mod for **Schedule I**.

## Features

Configure permitted products using the in-game **Take It** phone app, with per-category allowlists/blocklists, exact recipes and base strains, quality and effect restrictions, mixing policies, dealer-specific overrides, presets and per-save settings. Customer offers, player sales and hired-dealer orders use these policies where the mod manages the transaction. Includes fair-pricing restrictions, dealer notifications, save recovery and diagnostics.

## Compatibility

- **Game:** Schedule I **v0.4.7f12**, Windows, **IL2CPP**.
- **Loader:** MelonLoader **0.7.3** recommended.
- **Phone UI:** S1API Forked **3.2.3**.
- **Official scope:** single-player IL2CPP.
- **Co-op:** experimental; **not supported as fully host-authoritative player sales**. Client handovers follow vanilla client validation.
- **Mono and other game builds:** not supported for equivalent gameplay functionality.

## Installation

Download the **reference-enabled IL2CPP** ZIP from these release assets (not the CI-no-game-hooks artifact). Close the game, back up your save and `UserData/TakeItOrLeaveIt`, then extract the archive to the Schedule I root folder:

```text
Mods/TakeItOrLeaveIt.Il2Cpp.dll
UserLibs/TakeItOrLeaveIt.Core.dll
```

Install MelonLoader/S1API separately. Launch the game, open the in-game phone, select **Take It**, configure products and press **Save**. Full guide: [INSTALLATION.md](https://github.com/T-h07/TakeItorLeaveIt/blob/v1.0.0/docs/INSTALLATION.md).

## Known limitations

- Mixed-quality dealer stock can present a reservation/lot-selection mismatch when multiple appointments compete for the same product at different quality tiers. This scenario needs further physical validation.
- Client-originated handovers are not independently host-validated; experimental co-op should not be treated as secure server-enforced rules.
- Restock texts in co-op are sent to the host's phone only; no host migration.
- The old CI artifact omits the phone app and game hooks.

## Verification

- Last reported pre-release automated CI baseline: **805 passed**, no failures/skips.
- Owner reported RC2 working in-game. Detailed scenario-by-scenario results are not on record.
- **Before publishing this release**, attach the final **1.0.0** IL2CPP ZIP built from the tagged source, verify that it includes game hooks and S1API app, test the exact final ZIP and record its SHA256 below.

**Final ZIP:** PENDING FINAL BUILD  
**SHA256:** PENDING FINAL BUILD

MIT © 2026 Taulant Haxhiu. This license covers only the mod's own source.
