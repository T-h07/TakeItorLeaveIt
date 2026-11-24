using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Game.Patching;
using ItemList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Phase 04 hooks (docs/PHASE_04_PATCH_ANALYSIS.md, D1 and D2; strict fulfilment §11). Targets are resolved by exact signature; a
/// missing one throws so <see cref="PatchManager"/> removes every patch (vanilla game). Acceptance, the customer's
/// judgement, settlement and contract end reuse the Phase 03 hooks (<see cref="PlayerSalesPatches"/>).
/// Nothing here pays, awards XP, completes contracts or changes the dealer's cash or cut.
/// </summary>
internal sealed class DealerSalesPatches : IPatchModule
{
    public string Name => "Dealer sales (category appointments)";

    public static bool Applied { get; private set; }

    public void Apply(HarmonyLib.Harmony harmony)
    {
        Applied = false;
        var removeItems = Find(typeof(Dealer), nameof(Dealer.RemoveContractItems), typeof(Contract), typeof(EQuality), typeof(ItemList).MakeByRefType());
        var onTick = Find(typeof(Dealer), nameof(Dealer.OnTick));

        // The removal method D1 calls must exist with this exact signature too, or nothing is patched.
        Find(typeof(Dealer), nameof(Dealer.RemoveAndReturnProductFromInventory), typeof(string), typeof(int), typeof(EQuality));
        Find(typeof(Dealer), nameof(Dealer.TryMoveOverflowItems));
        Find(typeof(Dealer), nameof(Dealer.GetAllSlots));
        Find(typeof(Dealer), nameof(Dealer.AddItemToInventory), typeof(ItemInstance)); // rollback of a short removal

        harmony.Patch(removeItems, prefix: Hook(nameof(RemoveContractItemsPrefix)));
        harmony.Patch(onTick, postfix: Hook(nameof(OnTickPostfix)));
        Applied = true;
    }

    public static void MarkRemoved() => Applied = false;

