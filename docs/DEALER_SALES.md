# Dealer sales (Phase 04): hired dealers fill category orders

> **Status:** implemented and covered by automated tests; **not yet observed in game** (see
> [PHASE_04_TESTING.md](PHASE_04_TESTING.md)). The hooks were chosen from the installed build's call graph
> ([PHASE_04_PATCH_ANALYSIS.md](PHASE_04_PATCH_ANALYSIS.md)); the behaviour marked PROBABLE there is what the
> in-game tests confirm.

When a hired dealer accepts a customer's contract while the mod is on, the contract becomes the same
**"N units of a category"** order as a player sale (see [PLAYER_SALES.md](PLAYER_SALES.md)). The dealer then
hands over any product the Take It settings permitted for that category when the contract was accepted,
taken from **stock reserved for that appointment**. Payment, the dealer's cut, XP and the relationship stay
exactly as the game does them.

## 1. When a dealer contract is managed

The same rules as player orders, through the same code (`CategoryOrderFactory.CreateForDealer`):

| Condition | Result |
| --- | --- |
| Mod off for this save, or settings not loaded / unreadable | Vanilla |
| Product catalog not ready, unknown product, invalid quantity | Vanilla |
| The order lists products from more than one category | Vanilla |
| The category is *Disabled*, or *Selected Only* with nothing selected | Vanilla |
| The dealer cannot be identified | Vanilla |
| Multiplayer session or not the host | Vanilla |
| Otherwise | **Managed appointment** |

The snapshot (category, units, permitted products, mixed deliveries, policy revision) is taken at
acceptance and never changes afterwards. A product unticked later is still delivered for this appointment;
new appointments use the new rules.

## 2. Stock reservations

Each dealer has their own inventory, and several appointments can be open at once. To make sure the same
units are never promised twice, the mod keeps a **reservation ledger**:

- At acceptance the appointment reserves its units from the dealer's **eligible** stock (permitted, unlocked,
  packaged, right category) minus what that dealer's other appointments already hold.
- If there is not enough, the appointment is **Waiting for eligible stock**. It is retried whenever the
  dealer's stock changes (checked on the dealer's tick, at most every 3 seconds per dealer), oldest
  appointment first.
- **Mixed deliveries ON:** units are drawn across permitted products in product-id order.
  **OFF:** one product with enough units for the whole order (the one with the most units).
- Reservations only exist in the mod's memory. They never change the dealer's inventory, never create or
  hide items, and are never written to disk.

```text
                  acceptance
                      │
          ┌───────────┴────────────┐
   enough stock                not enough
          ▼                         ▼
      Reserved ◀──── stock arrives ── WaitingForEligibleStock
          │  ╲                          ▲
          │   ╲── stock lost ───────────┘   (stays managed; strict mode)
          ▼
      Committing ── the game's removal ran ──▶ CommitObserved ──▶ Completed
          │
   any state ── contract expired / cancelled / failed ──▶ ended, reservation released
```

If reserved stock disappears (you take it back, it was sold elsewhere), the newest reservations that no longer
fit are **invalidated** and replan. An appointment that keeps losing its stock stays managed (strict mode); after
5 losses it is reported once in diagnostics.

