# Phase 04 patch analysis: hired dealer sales

> Source: the Il2CppInterop xref scan cache of the installed IL2CPP build (the same S1Refs export as Phase 03),
> read with `tools/TakeItOrLeaveIt.Inspector --xref`. The scan lists which native methods each method calls
> and is called by. It proves that a method **exists** with that exact signature and that a **call edge**
> exists. It does not show branches, loops or argument values, and identical-code folding adds unrelated
> callees (filtered out below). Calls through virtual dispatch, delegates or inlining can be missing.

Evidence levels used below:

| Level | Meaning |
| --- | --- |
| VERIFIED | Exact signature present in the interop assembly and the call edge is in the xref scan |
| SOURCE-SUPPORTED | Follows from verified edges plus the method's name/parameters, no contradicting edge |
| PROBABLE | Consistent with the evidence but depends on internal logic the scan cannot show |
| UNVERIFIED | Not found or not checked; nothing in Phase 04 depends on it |

## 1. Dealer contract flow (installed build)

```text
Customer.TryGenerateContract(Dealer)                       offer built from GetWeightedRandomProduct(dealer, ...)
  └─ Customer.GetOrderableProductsWithQuantities(Dealer) → Dealer.GetOrderableProducts(EQuality)
Dealer.ShouldAcceptContract(ContractInfo, Customer)        → Dealer.GetOrderableProductQuantity(productID, min, max)
Dealer.ContractedOffered(ContractInfo, Customer)
  ├─ Customer.ContractAccepted(EDealWindow, bool, Dealer)
  │    └─ QuestManager.ContractAccepted(Customer, ContractInfo, bool, string, Dealer)   ◀ P03 postfix (now also dealers)
  └─ Dealer.AddContract(Contract) → Contract.SetDealer(Dealer)
Dealer.OnTick()                                            ◀ D2 postfix (reconcile, observe only)
  └─ Dealer.CheckAttendStart() → DealerAttendDealBehaviour.AssignContract(Contract), Behaviour.Enable_Server()
DealerAttendDealBehaviour.BeginHandover() → StartCoroutine(handover routine)
  handover routine .MoveNext()
    ├─ InstanceFinder.IsServer
    ├─ StandardsMethod.GetCorrespondingQuality(ECustomerStandard)        target quality from the customer
    ├─ Dealer.RemoveContractItems(Contract, EQuality, ref List<ItemInstance>)   ◀ D1 prefix (category fulfilment)
    │    ├─ Dealer.GetPackagedProductAmount()
    │    ├─ Dealer.RemoveAndReturnProductFromInventory(string productID, int requiredQuantity, EQuality)
    │    │    ├─ Dealer.GetAllSlots()
    │    │    ├─ Dealer.FilterAndSortSlots(slots, productID, quality, EAmountSortOrder)   ← exact product id compare
    │    │    │    └─ BaseItemInstance.get_ID(), ProductItemInstance.get_AppliedPackaging()
    │    │    └─ Dealer.<obfuscated>(List<ItemSlot>, bool split, bool onlyRemoveIdealQuality, ref ...)
    │    │         └─ ItemSlot.ChangeQuantity, Dealer.SplitItemSlot(ItemSlot)
    │    ├─ Dealer.TryMoveOverflowItems()
    │    └─ DialogueDatabase.GetChain → MSGConversation.SendMessageChain     (dealer text message)
    ├─ Dealer.AddItemToInventory(ItemInstance)                           (items handed back)
    └─ NPCBehaviour.DisableBehaviour_Server(int)
Customer.ProcessHandover(Contract, List<ItemInstance>, bool handoverByPlayer, bool giveBonuses)   ◀ P03 postfix
  └─ Customer.RpcLogic___ProcessHandoverServerSide(items, handoverByPlayer, totalPayment, productList, satisfaction, dealerObject)
       ├─ Dealer.SubmitPayment(float) → RpcLogic___SubmitPayment → RpcWriter___Server_SetCash   (dealer cash)
       ├─ DailySummary.AddDealerMoney(float), DailySummary.AddSoldItem(string, int)
       ├─ LevelManager.AddXP(int), MoneyManager.ChangeLifetimeEarnings(float)
       └─ ProductManager.RecordContractReceipt(...)
Contract.Complete / End / Expire / Fail                    ◀ P03 postfixes (now also dealers)
Dealer.CustomerContractEnded(Contract) → Contract.SetDealer(null)
```

