# Phase 08 release blockers

**2026-11-25 owner report:** owner states that the mod works perfectly in their in-game testing and requests the first public release. The specific scenario IDs, game logs, cash/stock evidence and co-op coverage were not provided, so the 270-scenario tracker remains unaltered. Co-op is excluded from the official v1.0.0 support guarantee; Mono gameplay is excluded. B-03 (dealer quality-lot mismatch in concurrent mixed-quality appointments) remains an explicitly disclosed unresolved risk, not a verified fix. The final reference-enabled 1.0.0 archive still needs to be rebuilt and tested after changing the embedded version.

**2026-11-25 final package smoke test:** the owner installed the exact final `TakeItOrLeaveIt-v1.0.0-Il2Cpp.zip` (SHA256 `A0D56D8772917F81ED73057CBE44ADF2D78804F71B5DD2D905316600E2D7E89D`) and reports that every smoke-test step passed: the mod initialized as `1.0.0`, the Take It phone app opened, a rule change was saved, a normal player sale and a dealer sale completed, and the log showed no new critical errors. This is recorded as a smoke test of the release package only; the scenario rows below remain unaltered.


Release gate for v1.0.0: **no open P0, and no open P1 unless the affected feature is removed from the supported
scope with the owner's approval.** A severity is never lowered to pass the gate.

| Severity | Meaning |
| --- | --- |
| **P0** | Release blocked: save corruption/loss, inventory duplication or irreversible loss, unauthorized or double settlement, double commission/XP, game-breaking crash, unsafe hooks enabled on a wrong build, authority bypass in a claimed feature |
| **P1** | Blocked until fixed or the feature is removed from scope: normal sales fail, stuck dealer appointments, wrong policy filtering, broken installation, unusable menu, wrong quality-lot removal, migration changes permissions, co-op contradicting the docs |
| **P2** | Fix when feasible; a documented limitation may be accepted with owner approval |
| **P3** | Polish, documentation, cosmetic |

Status values: OPEN, NEEDS EVIDENCE (cannot be decided without the physical test named), FIXED (commit, awaiting
retest), VERIFIED (physical retest passed), SCOPED OUT (approved scope reduction), ACCEPTED (approved limitation).

## Open items at the start of Phase 08 (RC1)

