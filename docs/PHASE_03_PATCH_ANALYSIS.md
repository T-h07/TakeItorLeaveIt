# Phase 03 patch analysis: customer sales lifecycle

> Build analysed: **Schedule I v0.4.7f12, IL2CPP** (Unity 2022.3.62f2), the build installed on the gaming PC.
> Source of truth: the Il2CppInterop assemblies MelonLoader generated for that build, including
> `MethodXrefScanCache.db` (call graph) and `MethodAddressToToken.db` (method identities), exported with
> `tools/Export-BuildReferences.ps1`. No game code was decompiled or copied; only method names, signatures
> and caller/callee edges were read.

## 1. How the evidence was collected

`tools/TakeItOrLeaveIt.Inspector` has a read-only `--xref` mode:

```powershell
dotnet tools\TakeItOrLeaveIt.Inspector\bin\Release\net8.0\TakeItOrLeaveIt.Inspector.dll `
  --xref "<S1RefsDir>\MelonLoader\Il2CppAssemblies" `
  --types "Il2CppScheduleOne.Economy.Customer;Il2CppScheduleOne.Quests.Contract;Il2CppScheduleOne.UI.Handover.HandoverScreenContractMode" `
  --out xref.md
```

For every method of the listed types it prints the native callees and callers recorded by Il2CppInterop's
xref scanner (`[CachedScanResults]` ranges into `MethodXrefScanCache.db`, resolved to names through
`MethodAddressToToken.db`). Limits of this evidence:

- It proves that a method **exists** with that exact signature in this build and that one native method
  **calls** another. It says nothing about conditions, order or arithmetic inside a method.
- Identical-code folding in IL2CPP makes some edges to trivial methods point at unrelated names (for
  example `Customer.ProcessHandover` "calling" a `RequestProductBehaviour` enumerator `Dispose`). Such
  edges are ignored here.
- Methods marked "no scan data" (for example `Customer.HandoverSubmitted`) have no recorded edges; their
  role is inferred from their callers' delegates.

### Evidence levels

| Level | Meaning |
| --- | --- |
| **VERIFIED** | The member and the stated call edge are in the installed build's xref cache / interop assemblies. |
| **SOURCE-SUPPORTED** | Named in S1API 3.2.3 compiled code (see [PATCH_MAP.md](PATCH_MAP.md) E1/E2) and present in the interop assemblies, but the behaviour described comes from names and call edges, not from observing it. |
| **PROBABLE** | Behaviour inferred from names, signatures and call edges; consistent with everything seen, not yet observed at runtime. |
| **UNVERIFIED** | No evidence beyond a guess. Not used for any patch. |

## 2. Call flow (installed build)

```text
Offer (vanilla, unchanged)
  Customer.TryGenerateContract(Dealer) -> GetWeightedRandomProduct(...) -> ContractInfo
  Customer.OfferContract(ContractInfo) -> phone message (ContractInfo.ProcessMessage)

Acceptance
  Customer.RpcLogic___SendContractAccepted / Dealer.ContractedOffered
    -> Customer.ContractAccepted(EDealWindow, bool trackContract, Dealer dealer)        [server]
         -> GUIDManager.GenerateUniqueGUID
         -> QuestManager.ContractAccepted(Customer, ContractInfo, bool track, string guid, Dealer) : Contract
                                                                   ^ Phase 03 postfix: snapshot

Handover UI (player meets the customer)
  Customer.CompleteContractDialogueChosen()
    -> HandoverScreen.Open_Contract(Contract, onSubmit = Customer.HandoverSubmitted, onCancel, requireFullChanceOfSuccess)
         -> HandoverScreenContractMode.Open(...) -> Refresh()
  HandoverScreenContractMode.Refresh / GetSuccessChance / GetError / GetWarning
    -> HandoverScreen.GetHandoverProducts(bool filterForPackagedOnly)
    -> Contract.GetProductListMatch(List<ProductItemInstance>, ref int matched)          ^ Phase 03 prefix
         -> Contract.GetProductListMatch(List<ItemInstance>, ref int matched)            ^ Phase 03 prefix
              -> Contract.GetDescendingMatchRatings(ProductList.Entry, List<ItemInstance>)
                   -> Registry.GetItem(string ID)
                   -> ProductItemInstance.GetSimilarity(ProductDefinition other, EQuality otherQuality)
  HandoverScreenContractMode.GetError(ref string)                                       ^ Phase 03 postfix (gate)
  HandoverScreenContractMode.Submit() -> HandoverScreen.GetHandoverItems() -> onSubmit(items)

