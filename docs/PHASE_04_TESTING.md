# Phase 04 testing: hired dealer sales

> **Status: IMPLEMENTATION COMPLETE — PHYSICAL ACCEPTANCE NOT TESTED.** Nothing in §4–§5 has been run in game.
> Only mark PASS what you observed; use FAIL, BLOCKED (could not set it up) or NOT TESTED otherwise.

## 1. Before you start

1. **Back up the save** (copy the whole save folder). Use a save with **at least one hired dealer** who has
   customers assigned, at least two unlocked Weed products, and one other category (Meth or Cocaine).
2. Build and install as in Phase 03 ([INSTALLATION.md](INSTALLATION.md)):

   ```powershell
   pwsh tools/Package-Mod.ps1 -Configuration Release
   ```

   The output must contain `TakeItOrLeaveIt: building WITH the S1API phone app` and
   `TakeItOrLeaveIt: building WITH the player and dealer sales game hooks`. Copy
   `artifacts/package/Il2Cpp/Mods/TakeItOrLeaveIt.Il2Cpp.dll` to `<Schedule I>/Mods/` and
   `artifacts/package/Il2Cpp/UserLibs/TakeItOrLeaveIt.Core.dll` to `<Schedule I>/UserLibs/` (replace both).
3. Launch, load the backup copy, and check `MelonLoader/Latest.log`:

   ```text
   [TakeItOrLeaveIt] Patch module applied: Player sales (category orders)
   [TakeItOrLeaveIt] Patch module applied: Dealer sales (category appointments)
   [TakeItOrLeaveIt] Harmony patches active: 12 method(s) across 2 module(s).
   [TakeItOrLeaveIt] Dealer sales: ...
   [TakeItOrLeaveIt] Dealer appointment hook: Active
   [TakeItOrLeaveIt] Dealer fulfilment hook: Active
   [TakeItOrLeaveIt] Dealer stock reconcile: Active
   [TakeItOrLeaveIt] Dealer sales authority: Single player host.
   [TakeItOrLeaveIt] Dealer sales: Active.
   ```

   `Patch module '...' failed` means the game runs vanilla: stop and send the log.
4. Phone → **Take It** → Diagnostics → *Player and dealer sales* shows `Dealer sales: Active`.

## 2. Settings for the tests

Unless a step says otherwise: mod **ON**, Weed = **Any Unlocked**, Mixed deliveries **ON**, saved. Before each
scenario write down: your cash, the dealer's cash (dealer management app), XP bar, and the units of each
product in the dealer's inventory.

## 3. Getting a dealer appointment

Give the dealer product, wait for one of their customers to order, and look for:

```text
Dealer appointment created: <guid>: 5 x marijuana (any of 3 product(s), mixed on, policy r..) for dealer <npc id>: Reserved (...)
Dealer contract <guid> uses vanilla rules: <reason> (...)
```

Only a "Dealer appointment created" contract tests the new behaviour. When the dealer delivers, the log shows:

```text
Dealer order <guid>: removed 5 unit(s) (5 x sourdiesel) from dealer <npc id>; payment, cut and XP stay vanilla.
Dealer order <guid>: handover processed by the game (payment, dealer cut and XP are vanilla's).
Dealer order <guid> ended (Completed, 5 unit(s) removed, 1 handover(s) observed); reservation released.
```

## 4. Scenarios