## 2. Answers to the investigation questions

| Question | Answer | Level |
| --- | --- | --- |
| Where does the dealer choose products/stacks? | `RemoveAndReturnProductFromInventory` takes `GetAllSlots()`, filters and sorts them with `FilterAndSortSlots(slots, productID, quality, sortOrder)`, then drains them with the obfuscated helper (split / ideal-quality flags) | VERIFIED (edges); order of draining PROBABLE |
| Where are exact product ids compared? | `FilterAndSortSlots` (calls `BaseItemInstance.get_ID` and `get_AppliedPackaging`); `GetOrderableProductQuantity(productID, ...)` for acceptance | VERIFIED (edges) |
| Where is dealer inventory read? | `GetAllSlots`, `GetAvailableProducts`, `GetOrderableProducts`, `GetOrderableProductQuantity`, `GetPackagedProductAmount`, `GetTotalInventoryItemCount` | VERIFIED |
| Where is it removed? | Only `RemoveAndReturnProductFromInventory` (`ItemSlot.ChangeQuantity`, `SplitItemSlot`), called only from `RemoveContractItems`, called only from the handover routine | VERIFIED (single caller each) |
| Where is it transferred/returned? | The removed items go into the `ref List<ItemInstance>` and to the customer; `AddItemToInventory` is called by the routine (items handed back) | VERIFIED (edges); when it returns items PROBABLE |
| Is anything reserved by vanilla? | No reservation method exists; `ShouldAcceptContract` only reads `GetOrderableProductQuantity` (no edge to other contracts) | SOURCE-SUPPORTED |
| How is the appointment started? | `OnTick → CheckAttendStart → DealerAttendDealBehaviour.AssignContract + Enable_Server` (time window, `GetMinsUntilExpiry`) | VERIFIED |
| How is completion triggered? | The handover routine; payment/XP/cash in `ProcessHandoverServerSide → Dealer.SubmitPayment`; the contract then ends through `Contract.Complete/End/Expire/Fail` | VERIFIED (edges); that the routine calls `Customer.ProcessHandover` is PROBABLE (no edge listed; the scan misses calls through some call sites) |
| Who pays the dealer's cut? | `ProcessHandoverServerSide` (`get_DealerData`, `SubmitPayment`, `AddDealerMoney`) | VERIFIED |
| Is it server-side? | The routine checks `InstanceFinder.IsServer`; `SubmitPayment` is a server RPC | VERIFIED |
| Quantities | `ProductList.Entry.Quantity` and `requiredQuantity` are units (contract totals use `ProductList.GetTotalQuantity`; removal splits packages with `SplitItemSlot`) | PROBABLE |

## 3. Chosen hooks

### New in Phase 04

| ID | Target (exact signature) | Kind | Purpose | Level |
| --- | --- | --- | --- | --- |
| D1 | `void Dealer.RemoveContractItems(Contract contract, EQuality targetQuality, ref List<ItemInstance> items)` | prefix | For a managed dealer order only: plan the delivery from the dealer's eligible stock (reservation ledger), then remove it with the game's own `Dealer.RemoveAndReturnProductFromInventory(productID, units, targetQuality)` once per planned product, add the returned items to `items`, call `TryMoveOverflowItems()`, and skip the original. Any other contract, or any failure before removal starts, runs the original unchanged | Existence and single caller VERIFIED; replacing its per-line loop is PROBABLE |
| D2 | `void Dealer.OnTick()` | postfix | Observe only: for dealers with managed appointments, read the inventory and reconcile reservations (invalidate lost stock, retry waiting orders). Throttled per dealer | VERIFIED |

D1 is the only place a dealer removes contract stock, so the substitution happens at the exact moment vanilla
would draw the requested product, with vanilla's own removal method doing the inventory work (packaging
splits, slot updates, networking). The mod chooses **which product id and how many units**, nothing else.

### Reused from Phase 03 (now also for dealer orders)

