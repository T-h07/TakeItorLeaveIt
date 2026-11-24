# Player sales (Phase 03): "Take what I give you"

> **Status:** implemented and covered by automated tests; **not yet observed in game** (see
> [PHASE_03_TESTING.md](PHASE_03_TESTING.md)). The hooks are chosen from the installed build's call graph
> ([PHASE_03_PATCH_ANALYSIS.md](PHASE_03_PATCH_ANALYSIS.md)); the behaviour marked PROBABLE there is what the
> in-game tests confirm.

When a customer's contract is accepted while the mod is on, the order becomes **"N units of a category"**
instead of "N units of one exact recipe". At the handover you can give any product the Take It settings
permit for that category, in any mix (unless you turn mixing off). Everything else about the sale (price,
payment, XP, relationship, deadline) stays exactly as the game does it.

## 1. How an order becomes a category order

The game still creates its normal contract (customer, product list, payment, location, window, expiry); the
mod does not rewrite it, because the contract is saved by the game and sent over the network. Instead, when
the contract is accepted (`QuestManager.ContractAccepted`), the mod decides whether it is a **category
order** and, if so, stores a **snapshot** next to it:

| Condition | Result |
| --- | --- |
| The contract is handled by a dealer | Not a player order: handled by dealer sales ([DEALER_SALES.md](DEALER_SALES.md)) |
| Mod off for this save, or settings not loaded / unreadable | Vanilla |
| Product catalog not ready, unknown product, invalid quantity | Vanilla |
| The order lists products from more than one category | Vanilla |
| The category is *Disabled*, or *Selected Only* with nothing selected | Vanilla (never an impossible order) |
| Multiplayer session or not the host | Vanilla |
| Otherwise | **Category order** |

The snapshot holds: contract GUID, category, total units (sum of the order lines), the products permitted
for that category at that moment (sorted IDs), the mixed-deliveries setting, and the policy revision. It never
holds game objects.