Only **fair** products are reserved: a substitute must be worth at least the quoted unit price
([PHASE_04_ENHANCEMENTS.md §2](PHASE_04_ENHANCEMENTS.md#2-fair-pricing-fixed-quote-rule)).

## 3. The handover

The game's dealer walks to the customer and runs its handover routine. At the moment it would remove the
requested product from the dealer's inventory (`Dealer.RemoveContractItems`), the mod:

1. reads the dealer's current inventory and reconciles the reservations with it;
2. makes sure this appointment holds a reservation. If it cannot, **nothing is removed and nothing is handed
   over**: the game's exact-product removal does not run, the appointment keeps waiting and the dealer texts you
   (strict mode, [PHASE_04_ENHANCEMENTS.md §3](PHASE_04_ENHANCEMENTS.md#3-strict-dealer-fulfilment));
3. calls the game's own `Dealer.RemoveAndReturnProductFromInventory(product, units, quality)` once per
   reserved product, so packaging splits, slot updates and networking are the game's;
4. releases the reservation (the stock has left the inventory) and logs the planned and removed units. If the
   removal returned a different amount, every removed item goes back to the dealer and nothing is handed over.

If the game moves the contract to another dealer, the appointment follows that dealer.

A second handover call for the same appointment removes nothing. The customer then judges the items with
the Phase 03 rules (any permitted product, quality still counts, no recipe-mismatch penalty), and the game
pays, takes the dealer's cut, awards XP and completes the contract. If the customer refuses, the game hands
the items back to the dealer as usual.

## 4. Economy safety

- The mod never calls payment, cash, cut, XP or completion methods.
- Inventory is only removed by the game's removal method, with exactly the reserved units.
- Settlement is observed once (`Customer.ProcessHandover`); repeats are logged and ignored.
- Contracts that are not managed (mod off, dealer contracts accepted before installing, multiplayer) run the
  game's code unchanged, except that while the mod is on and ready a dealer never hands over a product the
  settings exclude.
- Substitutes are never cheaper than the quote, so the game's fixed payment always matches the delivered value.

## 5. Save, reload and save switching

Appointments are stored in `UserData/TakeItOrLeaveIt/saves/<save id>.dealer-orders.json` (next to the player
orders file): snapshot, dealer, acceptance order, and whether the game already processed the handover.
Reservations are **not** stored: after loading, every appointment waits until the dealer's live inventory is
read, then reserves again. So a reload can never promise stock that no longer exists. Each save has its own
file. Appointments whose contract no longer exists are dropped 20 seconds after loading and every minute after.

## 6. Diagnostics

At startup and in the phone app (Diagnostics → Player and dealer sales):

```text
Dealer sales: Active
Dealer appointment hook: Active
Dealer fulfilment hook: Active
Dealer stock reconcile: Active
Managed dealer appointments: 2
Waiting for eligible stock: 1
Stock reservations: 1 (5 unit(s))
Last dealer error: none
```

Followed by the offer filter, fair pricing, strict fulfilment and restock message lines
([PHASE_04_ENHANCEMENTS.md §6](PHASE_04_ENHANCEMENTS.md#6-diagnostics)).

`Unavailable` means a hook is missing (CI or Mono build, or a game update changed a method: every mod patch
is then removed and the game runs vanilla). `Disabled` means the mod is off for this save.

## 7. Known limitations

- Single player only (Phase 07 for co-op).
- Acceptance stays vanilla: a dealer accepts a contract with the game's own stock check
  (`ShouldAcceptContract`, not patched). Offers only name permitted products now (offer filter).
- A short appointment still makes the dealer go to the customer; the handover is withheld and the game's own
  failed-deal handling applies (PROBABLE until ST01 runs).
- Quality (Phase 05): stock is tracked per quality lot; an appointment only reserves units at or above the
  stricter of your minimum and the customer's requirement, and the removal is asked for at least the reserved
  lot's tier. Which slots the game's removal takes first among qualifying items is PROBABLE.
- Dealer sales can be switched off separately and dealers can have their own rules (Phase 05,
  [ADVANCED_RULES.md](ADVANCED_RULES.md)).
- That `requiredQuantity` is in units, and that the handover routine calls `Customer.ProcessHandover`, are
  PROBABLE (call graph); the log line `removed N unit(s)` and scenarios DS01-DS03 in [PHASE_04_TESTING.md](PHASE_04_TESTING.md) confirm them.
- Dealer restock texts: [PHASE_04_ENHANCEMENTS.md §4](PHASE_04_ENHANCEMENTS.md#4-dealer-restock-messages).
- If the game is saved in the few seconds between the removal and the customer's evaluation, the appointment
  is restored as waiting.
