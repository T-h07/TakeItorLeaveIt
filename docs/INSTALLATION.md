# Installation — Take It Or Leave It v1.0.0

## Supported configuration

- Windows and **Schedule I v0.4.7f12 IL2CPP** (Steam default branch).
- **MelonLoader 0.7.3** recommended; the project compiles against it. A different version may generate warnings; avoid 0.7.1 on IL2CPP.
- **S1API Forked 3.2.3** for the in-game `Take It` phone application.
- An **IL2CPP ZIP built with local game references** containing the actual phone menu and gameplay hooks.

**Not included in the supported v1.0.0 scope:** Mono gameplay hooks and unverified co-op client handovers. Review [COMPATIBILITY](COMPATIBILITY.md). The CI archive titled `CI-no-game-hooks` is only a build/test artifact and cannot deliver the mod's core gameplay functionality.

## Fresh installation

1. Quit the game. Back up your Schedule I saves and `<game>/UserData/TakeItOrLeaveIt/` if it exists.
2. Install MelonLoader via its [official releases](https://github.com/LavaGang/MelonLoader/releases); launch once and close to generate IL2CPP interop files.
3. Install S1API Forked 3.2.3 using its own official instructions.
4. Download the reference-enabled IL2CPP ZIP from the [GitHub Release assets](https://github.com/T-h07/TakeItorLeaveIt/releases) **after it is attached to the release**.
5. Extract the ZIP into the game root, preserving:
   ```text
   Mods/TakeItOrLeaveIt.Il2Cpp.dll
   UserLibs/TakeItOrLeaveIt.Core.dll
   ```
6. Ensure no old `TakeItOrLeaveIt.Mono.dll` or duplicate `TakeItOrLeaveIt.Il2Cpp.dll` is in `Mods/`.
7. Launch Schedule I and load a save. Open the phone and tap **Take It**.
8. In `MelonLoader/Latest.log`, look for successful mod initialization, compatible game build, the *Player sales*, *Dealer sales* and *Customer offers* patch modules, and the S1API phone-app message. The tested reference-enabled integration has **13 patched methods across 3 modules**.

## Configure rules

Go to **Take It → Products** and pick a category. Choose **Any Unlocked**, **Selected Only**, **Blocklist** or **Disabled**. Select products already unlocked in your current save, optionally configure quality/effect/mixing restrictions in **Rules**, and configure player/dealer behavior in **Sales**. Use **Presets** for a starting configuration.

**Save** commits your draft for the current save. **Discard** reverts unsaved changes. Existing accepted managed orders keep their acceptance-time rule snapshot, so changing a rule does not rewrite an already accepted contract.

Configuration is held in `<game>/UserData/TakeItOrLeaveIt/saves/`, separate from the vanilla save data.

## Update an existing installation

1. Quit the game. Back up the game save and mod data folder.
2. Replace **both** mod DLLs from the same ZIP. Never mix versions.
3. Keep your mod data folder. Migration and backups are handled by the mod; files from a newer schema are not overwritten.
4. Launch and confirm the reported mod version and patch status.

## Verify the installed files

From the game root in PowerShell:

```powershell
Get-FileHash .\Mods\TakeItOrLeaveIt.Il2Cpp.dll -Algorithm SHA256
Get-FileHash .\UserLibs\TakeItOrLeaveIt.Core.dll -Algorithm SHA256
```

Compare to the published checksums for **that exact release asset**. The hashes of earlier RC2 test binaries are not valid checksums for a newly rebuilt final v1.0.0 release.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No `Take It` app | Correct reference-enabled IL2CPP ZIP; S1API present; phone-app message in Latest.log |
| Sales remain vanilla | Game build and three Harmony patch module results; never use the CI-no-game-hooks ZIP |
| Wrong DLL runtime | Confirm Steam default IL2CPP branch and that only `TakeItOrLeaveIt.Il2Cpp.dll` is in Mods |
| Version warning | Check Schedule I build (v0.4.7f12 target) and MelonLoader version |
| Save/settings issue | Preserve original JSON, backups and `*.corrupt-*`; inspect Diagnostics/Latest.log before attempting recovery |
| Multiplayer rules not showing | Co-op is experimental, and Steam lobby transport has not been runtime-verified |

## Uninstall

Close the game. Remove `<game>/Mods/TakeItOrLeaveIt.Il2Cpp.dll` and `<game>/UserLibs/TakeItOrLeaveIt.Core.dll`. Do **not** delete MelonLoader or S1API if used by other mods. The optional `<game>/UserData/TakeItOrLeaveIt/` directory holds your policies, order snapshots and backups; remove it only after a backup if you want to discard the mod's data. Existing managed contracts may revert to vanilla exact-product behavior when the mod is removed.

## Important limitations

- Single-player IL2CPP is the intended v1.0.0 release scope. No independent real co-op acceptance report is available.
- Client-originated handovers are not independently host-authorized by this mod; they follow the game's vanilla client flow.
- Dealer restock phone texts are local to the host in co-op.
- Mixed-quality dealer inventory under competing reservations has an outstanding exact-lot selection risk.
- Other Schedule I game builds and the Mono gameplay variant are not supported as equivalent.
- The owner reports that the RC2 mod works in the actual game, but the historical 270-scenario tracker does not contain per-scenario proof. Do not represent those entries as passed.

More details: [compatibility](COMPATIBILITY.md), [multiplayer](MULTIPLAYER.md), [release blockers](PHASE_08_RELEASE_BLOCKERS.md).