| # | Setup | Action | Expected |
| --- | --- | --- | --- |
| DS01 | Dealer holds only a permitted Weed that is **not** the ordered one, enough units | Let the dealer complete the deal | Delivered with the substitute; exactly N units leave the dealer; one payment, cut and XP |
| DS02 | Dealer holds two permitted Weeds, neither enough alone, together ≥ N; Mixed ON | Complete | Both used; total removed = N |
| DS03 | Same as DS02, Mixed **OFF** before acceptance | Complete | Appointment waits (log/diagnostics), then returns to vanilla at handover: vanilla outcome |
| DS04 | Dealer holds N of the ordered product and lots of Meth | Complete | Only Weed removed; Meth untouched |
| DS05 | Selected Only with one Weed ticked; dealer holds a lot of an unticked Weed | Complete | Unticked Weed never delivered; vanilla outcome if the ticked one is short |
| DS06 | Dealer holds less than N eligible units | Accept, check diagnostics | `Waiting for eligible stock: 1` |
| DS07 | After DS06, give the dealer enough permitted Weed | Wait a few seconds | Log `reserved`; diagnostics shows 1 reservation |
| DS08 | Two appointments for the same dealer, stock for only one | Accept both | One Reserved, one Waiting; never both reserved |
| DS09 | Two dealers, each with their own stock and appointment | Complete both | Each dealer only loses their own units |
| DS10 | Appointment Reserved; take the reserved product back from the dealer | Wait a few seconds | Log `invalidated`; the appointment waits or replans to other stock |
| DS11 | Reserved appointment | Let the contract expire (or cancel it) | Log `ended (Expired/Canceled ...) reservation released`; stock free for the next appointment |
| DS12 | Reserved appointment | Save, quit to menu, load | Log `Restored 1 dealer appointment(s)`; diagnostics shows it waiting, then reserved again |
| DS13 | As DS12, but remove the dealer's stock before saving | Load | Appointment waits; no reservation larger than the stock |
| DS14 | Save A with an appointment; load save B | Check B's diagnostics | No appointments from A; back in A, the appointment is there |
| DS15 | Mod **OFF** for the save | Dealer delivers | Vanilla: only the exact product; log `uses vanilla rules: ModDisabled` |
| DS16 | Appointment accepted with OG Kush permitted; untick OG Kush and save | Dealer delivers OG Kush | Delivered (the appointment keeps its snapshot) |
| DS17 | Three or more dealer deliveries in a row | Complete them | Each: one payment, one cut, exact units; diagnostics returns to 0 appointments |
| DS18 | Player sale (Phase 03) while a dealer appointment is open | Complete both | Both work; neither touches the other's items |

After every completed delivery check: your cash and the dealer's cash changed **once** by the game's normal
amounts, XP once, and the dealer's inventory lost exactly the units in the `removed` log line.

## 5. Status table (fill in)

| Scenario | Status | Notes |
| --- | --- | --- |
| DS01 | NOT TESTED | |
| DS02 | NOT TESTED | |
| DS03 | NOT TESTED | |
| DS04 | NOT TESTED | |
| DS05 | NOT TESTED | |
| DS06 | NOT TESTED | |
| DS07 | NOT TESTED | |
| DS08 | NOT TESTED | |
| DS09 | NOT TESTED | |
| DS10 | NOT TESTED | |
| DS11 | NOT TESTED | |
| DS12 | NOT TESTED | |
| DS13 | NOT TESTED | |
| DS14 | NOT TESTED | |
| DS15 | NOT TESTED | |
| DS16 | NOT TESTED | |
| DS17 | NOT TESTED | |
| DS18 | NOT TESTED | |

## 6. Automated tests (development PC)

`dotnet test TakeItOrLeaveIt.sln -c Release`: **390 tests pass** (337 from Phases 00–03 plus 53 Phase 04
cases). Suites A–C and E run against Core directly; suite D and the stress test run against
`DealerHarness`, a **simulated** vanilla dealer (inventory slots, exact-id removal, customer judgement, one
payment with a 20 % cut). They prove the model, not the game.

