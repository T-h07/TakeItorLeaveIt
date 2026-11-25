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
LICENSE
```

Install MelonLoader/S1API separately. Launch the game, open the in-game phone, select **Take It**, configure products and press **Save**. Full guide: [INSTALLATION.md](https://github.com/T-h07/TakeItorLeaveIt/blob/v1.0.0/docs/INSTALLATION.md).

## Known limitations

- Mixed-quality dealer stock can present a reservation/lot-selection mismatch when multiple appointments compete for the same product at different quality tiers. This scenario needs further physical validation.
- Client-originated handovers are not independently host-validated; experimental co-op should not be treated as secure server-enforced rules.
- Restock texts in co-op are sent to the host's phone only; no host migration.
- The old CI artifact omits the phone app and game hooks.

## Verification

- Automated: **805 passed**, 0 failed, 0 skipped; clean Release build with the Schedule I v0.4.7f12 references and S1API 3.2.3, 0 warnings, 0 errors. The build includes the S1API phone app and the player sales, dealer sales and customer offer hooks.
- In game: the owner reported RC2 working and then smoke-tested **this exact ZIP** on 2026-11-25: the mod initialized as `1.0.0`, the Take It phone app opened, a rule change was saved, a normal player sale and a dealer sale completed, and the log showed no new critical errors. The full historical scenario table is not individually marked PASS.
- The DLLs contain no developer paths and report version `1.0.0`; the ZIP holds only the files listed above.

**Final ZIP:** `TakeItOrLeaveIt-v1.0.0-Il2Cpp.zip` (185,826 bytes)  
**SHA256:** `A0D56D8772917F81ED73057CBE44ADF2D78804F71B5DD2D905316600E2D7E89D`

| File in the ZIP | SHA256 |
| --- | --- |
| `Mods/TakeItOrLeaveIt.Il2Cpp.dll` (130,048 bytes) | `9F95C63910CFCED069590CF8A7532CEE4BBE07D4BFB25AC5FC422D23DAEB71CE` |
| `UserLibs/TakeItOrLeaveIt.Core.dll` (309,248 bytes) | `A640E9A5E738609672C37E68B791E5A622AFB94B627B3B2022427332A0C2DA1B` |

The same hashes are in the attached `TakeItOrLeaveIt-v1.0.0-SHA256.txt`.

MIT © 2026 Taulant Haxhiu. This license covers only the mod's own source.