Evaluation and transaction (vanilla, unchanged)
  Customer.HandoverSubmitted(List<ItemInstance>)                                         (no scan data)
  Customer.EvaluateDelivery(Contract, List<ItemInstance>, ref highestAddiction, ref EDrugType,
                            ref int matchedProductCount, ref float qualityDifference) : satisfaction
    -> Contract.GetProductListMatch(List<ItemInstance>, ref int)                         (same prefix)
    -> ProductItemInstance.get_AppliedPackaging, ProductDefinition.GetAddictiveness
  Customer.ProcessHandover(Contract, List<ItemInstance>, bool handoverByPlayer, bool giveBonuses)
    -> HandoverScreen.TransferCustomerItemsToNPC(NPC)  (items move to the customer's inventory)
    -> CustomerSatisfaction.GetRelationshipChange(float), Contract.BonusPayment(...)
    -> DealCompletionPopup.PlayPopup(customer, satisfaction, relationshipDelta, basePayment, bonuses)
    -> server RPC ProcessHandoverServerSide(items, handoverByPlayer, totalPayment, productList, satisfaction, dealer)
  Customer.RpcLogic___ProcessHandoverServerSide_3315874220(...)                          [server]
    -> LevelManager.AddXP(int), DailySummary.AddPlayerMoney / AddSoldItem, MoneyManager.ChangeLifetimeEarnings,
       ProductManager.RecordContractReceipt, NPCInventory.RemoveCash, Dealer.SubmitPayment (dealer deals)
  Contract.SubmitPayment(float bonusTotal) -> MoneyManager.ChangeCashBalance(float, bool, bool)

Completion (observed for cleanup only)
  Contract.Complete(bool network) -> Quest.Complete(bool)                                ^ Phase 03 postfix
  Contract.End() -> Quest.End()                                                          ^ Phase 03 postfix
  Contract.Expire(bool network), Contract.Fail(bool network) -> QuestManager.SendQuestAction ^ Phase 03 postfix
```

## 3. Hook table

| ID | Member (installed signature) | Role | Evidence | Phase 03 use | Risk |
| --- | --- | --- | --- | --- | --- |
| P3-01 | `Customer.ContractAccepted(EDealWindow, bool, Dealer) : Contract` | Accepts an offered contract on the server; creates GUID | **VERIFIED** (edges to `GenerateUniqueGUID`, `QuestManager.ContractAccepted`; callers `SendContractAccepted` RPC and `Dealer.ContractedOffered`) | Not patched (the QuestManager call below carries the contract) | — |
| P3-02 | `QuestManager.ContractAccepted(Customer, ContractInfo, bool, string guid, Dealer) : Contract` | Creates the `Contract` quest | **VERIFIED** (signature, caller P3-01) | **Postfix**: take the acceptance-time policy snapshot for player contracts (`dealer == null`) | Low: read-only, runs once per acceptance |
| P3-03 | `Contract.GetProductListMatch(List<ProductItemInstance>, ref int) : float` | UI wrapper; converts the list and calls P3-04 | **VERIFIED** (callers `Refresh`, `GetSuccessChance`, `GetError`, `GetWarning`; callee P3-04) | **Prefix** for mod-managed contracts only (same logic as P3-04) | Medium |
| P3-04 | `Contract.GetProductListMatch(List<ItemInstance>, ref int matchedProductCount) : float` | The exact-recipe match: per requested entry, rates provided items with `GetSimilarity(definition, quality)` | **VERIFIED** (edges to `GetDescendingMatchRatings`, `ProductList.GetTotalQuantity`, `ProductItemInstance.get_Amount`, `get_AppliedPackaging`; callers P3-03, `EvaluateDelivery`, `OfferDealItems`) | **Prefix** for mod-managed contracts with a delivery that passes our validation: returns the match computed without the recipe term. Every other contract runs vanilla | **High**: central to success chance and satisfaction |
| P3-05 | `Contract.GetDescendingMatchRatings(ProductList.Entry, List<ItemInstance>)` | Ranks provided items per entry | **VERIFIED** | Not patched | — |
| P3-06 | `ProductItemInstance.GetSimilarity(ProductDefinition other, EQuality otherQuality) : float` | Similarity of an item to a requested product and quality | **VERIFIED** (signature); formula **PROBABLE** (recipe + quality terms) | Called by our prefix with the item's **own** definition and the requested quality, so only the recipe term drops out | Medium: formula not observed |
| P3-07 | `HandoverScreenContractMode.GetError(ref string) : bool` | Blocks the Done button with an error | **VERIFIED** (edges to `GetHandoverProducts`, P3-03) | **Postfix (gate)**: for mod-managed contracts, reject before submission with our reason | Low |
| P3-08 | `HandoverScreenContractMode.Refresh()` | Recomputes success chance, Done interactable | **VERIFIED** (edges to P3-03, `ShowSuccessChance`, `Selectable.set_interactable`) | Not patched (it reads P3-03) | — |
| P3-09 | `HandoverScreen.GetHandoverProducts(bool filterForPackagedOnly)` / `GetHandoverItems()` | Items in the handover slots | **VERIFIED** | Read by the gate | Low |
| P3-10 | `Customer.HandoverSubmitted(List<ItemInstance>)` | Submit callback | **VERIFIED** (exists; no scan data) | Not patched | — |
| P3-11 | `Customer.EvaluateDelivery(...) : float` | Satisfaction from match, quality and addiction | **VERIFIED** (edge to P3-04) | Not patched (gets the P3-04 result) | — |
| P3-12 | `Customer.ProcessHandover(Contract, List<ItemInstance>, bool, bool)` | Item transfer, popup, server RPC | **VERIFIED** (edges listed in §2) | **Postfix (observe only)**: marks the transaction committed (idempotency) | Low |
| P3-13 | `Customer.RpcLogic___ProcessHandoverServerSide_3315874220(...)` | XP, money stats, receipts | **VERIFIED** (edges to `LevelManager.AddXP`, `DailySummary.*`, `ProductManager.RecordContractReceipt`) | Not patched: vanilla authority | — |
| P3-14 | `Contract.SubmitPayment(float bonusTotal)` | Pays cash | **VERIFIED** (edge to `MoneyManager.ChangeCashBalance`); caller **UNVERIFIED** (none recorded, probably invoked through a delegate/RPC) | Not patched: vanilla authority | — |
| P3-15 | `Contract.Complete(bool)`, `Contract.End()`, `Contract.Expire(bool)`, `Contract.Fail(bool)` | Contract end states | **VERIFIED** (edges to `Quest.Complete`, `Quest.End`, `QuestManager.SendQuestAction`) | **Postfix (observe only)**: drop the snapshot | Low |
| P3-16 | `Quest.get_GUID() : Il2CppSystem.Guid`, `Contract.get_ProductList()`, `ProductList.entries`, `ProductList.Entry.ProductID/Quality/Quantity`, `Contract.get_Dealer()`, `static List<Contract> Contract.Contracts` | Contract data | **VERIFIED** (interop members) | Read-only | Low |
| P3-17 | `ProductItemInstance.get_Amount()`, `get_AppliedPackaging()`, `BaseItemInstance.get_ID()/get_Quantity()`, `QualityItemInstance.get_Quality()`, `ProductDefinition.get_DrugType()` | Item data | **VERIFIED** (interop members) | Read-only | Low |
| P3-18 | `static List<Player> Player.PlayerList`, `FishNet.InstanceFinder.IsServer` | Session shape and authority | **VERIFIED** (members; `IsServer` is an edge of P3-01) | Multiplayer guard | Low |
| P3-19 | `Customer.TryGenerateContract`, `OfferContract`, `ContractInfo.ProcessMessage` | Offer generation and phone text | **VERIFIED** | Not patched: offers stay vanilla (see §4) | — |
| P3-20 | `Dealer.*` contract paths (`Dealer.ContractedOffered`, `Dealer.SubmitPayment`, `Dealer.CustomerContractEnded`) | Dealer deals | **VERIFIED** | Not patched; dealer contracts are never mod-managed | — |

### Behaviour that is PROBABLE, not observed

- **Units.** `GetProductListMatch` reads `ProductItemInstance.get_Amount()`; a packaged item's units are
  probably `Quantity x Amount` (for example a jar holding 5). Phase 03 counts units that way.
- **Match value.** The return value is probably the fraction of requested units matched, weighted by the
  per-item similarity (0..1). The prefix returns the same shape.
- **Payment.** `Contract.Payment` is fixed at acceptance; whether vanilla scales it with the match is not
  visible in the call graph. Phase 03 does not touch payment either way.
- **Similarity formula.** Comparing an item with its own definition at the requested quality is expected to
  remove the recipe term and keep the quality term.

These are what the in-game tests in [PHASE_03_TESTING.md](PHASE_03_TESTING.md) confirm or refute.

## 4. Design decisions forced by the call graph

1. **Orders stay vanilla on the wire.** `ContractInfo`/`ProductList` are FishNet-serialised (`Write___ScheduleOne_Product_ProductList...`)
   and saved by `Contract.GetSaveData`. Rewriting them would change save and network data. Instead, a
   player contract whose requested entries are all one category becomes **mod-managed**: the mod stores
   `{category, required units, allowed products}` keyed by the contract GUID in its own per-save file.
2. **One decision point.** Everything that judges a delivery (UI chance, error gate, satisfaction) funnels
   through `GetProductListMatch`. Patching it for mod-managed contracts gives the same answer everywhere.
3. **No duplicate transaction logic.** Payment, XP, inventory transfer, receipts and completion all happen in
   vanilla methods after the match; the mod never calls them.
4. **Fail closed.** If any read fails, the contract is not mod-managed, the prefix lets vanilla run, and the
   reason is logged once.
5. **Dealers untouched.** Contracts accepted with a non-null `Dealer` are never mod-managed.
