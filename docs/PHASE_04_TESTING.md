# Phase 04 testing: hired dealer sales

> **Status: IMPLEMENTATION COMPLETE (incl. critical enhancements) — PHYSICAL ACCEPTANCE NOT TESTED.** Nothing in
> §4–§5 or §8 has been run in game. The enhancements (offers, fair pricing, strict dealers, restock messages) are
> described in [PHASE_04_ENHANCEMENTS.md](PHASE_04_ENHANCEMENTS.md).
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
   [TakeItOrLeaveIt] Patch module applied: Customer offers (policy filter)
   [TakeItOrLeaveIt] Harmony patches active: 13 method(s) across 3 module(s).
   [TakeItOrLeaveIt] Dealer sales: ...
   [TakeItOrLeaveIt] Dealer appointment hook: Active
   [TakeItOrLeaveIt] Dealer fulfilment hook: Active
   [TakeItOrLeaveIt] Dealer stock reconcile: Active
   [TakeItOrLeaveIt] Dealer sales authority: Single player host.
   [TakeItOrLeaveIt] Dealer sales: Active.
   [TakeItOrLeaveIt] Customer offer filter: Active
   [TakeItOrLeaveIt] Fair pricing: Active (substitutes must be worth at least the quoted unit price)
   [TakeItOrLeaveIt] Strict dealer fulfilment: On (managed appointments never fall back to the requested product)
   [TakeItOrLeaveIt] Dealer restock messages: Active
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
Dealer appointment created: <guid>: 5 x marijuana (any of 3 product(s), mixed on, policy r.., quote ..) for dealer <npc id>: Reserved (...)
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
| DS03 | Same as DS02, Mixed **OFF** before acceptance | Let the deal time come | Appointment waits; at the handover **nothing** is handed over (`nothing handed over`), no payment; the dealer texts a shortage |
| DS04 | Dealer holds N of the ordered product and lots of Meth | Complete | Only Weed removed; Meth untouched |
| DS05 | Selected Only with one Weed ticked; dealer holds a lot of an unticked Weed | Complete | Unticked Weed never delivered; if the ticked one is short nothing is handed over |
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

`dotnet test TakeItOrLeaveIt.sln -c Release`: **458 tests pass** (337 from Phases 00–03, 53 Phase 04 cases and
68 enhancement cases in suites O, P, M and S; see the second table). Suites A–C and E run against Core directly; suite D and the stress test run against
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
| P04-T28 | C | Short stock withholds the handover and keeps waiting (strict) |
| P04-T29 | C | Second handover removes nothing |
| P04-T30 | C | Lost stock replans to another product |
| P04-T31 | C | Repeated stock loss keeps the appointment managed (strict) |
| P04-T32 | C | Contract moved to another dealer follows the new dealer (strict) |
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

Enhancement suites (all pass; S and the harness-based P cases run on the **simulated** dealer/settlement):

| Suite | File | Cases | Covers |
| --- | --- | --- | --- |
| O, customer offers | `CustomerOfferTests.cs` | O01–O15 | Any Unlocked, Selected Only, Disabled, empty selection, global off, settings/catalog not ready, client/multiplayer, locked, unknown and blank candidates, order kept, counters, live policy, offer/quote/snapshot consistency, status |
| P, fair pricing | `FairPricingTests.cs` | P01–P17 | Sour Diesel quote refuses OG Kush, pricier/equal substitutes, inclusive boundary, multi-line average, missing/unpriced prices, no price source, `NoFairProduct`, every mixed basket ≥ quote, downgrade of unprovable snapshots, persistence, legacy snapshots, damaged price data, player settlement pays the game's quote once, dealer never reserves a cheaper product, float cleaning |
| M, restock messages | `DealerMessagingTests.cs` | M01–M22 | Never-stocked categories, settle period, exact texts, once only, re-arm after restock, failed send retry/back-off/give-up, permitted units only, Disabled category, other categories, shortage numbers, other appointments' holds, Mixed off best product, grow-only resend, episodes, shortage replaces out-of-stock, per-dealer identity, reload dedup, save switch, mod off/not host, throttle, damaged history |
| S, strict fulfilment | `StrictFulfilmentTests.cs` | S01–S13 (S05 runs twice) | No excluded fallback, no partial delivery, short removal rolled back, removal that cannot start, deadline/fail cleanup, unmanaged excluded contract withheld, unmanaged permitted vanilla, global off vanilla, multiplayer vanilla, repeated withholds never pay, rehome to an empty dealer, normal path unchanged, **randomised 3000-step stress (seed 9001, simulated)**: all-or-nothing deliveries, snapshot products only, basket ≥ quote, at most one payment, ledger invariants after every step |

