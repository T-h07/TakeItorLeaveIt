# Changelog

All notable project changes are documented here.

## [1.0.0] — First public release preparation

### Added
- Configurable category-based customer and hired-dealer sales using legitimately unlocked products.
- **Take It** in-game phone app with Overview, Products, Rules, Sales, Presets, Settings and Diagnostics.
- Per-category Any Unlocked, Selected Only, Blocklist and Disabled modes.
- Exact recipe/base-strain lineage, minimum quality, required/excluded effects and mixing policies.
- Independent player/dealer switches, per-dealer overrides and policy presets.
- Filtered customer offers, fair-price safeguards, strict dealer eligibility and restock notifications.
- Per-save policy storage, active-order snapshots, dealer appointments, atomic file replacement and recovery copies.
- Compile-/simulation-tested multiplayer session roles and host-to-client policy display (experimental, **not officially supported** in v1.0.0).
- Game/runtime/loader compatibility diagnostics and safe hook installation checks.

### Fixed and hardened
- Managed handovers fail closed if authority or safety-critical integration is unavailable.
- Dealer rollback failures retain a bounded retry queue rather than silently discarding items.
- Hardened JSON parsing, limits on document size, logs, cache keys and save isolation.
- Reduced accidental exposure of local Windows build paths in shipped DLLs.
- Hardened GitHub workflow action pinning and expanded regression/performance tests.
- The last pre-release CI baseline passed **805 tests**, zero failures/skips, zero warnings/errors.

### Supported
- Schedule I **v0.4.7f12 IL2CPP**, Windows, single-player; MelonLoader 0.7.3 and S1API Forked 3.2.3 recommended.

### Known limitations
- Multiplayer client handovers use vanilla client-side checks, not an independent host policy check; full co-op remains experimental.
- Dealer quality-lot allocation for simultaneous mixed-quality appointments needs further physical verification.
- Mono and stripped CI builds do not contain the functional game sales integration.
- Only the target game build is compiled against and inspected; other versions are not supported.

### Verification note
The owner reports the v1.0.0-rc.2 build working in-game, but the 270-scenario historical acceptance table is not individually marked PASS. The final 1.0.0 binary must be rebuilt with local game references, hash-verified and smoke-tested before its asset is published.
