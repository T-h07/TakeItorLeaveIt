# v1.0.0 release checklist

## Source and scope

- [x] Phase 08 code prepared from the merged Phase 07 baseline.
- [x] Security hardening fixes and MIT license committed.
- [x] Owner reported the RC2 mod working in game.
- [x] Core automated baseline **805 passed / 0 failed / 0 skipped**, CI zero warnings/errors on `180d922`.
- [ ] Detailed per-scenario physical acceptance evidence recorded in `PHASE_08_TESTING.md` (owner's general report does not auto-mark all cases PASS).
- [ ] Dealer quality-lot competing-appointment risk resolved or explicitly approved as an outstanding supported-scope limitation.
- [ ] Co-op enforcement risk excluded from the official supported scope (current docs: experimental, not fully host-authoritative).
- [x] Mono gameplay mode excluded from official v1.0.0 support scope.

## Build and artifact

- [ ] The source mod version and `InformationalVersion` both say **1.0.0**, and tests pass.
- [ ] Clean local IL2CPP build uses actual Schedule I v0.4.7f12 interop references and S1API 3.2.3.
- [ ] Build logs confirm **WITH S1API phone app** and **WITH player/dealer game hooks**.
- [ ] Final archive contains only `Mods/TakeItOrLeaveIt.Il2Cpp.dll` and `UserLibs/TakeItOrLeaveIt.Core.dll` (plus MIT license if desired).
- [ ] Check all binaries for absolute developer paths and unintended proprietary assemblies.
- [ ] Record source commit, file sizes and SHA256 checksums; ZIP must be rebuilt after any version/code change.
- [ ] Extract and install the **exact final v1.0.0 ZIP** on a clean/controlled game installation and smoke-test it.
- [ ] Do not substitute the `CI-no-game-hooks` archive or rename RC2 as v1.0.0.

## Documentation

- [x] README, INSTALLATION, COMPATIBILITY, CHANGELOG and draft release notes updated.
- [x] MIT LICENSE present.
- [ ] Replace PENDING placeholders in release notes with the final asset's actual hash and file name.
- [ ] Document final tested environment and any accepted limitations.

## Promotion and publication (require verified final artifact)

- [ ] Merge approved feature branch to `develop` after CI.
- [ ] Promote the approved commit/tree to `release`.
- [ ] Create annotated `v1.0.0` tag on `release` only after final build verification.
- [ ] Bring the exact tagged commit/tree to `main`, with no divergent release content.
- [ ] Upload the tested, reference-enabled final ZIP to a public GitHub Release and publish accurate notes.
- [ ] Set **`develop`** as the repository default branch.
- [ ] Verify branch SHA relationships, tag target, GitHub release asset/checksum and default branch.

## Existing unresolved issues

See [PHASE_08_RELEASE_BLOCKERS.md](PHASE_08_RELEASE_BLOCKERS.md). The owner-approved release intent is not evidence that every untested physical scenario passed. Never silently mark untested risk checks as verified.