All P04-T01..T48 pass. Development-PC figures (Release): the stress run settled 113 of 421 contracts with
every ledger invariant checked after each of 3000 steps; the benchmark ran 250 dealer reconciles over 1000
appointments in about 22 ms. In game, a reconcile runs at most every 3 seconds per dealer and only for
dealers with open appointments.

## 7. Evidence to send back

1. `MelonLoader/Latest.log` covering §4 (no private paths; contract ids are random GUIDs, dealer ids are NPC ids).
2. Before/after notes (your cash, dealer cash, XP, dealer inventory) for DS01, DS02 and DS17.
3. `<Schedule I>/UserData/TakeItOrLeaveIt/saves/*.dealer-orders.json` while an appointment is open.
4. Screenshots of Diagnostics for DS06, DS07 and DS12.
5. The tables in §5 and §8 filled in.
6. `<Schedule I>/UserData/TakeItOrLeaveIt/saves/*.dealer-alerts.json` after MSG02 and MSG05, and screenshots of
   the dealer's phone conversation.

## 8. Enhancement scenarios

Settings as §2 unless stated. Write down the prices shown in the Products app for the strains you use, your
cash, the dealer's cash and inventory before each scenario. Only mark PASS what you saw in game.

### Customer offers (OF)

| # | Setup | Action | Expected |
| --- | --- | --- | --- |
| OF01 | Weed = Selected Only, only Sour Diesel ticked, several Weeds listed | Wait for a few player offers (texts) | Every Weed offer names Sour Diesel |
| OF02 | Weed = **Disabled**, Meth listed | Wait for offers | No new Weed offers; Meth offers still arrive |
| OF03 | Dealer customers, dealer holds OG Kush and Sour Diesel, only Sour Diesel ticked | Wait for dealer offers | Dealer offers only Sour Diesel |
| OF04 | Mod **off** for the save | Wait for offers | Vanilla offers (any listed strain); `Customer offer filter` counters do not grow |
| OF05 | Any offer from OF01 | Open the message, journal entry and handover screen | Same product, same quantity, same payment everywhere |

### Pricing (PR)

| # | Setup | Action | Expected |
| --- | --- | --- | --- |
| PR01 | Sour Diesel priced above OG Kush, both ticked; accept a Sour Diesel order | Offer OG Kush at the handover | Refused before anything moves (*not allowed*) |
| PR02 | Same order | Hand over a ticked strain priced at least as high as Sour Diesel | Accepted; cash changes once by the offer's payment |
| PR03 | Same order, Mixed ON | Hand over 3 Sour Diesel + 2 of a pricier strain | Accepted, paid once |
| PR04 | Accept an OG Kush order (cheapest) | Hand over Sour Diesel | Accepted |
| PR05 | Dealer appointment for Sour Diesel; dealer holds only cheaper OG Kush | Let the deal time come | Nothing handed over; shortage text; OG Kush untouched |

### Dealer messages (MSG)