Customers pick offers the vanilla way, but since the Phase 04 enhancements only from products the live settings
permit (the candidate list is filtered before the game chooses and prices; no retry loop, no offer the game
would not make by itself). At acceptance the mod also reads the game's unit prices: a substitute must be worth
at least the quoted unit price, so the fixed payment never exceeds what is delivered. Details:
[PHASE_04_ENHANCEMENTS.md §1-§2](PHASE_04_ENHANCEMENTS.md#1-customer-offers-follow-the-settings).

## 2. Handover validation

When you meet the customer, the handover screen describes the real requirement:

```text
Deliver exactly 5 Weed, any of: Granddaddy Purple, OG Kush, Sour Diesel. Mixing products is allowed.
```

Every time the items in the handover slots change, the mod validates them against the snapshot, **before
anything is transferred**. The checks, in order (the first failure is shown in red and the Done button stays
disabled):

| Check | Message |
| --- | --- |
| Something is in the slots | `This customer wants 5 Weed.` |
| Each item is a readable product stack with positive units | `One of the items cannot be sold.` |
| Each product is packaged | `Package the product first.` |
| The product is known to the catalog | `This customer does not recognise one of the products.` |
| Same category as the order | `This customer only wants Weed.` |
| Permitted by the snapshot | `One of the products is not allowed for Weed orders (Take It settings).` |
| Unlocked | `One of the products is not unlocked.` |
| One product only, if mixing is off | `Mixed deliveries are off: use one product only.` |
| Exactly the ordered units | `Needs 5 Weed: you offered 3.` / `Needs exactly 5 Weed: you offered 6.` |

Units are `stack quantity x package amount` (a jar counts its contents). The category, permission and
unlock checks are the Phase 01 `SaleEligibilityPolicy` run through `InventoryAllocationPlanner` with the
snapshot as the policy, so the menu preview and the sale can never disagree.

**Exact quantity.** Phase 03 has no partial fulfilment and the game has no verified way to take part of a
stack, so more units than ordered are refused rather than handed over for free.

## 3. Mixed deliveries

Setting **Mixed deliveries** (phone app → Settings → Customer orders; saved per save as `mixedDeliveries`,
default **ON**):

- ON: any combination of permitted products of the category, e.g. 3 Granddaddy Purple + 2 OG Kush for 5 Weed.
- OFF: one product for the whole order (several stacks of that one product are fine).

The value is captured in the snapshot at acceptance, like the permitted products.

## 4. The transaction: vanilla stays in charge

```text
Pending ──(items change)──▶ Validated / Rejected ──(game processes the handover)──▶ CommitObserved ──▶ Completed
   └───────────────────────────────── any time ─────────────────────────────────▶ Failed / Canceled / Expired
```

1. The mod answers one question the game asks: **how well do these items match the contract?**
   (`Contract.GetProductListMatch`). For a category order with a valid delivery it answers with the quality
   match only (see §5) and the full unit count; for an invalid one it answers 0. Every other contract gets
   the game's own answer.
2. The game then does the whole sale itself: success chance, satisfaction (`Customer.EvaluateDelivery`), item
   transfer (`HandoverScreen.TransferCustomerItemsToNPC`), payment, XP and statistics
   (`ProcessHandoverServerSide`), relationship change and contract completion.
3. The mod only **observes** `Customer.ProcessHandover` (recorded once; repeats are logged and ignored) and
   the contract's end (`Complete` / `End` / `Expire` / `Fail`), and then drops the snapshot.

So there is no mod payment, no mod XP, no mod item removal, and nothing to roll back. A rejected delivery
never reaches the game's transaction: items stay in your inventory and no reward or completion happens. A
second Submit cannot pay twice because the game ends the contract on the first one; a repeated callback only
logs a warning.

**Inventory:** the items in the handover slots are exactly the items the game transfers, and the mod only
approves when they add up to the ordered units, so consumed = delivered = ordered.

## 5. Satisfaction: only the recipe penalty is removed

The game rates each delivered item with `ProductItemInstance.GetSimilarity(requested product, requested
quality)`. For a category order the mod rates each item against **its own product** at the requested
quality, so the recipe/effect difference drops out and **quality still counts**. Lateness, quality, addiction,
relationship rules and everything else in the game's satisfaction stay as they are. Nothing on the shared
customer data is changed. (That the similarity splits into a recipe and a quality term is PROBABLE; the
in-game test P03-T23 checks it.)

## 6. Changing settings while orders are active

- Active orders keep their snapshot: unticking OG Kush after accepting an order still lets that order take
  OG Kush; new orders use the new rules.
- Turning the whole mod or a category off affects **new** orders only. Existing category orders stay
  completable with their snapshot, so nothing is stranded.
- A product unlocked after acceptance is not added to an existing order.

## 7. Save, reload and save switching

Snapshots are written to `UserData/TakeItOrLeaveIt/saves/<save id>.orders.json` (next to the menu settings,
never in the game's save folder), atomically, off the main thread, whenever they change. They are loaded when
the save is loaded; 20 seconds later, and every minute after that, snapshots whose contract no longer exists
are removed. Each save has its own file, so orders never leak between saves.

If the file is missing, unreadable, from a newer version, or an entry is invalid, those orders fall back to
the game's own exact-product rules (logged). The game save itself is never modified. If a snapshot could not
be written before the game closed, that order also falls back to vanilla after the restart.

## 8. Runtime and session support

| Environment | Player sales |
| --- | --- |
| IL2CPP (default Steam branch), mod built with local game references | Active |
| IL2CPP build from CI (no game references) | Unavailable: the status report says so, sales are vanilla |
| Mono (alternate branch) | Unavailable: no Mono game references are exported, so there are no hooks |
| Single player | Active |
| Multiplayer (host with other players, or a client) | Inactive: vanilla rules. Customer handovers run through FishNet server RPCs; host-authority handling is Phase 07 |

## 9. Status report

At startup and in the phone app (Diagnostics → Player sales):

```text
Player Sales: Enabled
Runtime: Il2Cpp
Product catalog: Ready
Policy: Loaded
Offer hook: Active
Handover validation: Active
Transaction integration: Active
Session: Single player host.
Active category orders: 1
```

"Enabled" appears only when every hook is applied, the catalog and settings are ready, the mod is on for the
save and the session is supported. If a game method cannot be patched, every mod patch is removed, the
report says **Unavailable** with the reason, and the game runs vanilla.

## 10. Known limitations

- The customer's phone message and the journal entry name the product the game picked (always a permitted one
  while the mod is on); the handover screen shows the real requirement (category, units, permitted products).
- Substitutes cheaper than the quoted unit price are refused (fair-price rule); a product with no readable
  price allows only the requested product(s).
- More units than ordered are refused (no verified partial-stack hand-in).
- The match/similarity split, the units formula and whether the game scales payment with the match are
  PROBABLE (call graph), confirmed only by the in-game tests.
- `HandoverScreenContractMode.GetError` returning `true` for "there is an error" is PROBABLE; if it were the
  opposite, the match hook alone still blocks invalid deliveries (they score 0).
- Single player only; dealers: see [DEALER_SALES.md](DEALER_SALES.md). Quality, effect, lineage and mixing rules
  (Phase 05): [ADVANCED_RULES.md](ADVANCED_RULES.md); handover items below the quality minimum are refused before
  transfer, and the customer's own quality judgement is unchanged.