| ID | Sev | Area | Issue | Status | Decides it | Disposition / next step |
| --- | --- | --- | --- | --- | --- | --- |
| B-01 | P0 (gate) | Everything | **No physical acceptance of Phases 03–07 exists.** Every sale, dealer, save and co-op behaviour is verified only by automated tests and call-graph analysis | OPEN | Batches 01–11 | Run the batches; each failure becomes its own item |
| B-02 | P1 | Dealers | **Empty handover is unobserved.** When a managed appointment is short of permitted stock, D1 hands over nothing. What the customer/contract does next (wait, leave, fail, expire, or a false completion) is PROBABLE only. A false completion would be P0; a stuck appointment P1 | NEEDS EVIDENCE | P08-EH01, P04:ST01–ST03 | If the game completes, pays or sticks: fix with a verified lifecycle mechanism before release |
| B-03 | P1 | Dealers / quality lots | **Higher-tier removal is accepted.** The game's removal takes a *minimum* quality (`RemoveAndReturnProductFromInventory(id, qty, minQuality)`); slot order is PROBABLE. Phase 07 D1 accepts a removal of a *different* tier that still meets the minimum (logs it and force-reconciles). That can consume units reserved by another appointment (Premium taken for a Standard reservation), which the Phase 08 spec does not accept | NEEDS EVIDENCE | P08-QL01, P08-QL02, P05:QD03, P06:QL01 | If the game ever removes another tier: treat the removal as failed (hand the items back, release, retry) and add regression tests; if it never does, keep the detector and document it. **Code change expected either way** to stop counting a cross-reservation removal as a success; deferred until the game shows which slots it takes, so the fix matches real behaviour |
| B-04 | P1 (scope decision) | Co-op | **Client handovers are not host-validated.** A client's own handover is judged on the client by the game's exact-product rule; the host learns of it only through `ProcessHandoverServerSide`, which has no contract argument and a client-computed payment and is a hash-named generated RPC (must not be patched). An altered client could submit anything the vanilla game would accept from it; this is a vanilla-game trust property, not something the mod adds, but the mod cannot enforce its policy there | OPEN — owner decision | §22 investigation (see audit §5) + P08-CH01/CH02 | Options in [PHASE_08_RELEASE_AUDIT.md](PHASE_08_RELEASE_AUDIT.md) §5. Recommended: ship co-op as **"host-side features only; client handovers are vanilla and outside the enforcement guarantee"**, never advertised as host-authoritative player sales |
| B-05 | P1 | Co-op | Steam lobby rules transport is compile- and simulation-verified only (ownership, propagation, join-in-progress) | NEEDS EVIDENCE | P07:SA02–SA06, P07:NF01–NF06 | If it fails in game: the read-only client display is cut from scope (clients would show "Waiting for host rules"; nothing unsafe) |
| B-06 | P1 | Release | No LICENSE; README said the license was undecided | RESOLVED — owner chose **MIT** (2026-11-24) | — | `LICENSE` added (MIT, © 2026 Taulant Haxhiu); README updated. Whether the release ZIP also carries `LICENSE` is decided with the final package layout |
| B-07 | P1 | Offers | Offer filter's dealer argument identifying the dealer is PROBABLE; customer budget/price coherence of filtered offers unobserved | NEEDS EVIDENCE | P05:OF503, P04:OF03, P04:OF05 | If wrong: dealer-specific overrides for offers cut from scope or fixed |
| B-08 | P1 | Player sales | Match/payment model PROBABLE: units = Quantity × Amount, match fraction, whether payment scales with match, `GetError` polarity | NEEDS EVIDENCE | P03:S1–S9, P03-T20–T23 | Any wrong payment = P0 |
| B-09 | P1 | Discovery / rules | `ProductManager.mixRecipes` runtime content (lineage for base-strain rules) is PROBABLE | NEEDS EVIDENCE | P05:BS01–BS05, P05:UA01 | If incomplete: base-strain mode documented as limited; unknown lineage already fails closed |
| B-10 | P1 | Persistence | Phase 06 assumptions: save path stability, NoSave between loads, `OnDeinitializeMelon` on quit, dealer ID stability, pruning after loading an older game save | NEEDS EVIDENCE | Batch 08 | Failures become their own items |
| B-11 | P2 | Dealers | Restock texts are host-local in co-op (`network: false`) | ACCEPTED in Phase 07 docs; owner to confirm | — | Documented limitation |
| B-12 | P2 | Compatibility | Mono build has no sales hooks and no phone app (no Mono references exported) | OPEN — scope | — | Recommended: v1.0.0 ships IL2CPP only; Mono listed as unsupported |
| B-13 | P2 | Player sales | Customer phone message / journal names the game's chosen product, not the category | ACCEPTED limitation (documented since Phase 03) | P04:OF05 | Owner to confirm for release |
| B-14 | P3 | Docs | README, ROADMAP, INSTALLATION still describe development phases; no CHANGELOG, release notes, RELEASE_CHECKLIST | OPEN | — | Rewritten after physical results (Stage 08.12) |
| B-15 | P3 | Process | CI artifact name did not say it lacks the game hooks; no record tying evidence to a build | FIXED (artifact renamed `TakeItOrLeaveIt-CI-no-game-hooks-<sha>`; RC table in PHASE_08_TESTING §1) | — | `docs/RELEASE_CHECKLIST.md` (hash recording per RC); evidence must quote the RC id |
| B-16 | P3 | Logging | Startup line still reads `Phase 05: player sales hooks active; dealer sales hooks active.` (stale phase label; content correct) | FIXED in rc.2 (now `Sales hooks: player sales hooks active; …`) | P05:IN01 | INSTALLATION.md updated |

## Phase 08 hardening (rc.2)

Found by the code audit ([PHASE_08_CODE_AUDIT.md](PHASE_08_CODE_AUDIT.md)), not by a physical test. FIXED means fixed
in code with regression tests; each still needs its physical retest.

| ID | Sev | Area | Issue | Status | Decides it | Disposition / next step |
| --- | --- | --- | --- | --- | --- | --- |
| B-17 | P1 | Player and dealer sales | **Held contract fell back to vanilla** when session authority was briefly unconfirmed or a hook threw (A-01, A-02): the game's exact-product judgement or removal ran for a contract the mod holds | FIXED in rc.2 (fail closed: refused / nothing handed over, contract kept) | P08-HD02, P08-HD03, P08-HD04, P08-HD06 | Retest on rc.2 |
| B-18 | P0 class | Dealers | **Rollback failure lost items** (A-03): an item that could not be given back to the dealer was dropped | FIXED in rc.2 (queued and retried per dealer; reported in Diagnostics; loss after 20 attempts logged as an error) | P08-HD04 (normal path) | The failure itself cannot be provoked in play; keep watching logs for `could not give a removed item back` |
| B-19 | P2 | Privacy / package | **Build path with the developer's user name inside the shipped DLLs** (A-07) | FIXED in rc.2 (`PathMap`; byte scan of the rc.2 DLLs finds no `Users` path) | Package check per RC | RC1 ZIP must not be shared further |
| B-20 | P2 | Privacy / logs | **Absolute paths in log lines and diagnostics** from exception messages; untrusted text unbounded (A-08, A-09) | FIXED in rc.2 (`LogText` redaction in the log adapter) | P08-HD01 | Retest on rc.2 |
| B-21 | P3 | Robustness | Address-keyed caches (A-04–A-06), damaged `mixedDeliveries` widened an order (A-10), unbounded file reads (A-11), one-time log cap silent (A-12) | FIXED in rc.2 | P08-HD03, P08-HD05 | Retest on rc.2 |

New failures from the physical batches are added here with the next free ID, the scenario ID, the RC they were
seen on, and a link to the fix commit.