| ID | Suite | Test |
| --- | --- | --- |
| P04-T01 | A orders | Dealer contract becomes a category appointment with a snapshot |
| P04-T02 | A | Player and dealer factories never cross |
| P04-T03 | A | Mod disabled keeps dealers vanilla |
| P04-T04 | A | Unloaded settings keep dealers vanilla |
| P04-T05 | A | Clients and multiplayer keep dealers vanilla |
| P04-T06 | A | Disabled or empty category keeps dealers vanilla |
| P04-T07 | A | Mixed-category contract keeps dealers vanilla |
| P04-T08 | A | Unidentified dealer keeps the contract vanilla |
| P04-T09 | A | Repeated acceptance keeps one snapshot and one reservation |
| P04-T10 | A | Snapshot survives later policy changes |
| P04-T11 | B ledger | Reserves from one product |
| P04-T12 | B | Mixed reservation is deterministic |
| P04-T13 | B | Mixed off uses one product with enough stock |
| P04-T14 | B | Mixed off never combines products |
| P04-T15 | B | Two appointments never share units |
| P04-T16 | B | Each dealer only promises their own stock |
| P04-T17 | B | Repeated reserve is idempotent |
| P04-T18 | B | A contract cannot hold two dealers' stock |
| P04-T19 | B | Release frees units once |
| P04-T20 | B | Ineligible products are never reserved |
| P04-T21 | B | Lost stock invalidates the newest reservations first |
| P04-T22 | B | Commit releases the hold exactly once; state machine |
| P04-T23 | C appointments | Short stock waits instead of failing |
| P04-T24 | C | Waiting appointment reserves when stock arrives |
| P04-T25 | C | Waiting appointments served in acceptance order, bounded per reconcile |
| P04-T26 | C | Dealer delivers a permitted substitute |
| P04-T27 | C | Mixed delivery removes exactly the plan |
| P04-T28 | C | No eligible stock returns to vanilla before removal |
| P04-T29 | C | Second handover removes nothing |
| P04-T30 | C | Lost stock replans to another product |
| P04-T31 | C | Repeated stock loss returns to vanilla |
| P04-T32 | C | Contract moved to another dealer returns to vanilla |
| P04-T33 | C | Expire/cancel/fail release the stock |
| P04-T34 | C | Reconcile is skipped until something changes |
| P04-T35 | D settlement | One dealer sale settles exactly once (payment, cut, XP, units) |
| P04-T36 | D | Repeated settlement callbacks are ignored |
| P04-T37 | D | Refused delivery hands items back and pays nothing |
| P04-T38 | D | Unmanaged dealer contract is exactly vanilla |
| P04-T39 | D | Forbidden products are never substituted |
| P04-T40 | D | Customer judges the dealer delivery with the snapshot |
| P04-T41 | E recovery | Appointments round-trip without reservations |
| P04-T42 | E | Reload rebuilds reservations from live stock only (no phantom stock) |
| P04-T43 | E | Damaged files never create appointments |
| P04-T44 | E | Save switching isolates appointments |
| P04-T45 | E | Prune drops vanished contracts and their holds |
| P04-T46 | E | Status reports only what is really active |
| P04-T47 | E stress | 3000 random interleavings across 3 dealers keep every invariant (**simulated**) |
| P04-T48 | E perf | 50 dealers × 20 appointments, 5 reconcile rounds (**simulated** benchmark) |

All P04-T01..T48 pass. Development-PC figures (Release): the stress run settled 113 of 421 contracts with
every ledger invariant checked after each of 3000 steps; the benchmark ran 250 dealer reconciles over 1000
appointments in about 22 ms. In game, a reconcile runs at most every 3 seconds per dealer and only for
dealers with open appointments.

## 7. Evidence to send back

1. `MelonLoader/Latest.log` covering §4 (no private paths; contract ids are random GUIDs, dealer ids are NPC ids).
2. Before/after notes (your cash, dealer cash, XP, dealer inventory) for DS01, DS02 and DS17.
3. `<Schedule I>/UserData/TakeItOrLeaveIt/saves/*.dealer-orders.json` while an appointment is open.
4. Screenshots of Diagnostics for DS06, DS07 and DS12.
5. The table in §5 filled in.
