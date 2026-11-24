# Phase 08 release-readiness audit

> **Release status: NOT READY — physical acceptance has not started.** This audit is the starting point for Phase 08.
> It changes no historical result. Nothing here was observed in the game; every "verified" below means
> *build, automated test or call-graph verified* unless it says *runtime verified*.

## 1. Baseline (2026-11-24)

| Item | Value |
| --- | --- |
| Branch | `feature/phase-08-release`, from `develop` @ `f057527` (Phase 07 merge, PR #8); ancestry checked |
| `release`, `main` | Both at `0ecb026` ("Initial commit"), unprotected, both ancestors of `develop` (fast-forward promotion is possible) |
| Tags | None |
| Working tree | Clean, no unrelated modifications |
| .NET SDK | 10.0.400 (`global.json`) |
| Automated tests | **767 passed, 0 failed** (Core, xUnit, net10.0) — matches the Phase 07 baseline |
| Builds | IL2CPP with game references, CI mode (no references) and Mono (net472): 0 warnings, 0 errors |
| Game references | Local export `S1Refs-20261123-214348` (Schedule I v0.4.7f12 IL2CPP interop, S1API 3.2.3); never committed |
| Harmony patches in source | 13 `Patch` calls in 3 modules: Player sales (category orders), Dealer sales (category appointments), Customer offers (policy filter). Phase 05–07 added none. To be confirmed in the log (P08-ENV02) |

### Build and packaging review

- `TakeItOrLeaveIt.Game.csproj` compiles the phone app and the sales hooks only when `S1RefsDir` points at a local
  reference export; otherwise it prints "building WITHOUT" and excludes `PhoneMenu/**` and `Sales/**`.
- **The CI artifact is built without game references**: it has no phone app and no sales hooks. It must never be
  used for acceptance or release. Its artifact name (`TakeItOrLeaveIt-<sha>`) did not say so; renamed in this
  phase to `TakeItOrLeaveIt-CI-no-game-hooks-<sha>` (B-15).
- `tools/Package-Mod.ps1` writes `artifacts/package/{Il2Cpp,Mono}`; the IL2CPP folder holds exactly
  `Mods/TakeItOrLeaveIt.Il2Cpp.dll` and `UserLibs/TakeItOrLeaveIt.Core.dll`. No game, Steamworks, FishNet,
  MelonLoader or S1API assembly is copied (the game interop references are `Private=false`).
- Release candidates are versioned `1.0.0-rc.N` (`Directory.Build.props` + `ModInfo.Version`, unit-tested), so a
  log or screenshot always names the build it came from.

## 2. Phase-by-phase status

| Phase | Area | Automated | Physical | Open runtime assumptions |
| --- | --- | --- | --- | --- |
| 00 | Loader startup, runtime detection, patch manager | PASS | **Runtime verified** (mod loading, v0.4.7f12 IL2CPP, MelonLoader 0.7.3/0.7.4) | — |
| 01 | Discovery, unlocks, catalog, eligibility | PASS | NOT TESTED (log-based steps never run) | `LoadManager` polling; discovered-product semantics |
| 02 | Phone app | PASS | **Partially observed on the Phase 02 build** (icon once, opens, catalog Ready, Save worked; scroll areas clipped → layout fix). The fix and everything after it: NOT TESTED | S1API icon de-duplication across scene loads |
| 03 | Player category orders | PASS | NOT TESTED | Units = Quantity × Amount; match fraction; payment not scaled by match; `GetError` polarity (B-08) |
| 04 | Dealers, offers, fair price, strict fulfilment, restock texts | PASS (dealer model simulated) | NOT TESTED | Empty-handover lifecycle (B-02); `requiredQuantity` in units; offer hook's dealer argument (B-07) |
| 05 | Lists, lineage, quality, effects, mixing, switches, overrides, presets | PASS | NOT TESTED | `mixRecipes` contents (B-09); removal slot order by quality (B-03) |
| 06 | Save isolation, safe writes, recovery, migration | PASS (fault injection) | NOT TESTED | Save path stability, NoSave between loads, quit hook, dealer ID stability, pruning (B-10) |
| 07 | Session authority, rules sync, co-op, game build check | PASS (**simulation only**) | NOT TESTED | Lobby data transport (B-05); client handovers not host-validated (B-04) |

Historical acceptance tables are unchanged. Their IDs are consolidated in [PHASE_08_TESTING.md](PHASE_08_TESTING.md);
no earlier physical test is marked passed there.

## 3. Unresolved runtime assumptions (complete list)

1. Customer handover units are `Quantity × Amount` per packaged item (Phase 03).
2. `GetProductListMatch` returns a matched-unit fraction weighted by similarity; the mod's prefix returns the same shape.
3. `Contract.Payment` is fixed at acceptance and not scaled by the match.
4. `HandoverScreenContractMode.GetError` returns `true` for "there is an error".
5. Dealer `requiredQuantity` is in units; the handover routine calls `Customer.ProcessHandover`.
6. An empty dealer delivery makes the customer refuse and the game fail/expire the contract (B-02).
7. `RemoveAndReturnProductFromInventory(id, qty, minQuality)` slot order among qualifying tiers (B-03).
8. The offer hook's `dealer` argument identifies the dealer for dealer customers (B-07).
9. `ProductManager.mixRecipes` holds the recipes of discovered mixes after loading (B-09).
10. `LoadedGameFolderPath` is stable per save; main menu produces NoSave before the next load; `OnDeinitializeMelon`
    runs on normal quit; dealer IDs are unique and stable; vanished contracts are pruned (B-10).
11. Steam lobby data: owner-only writes, member reads, join-in-progress reads, propagation delay (B-05).
12. Offers for a client's customers are generated on the host and therefore filtered (Phase 07).
13. S1API does not duplicate the app icon across scene loads (P02-T28).

## 4. Known limitations carried into Phase 08

- Customer phone message / journal names the game's chosen product (always a permitted one) rather than the category.
- More units than ordered are refused (no partial-stack hand-in).
- A dealer short of permitted stock hands over nothing (strict); what the customer does is B-02.
- Dealer restock texts are host-local in co-op; no host migration.
- Mono and CI builds: no sales hooks, no phone app.
- A client's own handover in co-op keeps the vanilla exact-product rule (B-04).

## 5. Client-handover investigation (spec §22)

Re-inspected from the v0.4.7f12 call graph:

1. **Where items leave inventory.** `Customer.ProcessHandover` runs on the machine of the player who handed over and
   moves the items out of *that* player's inventory (`HandoverScreen.TransferCustomerItemsToNPC`) before anything is
   sent to the host.
2. **What the host receives.** `ProcessHandoverServerSide(items, handoverByPlayer, totalPayment, productList,
   satisfaction, dealerObject)` arrives as a generated FishNet RPC (`RpcLogic___ProcessHandoverServerSide_3315874220`).
   It carries the items, a payment and a satisfaction value computed by the sender, and no contract identity. The host
   could correlate it only through the customer object (`Customer` instance) and its current contract.
3. **Non-generated callees on the host** are bookkeeping after the fact: `LevelManager.AddXP`, `Dealer.SubmitPayment`,
   `MoneyManager.ChangeLifetimeEarnings`, `ProductManager.RecordContractReceipt(conn, receipt)`, `DailySummary.*`.
   None of them is a point where a delivery can still be refused.
4. **Consequence.** There is **no safe server-side validation boundary**: by the time the host can see a client's
   delivery, the client's items are already gone. Refusing at that point would destroy the items without payment (P0).
   The only server-side option is *detection* (log a delivery that breaks the host's snapshot), not enforcement.
   Patching the RPC itself is forbidden (build-specific hash) and would not change point 1.
5. **Altered clients.** A modified client can already send arbitrary handover RPCs in vanilla Schedule I; the mod
   neither adds nor removes that exposure. An unmodified or Take It client follows the game's exact-product rule.

**Options for the owner (B-04):**

| Option | What ships | Claim allowed |
| --- | --- | --- |
| **A (recommended)** | Phase 07 behaviour: host-side features (offers, dealers, host's own handovers) + read-only rules display; client handovers vanilla | "Host-side features in co-op. Client handovers use the game's normal rules and are outside Take It's enforcement." |
| B | A + host-side detection log for client deliveries that would break the snapshot (`RecordContractReceipt` correlation), no enforcement | Same as A; adds a diagnostic only. Needs a new hook (scope increase) |
| C | Disable every managed feature whenever another player is present (single player only) | "Single player only" |

Host-authoritative player sales cannot be advertised under any option.

## 6. Mono

Mono builds load the Core and discovery only: no Mono game references are exported, so no sales hooks and no phone
app. **Recommendation:** v1.0.0 ships the IL2CPP package only; Mono is documented as unsupported (B-12).

## 7. License (B-06, resolved)

**Decision (owner, 2026-11-24): MIT.** `LICENSE` added. The options that were considered:

| License | Effect |
| --- | --- |
| MIT | Anyone may use, modify and redistribute with the notice; no warranty. Common for MelonLoader mods |
| Apache-2.0 | Like MIT plus an explicit patent grant and change notices |
| GPL-3.0 | Derivatives must stay open source under GPL |
| All rights reserved | Source visible, no reuse or redistribution without permission |

Whatever is chosen covers only this mod's code. It claims nothing over Schedule I, MelonLoader, S1API, Steamworks,
FishNet or Unity, none of which is bundled.

## 8. Next steps

1. Owner runs **Batch 01** ([PHASE_08_TESTING.md §3](PHASE_08_TESTING.md#3-batch-01-instructions-rc1)) on RC1 and
   returns `Latest.log`, screenshots and PASS/FAIL per ID.
2. Failures → blockers with IDs, fixes with regression tests, new `rc.N`, targeted retest.
3. Batches 02–11 in order; B-02/B-03 decided after Batch 07, B-05 after Batch 09.
4. Owner decisions: B-04 (co-op scope), B-12 (Mono), B-11/B-13 (limitations).
5. Docs, changelog, release notes, checklist; final RC; approval; promotion.