| Target | Dealer use |
| --- | --- |
| `QuestManager.ContractAccepted(...)` postfix | `dealer != null`: take the category snapshot and try to reserve stock for that dealer |
| `Contract.GetProductListMatch` prefixes | The customer judges the dealer's delivered items against the snapshot (no recipe-mismatch penalty, quality still counts) |
| `Customer.ProcessHandover` postfix | Observe the vanilla settlement once |
| `Contract.Complete/End/Expire/Fail` postfixes | Release the reservation and drop the appointment |

### Deliberately not patched

| Method | Why |
| --- | --- |
| `Dealer.ShouldAcceptContract` | No caller in the scan (called through a path the scan cannot see); acceptance stays vanilla. Offers are generated from the dealer's own stock (`GetWeightedRandomProduct(dealer)`), so vanilla already only offers what the dealer has |
| `FilterAndSortSlots`, the obfuscated drain helper, `SplitItemSlot` | Guessing their internal contract is not needed: D1 calls the public removal method with a product id the dealer really holds |
| `SubmitPayment`, `ChangeCash`, `AddDealerMoney`, XP, `CompletedDeal` | Economy stays vanilla; the mod never calls or changes them |
| `CheckAttendStart`, `DealerAttendDealBehaviour.*` | Scheduling stays vanilla; a waiting order is handled at handover (§4) |
| `Customer.TryGenerateContract`, offer generation | Unchanged, as in Phase 03 |

## 4. Behaviour at the hooks

1. **Acceptance** (`QuestManager.ContractAccepted`, dealer set): Core `DealerSaleCoordinator.OnContractAccepted`
   builds the same `CategoryOrder` snapshot as player sales (`CategoryOrderFactory.CreateForDealer`, shared
   code), then asks the `ReservationLedger` to reserve the units from the dealer's current eligible stock minus
   what that dealer's other appointments already hold. Result: **Reserved** or **WaitingForEligibleStock**.
2. **Reconcile** (D2, at most every few seconds per dealer, only when the dealer's stock changed): reservations
   whose product disappeared are **Invalidated** (newest first) and go back to waiting; waiting orders are
   retried in acceptance order (bounded per call). After too many invalidations an order returns to vanilla.
3. **Handover** (D1): fresh inventory read → reconcile → ensure a reservation → **Committing** → vanilla
   removal per planned product → reservation **CommitObserved** (stock left the inventory, hold released).
   If no eligible stock can be reserved, the order **returns to vanilla** before anything is removed and the
   original method runs (the dealer then behaves exactly as vanilla would). A second call for the same
   contract removes nothing.
4. **Settlement**: the customer judges the items with the Phase 03 match; payment, the dealer's cut, XP and
   relationship are vanilla's (`ProcessHandoverServerSide`). `Customer.ProcessHandover` is recorded once.
5. **End**: `Complete/End/Expire/Fail` release any hold and drop the appointment.

## 5. Authority

Same gate as Phase 03: `InstanceFinder.IsServer && Player.PlayerList.Count <= 1`. Clients and multiplayer
hosts leave every dealer contract vanilla (Phase 07).

## 6. Risks and how they are contained

| Risk | Containment |
| --- | --- |
| `RemoveContractItems` does more than its edges show (e.g. the dealer's "not enough product" message) | Only managed orders skip it; the message is only needed when stock is short, and a short order returns to vanilla first |
| `requiredQuantity` is not in units | The removed items are counted after removal; a shortfall is logged and the customer's validation refuses an incomplete delivery (score 0, vanilla failure path) |
| Removal returns fewer units than planned | Nothing is removed twice; the delivery is judged as is; the error appears in diagnostics |
| The handover routine does not call `Customer.ProcessHandover` for dealers | Settlement is still observed through `Contract.Complete`; reservations are released at removal, not at settlement |
| Signature changes in a game update | Every target is resolved by exact signature; a missing one makes `PatchManager` remove all patches (vanilla game) |

## 7. Read-only diagnostic

No extra diagnostic build is needed: the decisions above rely on verified signatures and single-caller
edges, and the behaviour marked PROBABLE is checked by the physical tests in
[PHASE_04_TESTING.md](PHASE_04_TESTING.md) (the log prints every planned and removed quantity).