| # | Setup | Action | Expected |
| --- | --- | --- | --- |
| MSG01 | Load a save with hired dealers | Watch the phone for 1 minute | No burst of dealer texts on load |
| MSG02 | Dealer holds permitted Weed | Let them sell it all (or take it back) | One text: *Yo boss, I'm out of Weed I can sell. Need you to restock me.* in that dealer's conversation, with the game's notification |
| MSG03 | After MSG02 | Wait 2 minutes | No repeat |
| MSG04 | After MSG02 | Give the dealer permitted Weed, then take it away again | A second out-of-stock text |
| MSG05 | Dealer has 2 permitted Weed, appointment needs 5 | Wait a few seconds after acceptance | *Boss, I've got a customer waiting for 5 Weed, but only 2 permitted units are available. Need 3 more.* |
| MSG06 | After MSG05 | Take one more unit away | A new text with *Need 4 more*; giving 1 back sends nothing |
| MSG07 | Mixed OFF; dealer has 3 OG Kush + 2 Sour Diesel, appointment needs 5 | Wait | Text says *only 3 permitted units* |
| MSG08 | Dealer holds only an unticked Weed | Wait | Counts as out of Weed (text if the dealer stocked permitted Weed before) |
| MSG09 | After MSG02 | Save, quit, load | No repeat of the delivered text |
| MSG10 | Weed Disabled | Dealer runs out | No Weed text |

### Strict fulfilment (ST)

| # | Setup | Action | Expected |
| --- | --- | --- | --- |
| ST01 | Only OG Kush ticked; appointment (vanilla asked for Sour Diesel); dealer has 2 OG Kush and plenty of Sour Diesel | Let the deal time come | Log `nothing handed over`; Sour Diesel and OG Kush untouched; no payment, no cut, no XP. **Note what the customer does** (refuses, leaves, contract fails) |
| ST02 | After ST01, give the dealer enough OG Kush before the deadline | Wait | Appointment reserves; next attempt (if the game retries) delivers OG Kush; or the contract fails/expires cleanly |
| ST03 | ST01 left to its deadline | Wait | Contract expires/fails through the game; diagnostics back to 0 appointments; nothing removed |
| ST04 | Move a customer with an appointment to another dealer who has permitted stock | Deal time | The new dealer delivers from their own stock; the old dealer keeps theirs |

### Regression (RG)

| # | Action | Expected |
| --- | --- | --- |
| RG01 | DS01, DS02, DS17 again | Unchanged results |
| RG02 | A Phase 03 player sale with a permitted, equally priced substitute | Accepted, paid once |
| RG03 | Load a save that has `*.orders.json` / `*.dealer-orders.json` from the previous Phase 04 build | Log `predates the fair-price rule`; those orders accept only their requested strain |

### Status table (fill in)

| Scenario | Status | Notes |
| --- | --- | --- |
| OF01 | NOT TESTED | |
| OF02 | NOT TESTED | |
| OF03 | NOT TESTED | |
| OF04 | NOT TESTED | |
| OF05 | NOT TESTED | |
| PR01 | NOT TESTED | |
| PR02 | NOT TESTED | |
| PR03 | NOT TESTED | |
| PR04 | NOT TESTED | |
| PR05 | NOT TESTED | |
| MSG01 | NOT TESTED | |
| MSG02 | NOT TESTED | |
| MSG03 | NOT TESTED | |
| MSG04 | NOT TESTED | |
| MSG05 | NOT TESTED | |
| MSG06 | NOT TESTED | |
| MSG07 | NOT TESTED | |
| MSG08 | NOT TESTED | |
| MSG09 | NOT TESTED | |
| MSG10 | NOT TESTED | |
| ST01 | NOT TESTED | |
| ST02 | NOT TESTED | |
| ST03 | NOT TESTED | |
| ST04 | NOT TESTED | |
| RG01 | NOT TESTED | |
| RG02 | NOT TESTED | |
| RG03 | NOT TESTED | |

Use PASS, FAIL, BLOCKED (could not set it up) or NOT TESTED.
