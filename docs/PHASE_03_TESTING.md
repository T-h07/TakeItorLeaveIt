# Phase 03 testing: player sales

> **Status: IMPLEMENTATION COMPLETE — PHYSICAL ACCEPTANCE DEFERRED.** Nothing below §6 has been run in game.
> Physical tests stay **DEFERRED** until you run them; only mark PASS what you observed.

## 1. Before you start

1. **Back up the save** you will test with (copy the whole save folder somewhere safe). Use a save with at
   least two unlocked Weed products and at least one other category (Meth or Cocaine), and a few customers.
2. Build and install (same as Phase 02, see [INSTALLATION.md](INSTALLATION.md)):

   ```powershell
   pwsh tools/Package-Mod.ps1 -Configuration Release
   ```

   The build output must contain both lines
   `TakeItOrLeaveIt: building WITH the S1API phone app` and `TakeItOrLeaveIt: building WITH the player and dealer sales game hooks`.
   Copy `artifacts/package/Il2Cpp/Mods/TakeItOrLeaveIt.Il2Cpp.dll` to `<Schedule I>/Mods/` and
   `artifacts/package/Il2Cpp/UserLibs/TakeItOrLeaveIt.Core.dll` to `<Schedule I>/UserLibs/` (replace both).
3. Launch, load the backup copy of the save, and check `MelonLoader/Latest.log`:

   ```text
   [TakeItOrLeaveIt] Patch module applied: Player sales (category orders)
   [TakeItOrLeaveIt] Harmony patches active: 10 method(s) across 1 module(s).
   [TakeItOrLeaveIt] Player Sales: ...
   [TakeItOrLeaveIt] Offer hook: Active
   [TakeItOrLeaveIt] Handover validation: Active
   [TakeItOrLeaveIt] Transaction integration: Active
   [TakeItOrLeaveIt] Player sales authority: Single player host.
   [TakeItOrLeaveIt] Player Sales: Enabled.
   ```

   If you see `Patch module 'Player sales (category orders)' failed`, stop and send the log: the game runs
   vanilla in that case.
4. Phone → **Take It** → Diagnostics → *Player sales* should show `Player Sales: Enabled`.

## 2. Settings for the tests

Unless a step says otherwise: mod **ON**, Weed = **Any Unlocked**, Mixed deliveries **ON** (Settings →
Customer orders), saved. Write down, before each sale: cash, XP/rank bar, the customer's relationship, and
the units of each product you carry.

## 3. How to get an order

Wait for (or trigger) a customer message offering a deal, **accept it**, and look for one of these in the log:

```text
Category order created: <guid>: 5 x marijuana (any of 3 product(s), mixed on, policy r..).
Contract <guid> uses vanilla rules: <reason> (...)
```

Only a "Category order created" contract tests the new behaviour. Then go to the meeting point and talk to
the customer to open the handover screen. Its description must read like
`Deliver exactly 5 Weed, any of: ... Mixing products is allowed.`

## 4. Sales to perform

Use the order's own numbers where the table says *N* (for example N = 5). "Refused" means: red message, Done
disabled, nothing leaves your inventory. Close the handover (Cancel) after each refused attempt and check
your inventory is unchanged.

| # | Order | Put in the slots | Settings | Expected |
| --- | --- | --- | --- | --- |
| S1 | N Weed | N of one permitted Weed that is **not** the product named in the message | default | Accepted; normal success chance |
| S2 | N Weed | A mix of two permitted Weeds adding up to N | Mixed ON | Accepted |
| S3 | N Weed | Same mix as S2 | Mixed OFF (saved **before accepting** the order) | Refused: "Mixed deliveries are off" |
| S4 | N Weed | N Meth or Cocaine | default | Refused: "only wants Weed" |
| S5 | N Weed | N of a Weed you unticked under Selected Only **before accepting** | Selected Only | Refused: "not allowed" |
| S6 | N Weed | Fewer than N | default | Refused: "Needs N Weed: you offered ..." |
| S7 | N Weed | More than N | default | Refused: "Needs exactly N" (no extra units taken) |
| S8 | N Weed | Unpackaged product | default | Refused: "Package the product first" |
| S9 | N Weed | Exactly N valid units, then press Done twice quickly | default | One sale only |
| S10 | Order accepted with OG Kush permitted; then untick OG Kush and Save | N OG Kush | — | Accepted (the order keeps its snapshot) |
| S11 | Order accepted after S10 | N OG Kush | — | Refused ("not allowed") |
| S12 | Any order accepted with the mod **OFF** | Exact product named in the message | mod OFF | Vanilla: only the exact product works |

After every accepted sale write down again: cash (+ the agreed payment, once), XP (one normal award), the
relationship change, and your inventory (exactly the delivered units gone). The log shows:

```text
Order <guid>: N marijuana accepted (match 1.00, recipe not compared).
Order <guid>: handover processed by the game (payment, XP and items are vanilla's).
Order <guid> ended (Completed, 1 handover(s) observed); snapshot removed.
```

## 5. Lifecycle checks

1. **Save/reload:** accept a category order, save, quit to menu, load again, then complete it with a
   substitute product. Log after loading: `Restored 1 active category order(s) for <save>`.
2. **Save switching:** with an active order in save A, load save B: B's orders file is separate; B's customers
   behave per B's settings. Go back to A: the order still works.
3. **Expiry/cancel:** let a category order expire (or cancel it). Log: `Order <guid> ended (Expired ...)`.
4. **Dealer:** let a dealer complete a deal: nothing from Take It in the log except possibly
   `Contract <guid> uses vanilla rules: NotPlayerContract`; the dealer behaves as before.
5. **Repeat:** complete three or more category orders in a row.

