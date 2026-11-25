# v1.0.0 release checklist

## Source and scope

- [x] Phase 08 code prepared from the merged Phase 07 baseline.
- [x] Security hardening fixes and MIT license committed.
- [x] Owner reported the RC2 mod working in game.
- [x] Core automated baseline **805 passed / 0 failed / 0 skipped**, CI zero warnings/errors on `180d922`.
- [ ] Detailed per-scenario physical acceptance evidence recorded in `PHASE_08_TESTING.md` (owner's general report does not auto-mark all cases PASS).
- [x] Dealer quality-lot competing-appointment risk explicitly approved by the owner as a documented, unresolved v1.0.0 limitation (B-03).
- [x] Co-op enforcement risk excluded from the official supported scope (experimental, not fully host-authoritative).
- [x] Mono gameplay mode excluded from official v1.0.0 support scope.

## Build and artifact

- [x] The source mod version and `InformationalVersion` both say **1.0.0**, and tests pass (805/805).
- [x] Clean local IL2CPP build uses actual Schedule I v0.4.7f12 interop references and S1API 3.2.3 (.NET SDK 10.0.400, 0 warnings, 0 errors).
- [x] Build logs confirm **WITH S1API phone app** and **WITH player/dealer game hooks**; the DLL references S1API and contains the phone app and all three patch classes.
- [x] Final archive contains only `Mods/TakeItOrLeaveIt.Il2Cpp.dll`, `UserLibs/TakeItOrLeaveIt.Core.dll` and `LICENSE`.
- [x] Check all binaries for absolute developer paths and unintended proprietary assemblies.
- [x] Record source commit, file sizes and SHA256 checksums; ZIP must be rebuilt after any version/code change. ZIP SHA256 `A0D56D8772917F81ED73057CBE44ADF2D78804F71B5DD2D905316600E2D7E89D`; The shipped, smoke-tested DLLs were built from `c4c1e0c`; the tagged commit changes documentation only. Rebuilding from the tagged commit gives the same code: the DLLs differ only in the Git commit ID embedded for source links and the IDs derived from it, and with source links disabled both commits build byte-identical DLLs.
- [x] Extract and install the **exact final v1.0.0 ZIP** and smoke-test it (owner, 2026-11-25: the mod initialized as `1.0.0`, the Take It phone app opened, a rule change was saved, a normal player sale and a dealer sale completed, and the log showed no new critical errors).
- [x] Do not substitute the `CI-no-game-hooks` archive or rename RC2 as v1.0.0.

## Documentation

- [x] README, INSTALLATION, COMPATIBILITY, CHANGELOG and draft release notes updated.
- [x] MIT LICENSE present.
- [x] Replace PENDING placeholders in release notes with the final asset's actual hash and file name.
- [x] Document final tested environment and any accepted limitations.

## Promotion and publication (require verified final artifact)

The tag, the GitHub Release and the post-publication checks happen after this commit; their results are on the
GitHub Release page.

- [x] Merge approved feature branch to `develop` after CI (PR #9).
- [x] Promote the approved commit/tree to `release`.
- [ ] Create annotated `v1.0.0` tag on `release` only after final build verification.
- [x] Bring the exact tagged commit/tree to `main`, with no divergent release content.
- [ ] Upload the tested, reference-enabled final ZIP to a public GitHub Release and publish accurate notes.
- [x] Set **`develop`** as the repository default branch.
- [ ] Verify branch SHA relationships, tag target, GitHub release asset/checksum and default branch.

## Existing unresolved issues

See [PHASE_08_RELEASE_BLOCKERS.md](PHASE_08_RELEASE_BLOCKERS.md). The owner-approved release intent is not evidence that every untested physical scenario passed. Never silently mark untested risk checks as verified.
