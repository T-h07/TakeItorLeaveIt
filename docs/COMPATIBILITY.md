# Compatibility — v1.0.0

**Supported release target:** Windows, Schedule I **v0.4.7f12 IL2CPP**, single-player, with MelonLoader 0.7.3 and S1API Forked 3.2.3 for the phone menu.

The owner reported the Phase 08 RC2 build working in-game and smoke-tested the final v1.0.0 ZIP before publication. This does **not** mean that every historical acceptance case or supported-matrix combination was individually exercised. Only the baseline loading (Phase 00) has earlier detailed logged physical verification; the remainder of the original acceptance tracker is still awaiting scenario-specific reports.

| Component or arrangement | Evidence / claim |
| --- | --- |
| Schedule I v0.4.7f12, IL2CPP, Windows — single-player | Targeted build, compiled with local references; owner reports the mod works in-game; final v1.0.0 ZIP smoke-tested |
| MelonLoader 0.7.3 | Recommended; build verified and Phase 00 loading verified |
| S1API Forked 3.2.3 | Required for the seven-screen Take It phone app; compiled against |
| Other Schedule I versions | Not tested or guaranteed; game changes may invalidate hooks |
| Mono alternate branch | Compiles a reduced build but **no** phone app or player/dealer sales hooks; not included as a supported gameplay release |
| CI-no-game-hooks archive | Core/loader-only test artifact, **not installable as the functional sales mod** |
| Multiplayer — same mod on host/client | Simulation-tested only; not a supported v1.0.0 gameplay guarantee |
| Multiplayer — host-only/client-only mixed install | Not verified in game |
| Different game/mod protocol versions in co-op | Not supported |

## Game startup protection

The mod checks loader/runtime facts, game version and exact Harmony target signatures. Missing interdependent patch targets cause a patch rollback. For previously accepted managed contracts, Phase 08 hardened exception/authority failure paths to refuse unsafe handovers instead of silently falling back to permissive vanilla behavior.

A game build outside the verified target may produce a warning but can still pass signature checks; **a warning is not a promise of compatibility**.

## Multiplayer limitation

The game evaluates client-originated handovers on the client's machine. The host receives a separate settlement RPC with values computed by the client, and the mod cannot independently correlate/validate the full contract and handed-over items at that boundary using the verified stable hooks. Co-op therefore remains **experimental / excluded from the official v1.0.0 support guarantee**, not fully host-authoritative player sales.

Host-side offers, dealer appointments, Steam lobby rule display and reconnect protection are implemented, but require actual multiplayer acceptance before being advertised as supported. Dealer texts in co-op reach the host only. No host migration support.

## Inventory edge case

A hired dealer's game removal API accepts a **minimum quality**, not an exact reserved quality lot. If multiple appointments compete for mixed-quality stock, a higher-quality lot may be taken from another appointment's reservation. This is an outstanding behavior risk pending the documented quality-lot physical tests; avoid such arrangements until verified.

References: [Release checklist](RELEASE_CHECKLIST.md), [Multiplayer](MULTIPLAYER.md), [Release blockers](PHASE_08_RELEASE_BLOCKERS.md), [Phase 08 testing](PHASE_08_TESTING.md).