## 6. Automated tests (done on the development PC)

`dotnet test TakeItOrLeaveIt.sln -c Release --no-build`: **337 tests pass** (no game needed), including the
Phase 03 suites:

| Suite | File | Covers |
| --- | --- | --- |
| A — category offers | `CustomerOrderTests.cs` | when a contract becomes a category order, quantity, disabled/empty categories, global disable, dealers, identity, file round trip |
| B — handover eligibility | `MixedDeliveryTests.cs` | the brief's 13-row table, duplicates, bad quantities, missing references, packaging, units per package, 1000-product/200-stack benchmark |
| C — transaction integrity | `SaleTransactionTests.cs` | a simulated vanilla settlement counting completions, payments, XP, relationship and inventory; repeats, cancel/expire/fail, stale callbacks, state machine, match maths |
| D — mid-order changes | `ActiveContractTests.cs` | snapshot vs new rules, disabling, unlocks, save switching, restart, cleanup, corrupt files, unsupported sessions, unreadable settings |
| E — satisfaction/regression | `SalesRegressionTests.cs` | substitution scores like the requested recipe, quality still counts, vanilla orders, dealers, no extra payout, the mixed setting and its menu toggle, status report |

Suite C proves the model, not the game: §4 and §5 check the same invariants in game.
Benchmark (development PC, Release): 100 validations of 200 stacks against a 1000-product catalog took
114 ms in total including building the catalog, i.e. about 1 ms per validation. Validation runs only when the
handover items change (results are cached), never per frame.

## 7. Acceptance checklist

| ID | Test | Status |
| --- | --- | --- |
| P03-T01 | Feature branch created correctly | PASS (`feature/phase-03-player-sales` from `develop` at the Phase 02 merge) |
| P03-T02 | Phase 01 and 02 integration confirmed | PASS (code review: validation uses `SaleEligibilityPolicy` + `InventoryAllocationPlanner`; the menu's saved policy feeds the snapshots) |
| P03-T03 | Customer lifecycle hooks investigated | PASS (static call graph, [PHASE_03_PATCH_ANALYSIS.md](PHASE_03_PATCH_ANALYSIS.md)) |
| P03-T04 | Handover acceptance hook verified | DEFERRED (exists in the installed build; runtime behaviour unobserved) |
| P03-T05 | Game build loads the mod | DEFERRED |
| P03-T06 | Mod menu still operates | DEFERRED |
| P03-T07 | Global enable/disable works | DEFERRED (S12) |
| P03-T08 | Category-based offer displayed correctly | DEFERRED (§3; phone message limitation documented) |
| P03-T09 | Existing vanilla contracts remain safe | DEFERRED (S12, contracts accepted before installing) |
| P03-T10 | One eligible product fulfils a valid order | DEFERRED (S1) |
| P03-T11 | Two eligible products fulfil a mixed order | DEFERRED (S2) |
| P03-T12 | Mixed-stack-disabled behaviour | DEFERRED (S3) |
| P03-T13 | Wrong category rejected | DEFERRED (S4) |
| P03-T14 | Unselected product rejected | DEFERRED (S5) |
| P03-T15 | Locked or unknown product rejected | DEFERRED (automated only; hard to force in game) |
| P03-T16 | Insufficient quantity rejected | DEFERRED (S6) |
| P03-T17 | Excess quantity causes no excess consumption | DEFERRED (S7) |
| P03-T18 | Successful delivery consumes exactly the required items | DEFERRED |
| P03-T19 | Rejected handover preserves inventory | DEFERRED |
| P03-T20 | Payment credited exactly once | DEFERRED |
| P03-T21 | XP awarded correctly and once | DEFERRED |
| P03-T22 | Relationship progression correct | DEFERRED |
| P03-T23 | Allowed recipe mismatch has no mismatch-only penalty | DEFERRED (compare satisfaction popup of S1 with an exact-product sale of the same quality) |
| P03-T24 | Repeated Submit cannot duplicate | DEFERRED (S9) |
| P03-T25 | Mid-order policy changes predictable | DEFERRED (S10, S11) |
| P03-T26 | Cancellation/expiry | DEFERRED (§5.3) |
| P03-T27 | Active order survives save/reload | DEFERRED (§5.1) |
| P03-T28 | Save switching does not leak orders | DEFERRED (§5.2) |
| P03-T29 | Global disable preserves safe behaviour | DEFERRED |
| P03-T30 | Repeated customer sales | DEFERRED (§5.5) |
| P03-T31 | Dealer behaviour unchanged | DEFERRED (§5.4) |
| P03-T32 | No mod crash, deadlock or unhandled exception | DEFERRED |
| P03-T33 | Existing automated tests pass | PASS (all Phase 00-02 tests unchanged and passing) |
| P03-T34 | New transaction tests pass | PASS (suites A-E) |
| P03-T35 | Both targets build | PASS (IL2CPP with and without local references, Mono; 0 warnings) |
| P03-T36 | GitHub Actions passes | see the pull request checks |
| P03-T37 | Documentation updated | PASS |
| P03-T38 | PR targets `develop`, not merged | see the pull request |

## 8. Evidence to send back

1. `MelonLoader/Latest.log` of a session covering §4 and §5 (it contains no private paths; contract and save
   ids are random/hashed).
2. Your before/after notes (cash, XP, relationship, inventory) for S1, S2 and S9.
3. Screenshots of the handover screen for S1 (description), S4 and S6 (red messages), and the satisfaction
   popup after S1.
4. `<Schedule I>/UserData/TakeItOrLeaveIt/saves/*.orders.json` while an order is active.
5. The checklist above with PASS / FAIL / BLOCKED.