    private static MethodInfo Find(Type type, string name, params Type[] parameters)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly, null, parameters, null);
        return method ?? throw new MissingMethodException(
            $"{type.FullName}.{name}({string.Join(", ", parameters.Select(p => p.Name))}) not found in this game build.");
    }

    private static HarmonyMethod Hook(string name)
        => new(typeof(DealerSalesPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new MissingMethodException(nameof(DealerSalesPatches), name));

    // ---- D1: category fulfilment at the dealer's handover (strict) ----------------------------------

    /// <returns>
    /// true to run the game's own removal: only for contracts the mod does not manage and whose products the settings
    /// permit. false for every managed appointment (the planned units, or nothing) and for an unmanaged contract that
    /// asks for an excluded product (nothing). A managed appointment never falls back to the game's exact-product removal.
    /// </returns>
    private static bool RemoveContractItemsPrefix(Dealer __instance, Contract contract, EQuality targetQuality, ref ItemList items)
    {
        var coordinator = GameDealers.Coordinator;
        if (coordinator is null || contract is null || __instance is null)
        {
            return true;
        }

        string? id = null;
        var managed = false;
        var held = false;
        HandoverPlan plan;
        try
        {
            id = GameSales.ContractId(contract);
            held = coordinator.HoldsSnapshot(id);
            managed = coordinator.Find(id) is not null;
            if (!managed && held)
            {
                // Phase 08 (fail closed): the appointment exists but authority is not confirmed right now. Never fall
                // back to the game's exact-product removal for it; hand over nothing this time.
                items ??= new ItemList();
                GameSales.Once("dealer-held-unchecked:" + id, $"Dealer order {id}: session authority not confirmed at handover; nothing handed over (the appointment is kept).", warning: true);
                return false;
            }

            if (!managed)
            {
                if (coordinator.BlocksUnmanaged(GameSales.ToRequest(contract, isPlayerContract: false), GameDealers.DealerId(__instance), out var reason))
                {
                    items ??= new ItemList();
                    GameSales.Once("dealer-blocked:" + id, $"Dealer contract {id}: {reason}; the dealer hands over nothing (excluded products are never sold).", warning: true);
                    return false;
                }

                return true;
            }

            var stock = GameDealers.Stock(__instance);
            if (stock is null)
            {
                items ??= new ItemList();
                GameSales.Once("dealer-unidentified:" + id, $"Dealer order {id}: the dealer could not be identified at handover; nothing handed over.", warning: true);
                return false;
            }

            plan = coordinator.PrepareHandover(id, stock);
        }
        catch (Exception ex)
        {
            if (!managed && !held)
            {
                return true;
            }

            items ??= new ItemList();
            GameSales.Once("dealer-plan-error", $"Dealer handover planning failed; nothing handed over for this managed order. {LogText.Describe(ex)}", warning: true);
            return false;
        }

        switch (plan.Decision)
        {
            case HandoverDecision.Vanilla:
                return true;
            case HandoverDecision.NothingToGive:
            case HandoverDecision.Withheld:
                items ??= new ItemList();
                GameSales.Log?.Info($"Dealer order {id}: nothing handed over ({plan.Detail}).");
                return false;
        }

        var removedItems = new System.Collections.Generic.List<ItemInstance>();
        var removedLots = new System.Collections.Generic.List<RemovedLot>();
        var removalStarted = false;
        long units = 0;
        try
        {
            foreach (var line in plan.Lines)
            {
                removalStarted = true;
                // Phase 05: never below the reserved lot's tier (which already meets the order's minimum).
                var minimum = line.Quality is { } lot && (int)lot > (int)targetQuality ? (EQuality)(int)lot : targetQuality;
                var removed = __instance.RemoveAndReturnProductFromInventory(line.Product.Value, line.Units, minimum);
                if (removed is null)
                {
                    continue;
                }

                for (var i = 0; i < removed.Count; i++)
                {
                    var item = removed[i];
                    if (item is null)
                    {
                        continue;
                    }

                    var itemUnits = (long)item.Quantity * (item.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>()?.Amount ?? 1);
                    units += itemUnits;
                    removedItems.Add(item);
                    // Phase 07: record what actually left (product and tier); the game's removal takes a minimum, not a lot.
                    removedLots.Add(new RemovedLot(
                        TakeItOrLeaveIt.Core.Products.ProductKey.TryCreate(item.ID, out var removedKey) ? removedKey : default, // unknown id: never matches the plan, so it rolls back
                        GameSales.QualityOf(item), itemUnits));
                }
            }
        }
        catch (Exception ex)
        {
            if (!removalStarted)
            {
                coordinator.AbortHandover(id, $"removal could not start ({ex.GetType().Name})");
                items ??= new ItemList();
                return false;
            }

            // Never run the original after a removal attempt: that could take the stock twice. What was removed so far
            // is checked below and given back unless it is exactly the planned amount.
            GameSales.Once("dealer-remove-error:" + id, $"Dealer removal for order {id} failed part-way. {LogText.Describe(ex)}", warning: true);
        }

        items ??= new ItemList();
        var completion = coordinator.CompleteHandover(id, removedLots, out var lotMismatches);
        if (completion == HandoverCompletion.Committed)
        {
            foreach (var item in removedItems)
            {
                items.Add(item);
            }

            GameSales.Log?.Info($"Dealer order {id}: removed {units} unit(s) ({plan.Detail}) from dealer {plan.Appointment?.DealerId}; payment, cut and XP stay vanilla.");
        }
        else
        {
            // No partial delivery: every removed item goes back to the dealer and the customer gets nothing.
            var returned = 0;
            var queued = 0;
            var owner = plan.Appointment?.DealerId ?? GameDealers.DealerId(__instance);
            foreach (var item in removedItems)
            {
                try
                {
                    __instance.AddItemToInventory(item);
                    returned++;
                }
                catch (Exception ex)
                {
                    // Phase 08: keep the item and retry on the dealer's next ticks instead of losing it.
                    var itemUnits = (long)item.Quantity * (item.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>()?.Amount ?? 1);
                    if (owner is not null && GameDealers.PendingReturns.Add(owner, item, itemUnits))
                    {
                        queued++;
                    }
                    else
                    {
                        GameSales.Log?.Error($"Dealer order {id}: a removed item ({itemUnits} unit(s)) could not be given back and could not be queued for a retry; it is lost. {LogText.Describe(ex)}");
                    }

                    GameSales.Once("dealer-return-error:" + id, $"Dealer order {id}: could not give a removed item back to the dealer at once; it is retried on the dealer's next ticks. {LogText.Describe(ex)}", warning: true);
                }
            }

            var queuedText = queued > 0 ? $", {queued} queued for a retry" : string.Empty;
            GameSales.Log?.Warning($"Dealer order {id}: removal returned {units} of {plan.Appointment?.Order.RequiredUnits} planned unit(s); {returned} item stack(s) given back to the dealer{queuedText}, nothing handed over.");
        }

        try
        {
            __instance.TryMoveOverflowItems();
        }
        catch (Exception)
        {
            // cosmetic: the game moves overflow items on its own later
        }

        if (lotMismatches > 0)
        {
            // Another tier left than the one reserved: re-check every hold of this dealer against what is really left.
            try
            {
                var after = GameDealers.Stock(__instance);
                if (after is not null)
                {
                    coordinator.Reconcile(after, force: true);
                }
            }
            catch (Exception ex)
            {
                GameSales.Once("dealer-lot-reconcile:" + id, $"Dealer order {id}: re-checking reservations after a quality-tier difference failed; it is retried on the dealer's next tick. {LogText.Describe(ex)}", warning: true);
            }
        }

        GameSales.InvalidateCache();
        return false;
    }

    // ---- D2: keep reservations in line with the dealer's inventory (observe only) ----------------------

    private static void OnTickPostfix(Dealer __instance)
    {
        try
        {
            if (__instance is not null)
            {
                GameDealers.RetryReturns(__instance);
                GameDealers.Observe(__instance);
                GameDealers.MaybeReconcile(__instance);
                GameDealers.MaybeNotify(__instance);
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("dealer-reconcile-error", $"Dealer stock reconcile failed; it is retried at the handover. {LogText.Describe(ex)}", warning: true);
        }
    }
}
