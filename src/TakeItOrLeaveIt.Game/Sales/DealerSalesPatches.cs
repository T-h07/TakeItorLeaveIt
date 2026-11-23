using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Game.Patching;
using ItemList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Phase 04 hooks (docs/PHASE_04_PATCH_ANALYSIS.md, D1 and D2). Targets are resolved by exact signature; a
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

    // ---- D1: category fulfilment at the dealer's handover ----------------------------------------------

    /// <returns>true to run the game's own removal (any contract the mod does not manage); false when the mod removed the planned units.</returns>
    private static bool RemoveContractItemsPrefix(Dealer __instance, Contract contract, EQuality targetQuality, ref ItemList items)
    {
        var coordinator = GameDealers.Coordinator;
        if (coordinator is null || coordinator.ActiveAppointments == 0 || contract is null || __instance is null)
        {
            return true;
        }

        string? id = null;
        HandoverPlan plan;
        try
        {
            id = GameSales.ContractId(contract);
            if (coordinator.Find(id) is null)
            {
                return true;
            }

            var stock = GameDealers.Stock(__instance);
            if (stock is null)
            {
                coordinator.ReturnToVanilla(id, "the dealer could not be identified at handover");
                return true;
            }

            plan = coordinator.PrepareHandover(id, stock);
        }
        catch (Exception ex)
        {
            GameSales.Once("dealer-plan-error", $"Dealer handover planning failed; the game's own removal runs. {ex.GetType().Name}: {ex.Message}", warning: true);
            TryReturnToVanilla(coordinator, id, "planning error");
            return true;
        }

        switch (plan.Decision)
        {
            case HandoverDecision.Vanilla:
                return true;
            case HandoverDecision.NothingToGive:
                items ??= new ItemList();
                return false;
        }

        var removalStarted = false;
        long units = 0;
        try
        {
            items ??= new ItemList();
            foreach (var line in plan.Lines)
            {
                removalStarted = true;
                var removed = __instance.RemoveAndReturnProductFromInventory(line.Product.Value, line.Units, targetQuality);
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

                    units += (long)item.Quantity * item.Amount;
                    items.Add(item);
                }
            }

            __instance.TryMoveOverflowItems();
        }
        catch (Exception ex)
        {
            if (!removalStarted)
            {
                coordinator.AbortHandover(id, $"removal could not start ({ex.GetType().Name})");
                return true;
            }

            // Never run the original after a removal attempt: that could take the stock twice.
            GameSales.Once("dealer-remove-error:" + id, $"Dealer removal for order {id} failed part-way; the customer judges what was removed. {ex.GetType().Name}: {ex.Message}", warning: true);
        }

        coordinator.CompleteHandover(id, units);
        GameSales.InvalidateCache();
        GameSales.Log?.Info($"Dealer order {id}: removed {units} unit(s) ({plan.Detail}) from dealer {plan.Appointment?.DealerId}; payment, cut and XP stay vanilla.");
        return false;
    }

    // ---- D2: keep reservations in line with the dealer's inventory (observe only) ----------------------

    private static void OnTickPostfix(Dealer __instance)
    {
        try
        {
            if (__instance is not null)
            {
                GameDealers.MaybeReconcile(__instance);
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("dealer-reconcile-error", $"Dealer stock reconcile failed; it is retried at the handover. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }

    private static void TryReturnToVanilla(DealerSaleCoordinator coordinator, string? id, string reason)
    {
        try
        {
            coordinator.ReturnToVanilla(id, reason);
        }
        catch (Exception)
        {
            // best effort: the appointment then ends with its contract
        }
    }
}
