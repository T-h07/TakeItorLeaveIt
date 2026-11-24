# Phase 04 enhancements: policy-based offers, fair pricing, strict dealers, restock messages

> **Status: implemented, automated tests pass, NOT TESTED IN GAME.** Everything below marked PROBABLE is what
> the physical scenarios in [PHASE_04_TESTING.md §8](PHASE_04_TESTING.md#8-enhancement-scenarios) confirm.
> Hook evidence: [PHASE_04_PATCH_ANALYSIS.md §8-§11](PHASE_04_PATCH_ANALYSIS.md#8-enhancements-overview).

These four changes close the gaps found after the first Phase 04 build:

| Gap | Before | Now |
| --- | --- | --- |
| Customers asked for products the settings exclude | Offers were vanilla; only the delivery was restricted | Excluded products are removed from the offer pool **before** the game picks and prices an offer |
| Quote for Sour Diesel, cheaper OG Kush delivered | Any permitted substitute was accepted for the same fixed payment | A substitute must be worth **at least the quoted unit price** |
| Dealer short of permitted stock sold the excluded requested strain | Managed appointment fell back to vanilla at handover | **Strict:** nothing is handed over, the appointment keeps waiting |
| Player never knew a dealer was short | Only the log and the phone diagnostics | The dealer **texts you** through the game's own phone conversation |

## 1. Customer offers follow the settings

The game builds every customer offer from one candidate list, `Customer.GetOrderableProductsWithQuantities(dealer)`
(the player's listed products when the dealer is null, the dealer's stock otherwise). The mod filters that list
right after the game builds it, using the **same rule as deliveries** (`SaleEligibilityPolicy`, live settings,
the product's own category):

| Category mode | Offers |
| --- | --- |
| **Any Unlocked** | Any unlocked product of the category |
| **Selected Only** | Only the ticked products that are unlocked; an empty selection means no offers for the category |
| **Disabled** | No new offers for the category at all |
| Mod **off** for the save | Vanilla (the list is not touched) |

Locked products and products the catalog does not know are never offered while the mod is on. The game then
picks a product from what is left, prices it, writes the message and the journal entry, all vanilla, so the
product, quote, message and journal always agree. When nothing is left the game makes no offer this time,
which is the same thing it does when you have nothing listed (PROBABLE, see the patch analysis §9).

The filter only applies on a single-player host with the save's settings loaded and the product catalog
ready; otherwise offers are vanilla. It never changes customer data, product definitions or prices: it only
removes entries from the list the game just created for this one offer.

## 2. Fair pricing (fixed-quote rule)

The game fixes the payment when it makes the offer, from the chosen product's price, and pays that amount at
completion whatever is delivered. So the mod bounds substitutes instead of repricing:

- At acceptance it reads the game's unit price of each product (`ProductDefinition.Price`, the same getter the
  offer uses; normally your listing price) and records **quote = Σ requested quantity × price**.
- A product is a fair substitute only if **its unit price ≥ quote ÷ units**. Ties are allowed.
- Every basket made only of fair products is worth at least the quote, so **mixed deliveries can never be
  underpaid**: 3 Sour Diesel + 2 GDP for a 5 Sour Diesel quote is fine, any OG Kush (cheaper) is not.
- The snapshot stores the prices and the quote, so later price changes never change an accepted order.

| Situation | Result |
| --- | --- |
| Sour Diesel (45) requested, permitted OG Kush (35), GDP (50) | GDP or Sour Diesel only, mixed if allowed |
| OG Kush (35) requested | Any permitted Weed (all ≥ 35) |
| A requested product has no readable price | **No substitution**: requested product(s) only |
| A substitute has no readable price | That substitute is excluded |
| No permitted product is worth the quote | The order is not managed (`NoFairProduct`); see §3 for dealers |

The mod never sets or changes a price, a payment, the dealer's cut or XP. Example prices above are test data.

Rejection text on the handover screen for a cheaper product: *One of the products is not allowed for Weed
orders (Take It settings).* (it is not in the order's allowed list). A damaged snapshot whose basket is worth
less than the quote shows *These products are worth less than the agreed price.*

## 3. Strict dealer fulfilment

A **managed** dealer appointment never falls back to the game's exact-product removal any more:

| At the handover | Before | Now |
| --- | --- | --- |
| Enough eligible, fair stock | Removed by the game's removal method, as reserved | Unchanged |
| Not enough | Returned to vanilla: the dealer sold the requested product, even if excluded | **Withheld**: nothing removed, nothing handed over, the appointment stays *Waiting for eligible stock* and the dealer texts you (§4) |
| Removal returned a different number of units | Logged, the customer judged what was removed | **Rolled back**: every removed item goes back to the dealer, nothing handed over, the appointment waits again |
| Removal threw before removing anything | Returned to vanilla | Withheld, the appointment waits again |
| Contract moved to another dealer | Returned to vanilla | The appointment **follows the new dealer** (old hold released, replanned with the new dealer's stock) |
| Reserved stock lost more than 5 times | Returned to vanilla | Still managed; reported once in diagnostics |

When the handover is withheld the customer receives nothing, and the game's own short-delivery handling
applies (the same path as a vanilla dealer without enough stock): the deal fails or the contract expires at
its deadline, through the game's `Contract.Fail/Expire`; the mod then releases everything. No fake completion,
no payment, no XP, no stuck appointment. What exactly the customer does with an empty delivery is PROBABLE
and is the first thing the in-game scenarios check.

**Unmanaged dealer contracts** (mod off, not the host, or accepted before this save's settings were loaded)
stay vanilla, with one guard while the mod is on and ready: a contract that asks for a product the live
settings exclude is withheld too, so an excluded product is never sold through the vanilla path.

Player contracts are not changed by strict mode: you choose what to hand over, and the handover screen
refuses anything the order does not allow.

## 4. Dealer restock messages

Hired dealers text you in the game's phone (their own conversation, with the game's normal notification).
No Windows notification, no separate panel.

| Type | When | Text |
| --- | --- | --- |
| A, out of stock | A category the dealer **stocked before in this save** (or has an appointment for) drops to 0 permitted units | `Yo boss, I'm out of Weed I can sell. Need you to restock me.` |
| B, appointment shortage | An appointment is waiting and the dealer cannot cover it | `Boss, I've got a customer waiting for 5 Weed, but only 2 permitted units are available. Need 3 more.` |

- *Permitted units* use the live settings (Type A) or the appointment's snapshot including the fair-price rule
  (Type B). Available for an appointment = eligible stock − units reserved for the dealer's other
  appointments; with Mixed deliveries **off** only the best single product counts.
- A Disabled category, or one with no eligible product, is never reported. Products in other categories never
  hide an empty one.
- A waiting appointment's message replaces the out-of-stock text for the same category.

**Deduplication.** Identity = save + dealer + category + type (+ the appointment for Type B).

- Type A is sent once and re-armed only by a real restock (permitted units above 0 again).
- Type B is sent when the shortfall first appears and again only if it grows. When the appointment gets its
  stock the episode ends; a later shortage is new.
- No messages for 20 seconds after a save loads, and a category the dealer never stocked is never reported,
  so loading a save does not produce a burst of texts.
- Checks run from each hired dealer's tick, at most every 5 seconds per dealer.

**Delivery.** A message is marked delivered only after the game accepted it. A failed send is tried at most 3
times (retries after 30 s and then 60 s), then given up until the stock changes. If the game's messaging methods are missing the
messages are off (logged once) and sales are unaffected.

**Persistence.** `UserData/TakeItOrLeaveIt/saves/<save id>.dealer-alerts.json` holds only the dedup state
(per dealer and category: stocked before / out-of-stock sent; per waiting appointment: last shortfall sent).
It never goes into the game's save. A missing or damaged file means "no history" (with a log line).

## 5. Compatibility with older data

| Data | Handling |
| --- | --- |
| Order or appointment snapshots saved by the first Phase 04 build (no prices) | Loaded through a **legacy path**: allowed products narrowed to the requested products they already allowed (no cheaper substitute). If none of the requested products was allowed, the snapshot is kept as it was and logged as a legacy snapshot |
| Snapshot with unusable price data | Requested products only; dropped (logged) when none is allowed |
| Order files written now, read by the first Phase 04 build | Extra fields are ignored; schema stays 1 |
| No `dealer-alerts.json` | Fresh history; nothing is sent for categories the dealer has not stocked |

## 6. Diagnostics

Startup log and Phone → Take It → Diagnostics → *Player and dealer sales* add:

```text
Customer offer filter: Active
Offer candidates removed: 12 of 40 (1 pool(s) left empty)
Fair pricing: Active (substitutes must be worth at least the quoted unit price)
Strict dealer fulfilment: On (managed appointments never fall back to the requested product)
Dealer restock messages: Active
Dealer messages sent: 2, failed: 0
```

Log lines to look for:

```text
Category order created: <guid>: 5 x marijuana (any of 2 product(s), mixed on, policy r3, quote 225)
Dealer order <guid>: not enough eligible stock at handover (2/5 eligible unreserved unit(s).); nothing handed over, still waiting.
Dealer order <guid>: nothing handed over (...)
Dealer message sent: AppointmentShortage benji_coleman/marijuana <guid>: "Boss, I've got a customer waiting ..."
Dealer contract <guid>: requested product sourdiesel is not permitted (ProductNotSelected); the dealer hands over nothing ...
```

## 7. Limitations

- Single player only, like the rest of Phase 04.
- The quote is checked against prices read at acceptance; if you change a listing price between the offer and
  accepting it, the comparison uses the newer prices for every product (still a fair relative comparison).
- Multi-product vanilla offers (if the game ever makes them) use the average unit price; a requested product
  cheaper than that average is then not accepted as part of the delivery.
- What the customer does with a withheld (empty) dealer delivery is the game's behaviour and is PROBABLE until
  the in-game scenarios run.
- Restock messages are decisions on dealer inventory snapshots every few seconds; a stock change and a
  restock within the same 5 seconds can be missed (no message, never a wrong one).

## 8. Runtime verification

Run [PHASE_04_TESTING.md §8](PHASE_04_TESTING.md#8-enhancement-scenarios) (OF, PR, MSG, ST, RG). Automated tests prove the
model only (suites O, P, M, S, **simulated** game); nothing here is verified in game until those scenarios pass.
