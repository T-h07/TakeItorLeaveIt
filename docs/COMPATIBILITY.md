# Compatibility (Phase 07)

> Compiling against a build is **not** support. A combination is listed as supported only after it was
> tested; everything else is labelled honestly below.

## 1. Labels

| Label | Meaning |
| --- | --- |
| **Build verified** | The mod compiles against this build's own interop assemblies and every hook signature was checked against them |
| **Automated tests verified** | Covered by the xUnit suites (no game running) |
| **Runtime verified** | Observed working in the running game |
| **Unsupported** | Known not to work, or deliberately switched off |
| **Not tested** | Unknown; may work, no claim is made |

## 2. Matrix

| Item | Status |
| --- | --- |
| Schedule I v0.4.7f12, IL2CPP (default branch) — mod loading | Runtime verified (Phase 00) |
| Schedule I v0.4.7f12, IL2CPP — sales, dealers, menu (Phases 03–06) | Build verified, Automated tests verified; runtime: Not tested (Phase 08) |
| Schedule I v0.4.7f12, IL2CPP — multiplayer (Phase 07) | Build verified, Automated tests verified (simulation only); runtime: **Not tested** (Phase 08) |
| Any other Schedule I build | Not tested (warns at startup) |
| Mono (`alternate` beta) | Build verified for loading and the rule engine only; no sales hooks, no menu → sales Unsupported |
| CI build (no game references) | Loading and rule engine only; sales Unsupported (status report says so) |
| MelonLoader 0.7.3 | Build verified, Runtime verified (Phase 00) |
| MelonLoader 0.7.0 / 0.7.2 | Not tested (accepted with a warning) |
| MelonLoader 0.7.1 on IL2CPP | Unsupported (known problematic; warning) |
| MelonLoader below 0.7.0 | Unsupported (refused) |
| S1API 3.2.3 (phone menu) | Build verified; runtime Not tested |
| Host and client on different game builds | Unsupported (diagnostics say "Not a supported combination") |
| Host and client on different Take It versions, same rules protocol | Not tested (rules shown with a note) |
| Host and client on different rules protocols | Unsupported (client shows no rules, asks to update) |

## 3. Co-op modes

| Mode | Setup | Behaviour | Automated tests |
| --- | --- | --- | --- |
| **A** | Host and client both have the same Take It | Host runs Take It; client shows host rules read-only | F01, E01, suites C/D |
| **B** | Host has Take It, client does not | Host-side features work (offers, dealers, host's own handovers); the client plays vanilla | F02, C-suite |
| **C** | Client has Take It, host does not | Client waits 30 s, then "The host does not share Take It rules. Vanilla rules apply."; nothing managed | F03, E10 |
| **D** | Different Take It versions | Same protocol: rules shown with "Different Take It versions" in Diagnostics. Other protocol: CompatibilityFailure, no rules | F04, F05 |
| **E** | Different game builds | Rules still shown, Diagnostics say "Not a supported combination" | F06 |

All rows are simulation results; none is runtime verified.

## 4. Startup checks

- `CompatibilityChecker` evaluates the runtime (build vs loader vs disk), the MelonLoader version and, since
  Phase 07, the **game build** (`Application.version`): `0.4.7f12` is *Build verified*, anything else is
  *Not tested* and adds a warning; no version read is *Unknown*. The log line is
  `Compatibility: <status>; game build: <BuildVerified|NotTested|Unknown>.`
- An untested build is not refused: `PatchManager` still resolves every hook by its exact signature at startup,
  and if one is missing it removes every Take It patch and the game runs vanilla ("Unavailable" in the status
  report). A client whose hooks failed is still read-only (it never manages anything).
- Generated RPC methods (names with a hash suffix) are never patched, so a hash change cannot break a hook.
