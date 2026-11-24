using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Handover;
using TakeItOrLeaveIt.Core.Sales;
using TakeItOrLeaveIt.Game.Patching;
using ItemList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>;
using ProductItemList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Product.ProductItemInstance>;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Phase 03 hooks (docs/PHASE_03_PATCH_ANALYSIS.md, P3-02/03/04/07/08/12/15). Every target is resolved by its
/// exact signature; a missing one throws so <see cref="PatchManager"/> removes all patches (vanilla game).
/// Every hook only acts on contracts the coordinator manages; for any other contract it does nothing.
/// No hook pays, moves items, awards XP or completes a contract.
/// </summary>
internal sealed class PlayerSalesPatches : IPatchModule
{
    public string Name => "Player sales (category orders)";

    /// <summary>Set once every hook is applied.</summary>
    public static bool Applied { get; private set; }

    public void Apply(HarmonyLib.Harmony harmony)
    {
        Applied = false;
        var accepted = Find(typeof(QuestManager), nameof(QuestManager.ContractAccepted),
            typeof(Customer), typeof(ContractInfo), typeof(bool), typeof(string), typeof(Dealer));
        var matchItems = Find(typeof(Contract), nameof(Contract.GetProductListMatch), typeof(ItemList), typeof(int).MakeByRefType());
        var matchProducts = Find(typeof(Contract), nameof(Contract.GetProductListMatch), typeof(ProductItemList), typeof(int).MakeByRefType());
        var getError = Find(typeof(HandoverScreenContractMode), nameof(HandoverScreenContractMode.GetError), typeof(string).MakeByRefType());
        var refresh = Find(typeof(HandoverScreenContractMode), nameof(HandoverScreenContractMode.Refresh));
        var processHandover = Find(typeof(Customer), nameof(Customer.ProcessHandover), typeof(Contract), typeof(ItemList), typeof(bool), typeof(bool));
        var complete = Find(typeof(Contract), nameof(Contract.Complete), typeof(bool));
        var end = Find(typeof(Contract), nameof(Contract.End));
        var expire = Find(typeof(Contract), nameof(Contract.Expire), typeof(bool));
        var fail = Find(typeof(Contract), nameof(Contract.Fail), typeof(bool));

        harmony.Patch(accepted, postfix: Hook(nameof(ContractAcceptedPostfix)));
        harmony.Patch(matchItems, prefix: Hook(nameof(MatchItemsPrefix)));
        harmony.Patch(matchProducts, prefix: Hook(nameof(MatchProductsPrefix)));
        harmony.Patch(getError, postfix: Hook(nameof(GetErrorPostfix)));
        harmony.Patch(refresh, postfix: Hook(nameof(RefreshPostfix)));
        harmony.Patch(processHandover, postfix: Hook(nameof(ProcessHandoverPostfix)));
        harmony.Patch(complete, postfix: Hook(nameof(CompletePostfix)));
        harmony.Patch(end, postfix: Hook(nameof(EndPostfix)));
        harmony.Patch(expire, postfix: Hook(nameof(ExpirePostfix)));
        harmony.Patch(fail, postfix: Hook(nameof(FailPostfix)));
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
        => new(typeof(PlayerSalesPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new MissingMethodException(nameof(PlayerSalesPatches), name));

    // ---- P3-02: acceptance → snapshot --------------------------------------------------------------

    private static void ContractAcceptedPostfix(Contract __result, Dealer dealer)
    {
        try
        {
            var coordinator = GameSales.Coordinator;
            if (coordinator is null || __result is null)
            {
                return;
            }

            SalesSession.RefreshAuthority();
            if (dealer is not null)
            {
                DealerAccepted(__result, dealer);
                return;
            }

            var creation = coordinator.OnContractAccepted(GameSales.ToRequest(__result, dealer is null));
            if (creation.IsManaged)
            {
                GameSales.Log?.Info($"Category order created: {creation.Order}.");
            }
            else
            {
                GameSales.Log?.Info($"Contract {GameSales.ContractId(__result)} uses vanilla rules: {creation.Outcome} ({creation.Detail})");
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("accept-error", $"Could not read an accepted contract; it uses vanilla rules. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }

    /// <summary>Phase 04: a hired dealer's contract becomes a category appointment with a stock reservation.</summary>
    private static void DealerAccepted(Contract contract, Dealer dealer)
    {
        var dealers = GameDealers.Coordinator;
        if (dealers is null)
        {
            GameSales.Log?.Info($"Contract {GameSales.ContractId(contract)} uses vanilla rules: dealer sales unavailable.");
            return;
        }

        var dealerId = GameDealers.DealerId(dealer);
        var stock = dealerId is null ? null : GameDealers.Stock(dealer);
        var creation = dealers.OnContractAccepted(GameSales.ToRequest(contract, isPlayerContract: false), dealerId, stock);
        GameSales.Log?.Info(creation.IsManaged
            ? $"Dealer appointment created: {creation.Detail}."
            : $"Dealer contract {GameSales.ContractId(contract)} uses vanilla rules: {creation.Outcome} ({creation.Detail})");
    }

    // ---- P3-03/04: the match every judge of a delivery uses ------------------------------------------

    private static bool MatchItemsPrefix(Contract __instance, ItemList items, ref int matchedProductCount, ref float __result)
        => Match(__instance, items is null ? null : GameSales.Items(items), ref matchedProductCount, ref __result);

    private static bool MatchProductsPrefix(Contract __instance, ProductItemList items, ref int matchedProductCount, ref float __result)
        => Match(__instance, items is null ? null : GameSales.Items(items), ref matchedProductCount, ref __result);

    /// <returns>false when the mod answered (vanilla skipped); true to run vanilla.</returns>
    private static bool Match(Contract contract, System.Collections.Generic.List<ItemInstance?>? items, ref int matched, ref float result)
    {
        try
        {
            if (items is null || !GameSales.ManagedAny(contract, out var id))
            {
                return true;
            }

            var validation = GameSales.Validate(id!, items);
            if (validation is null)
            {
                return true;
            }

            var match = validation.IsValid ? GameSales.Match(contract, validation, items) : default;
            result = match.Match;
            matched = match.MatchedUnits;
            return false;
        }
        catch (Exception ex)
        {
            // Never approve on an error: the game's own exact-product check decides instead.
            GameSales.Once("match-error:" + ex.GetType().Name, $"Category match failed; vanilla matching used for this delivery. {ex.GetType().Name}: {ex.Message}", warning: true);
            return true;
        }
    }

    // ---- P3-07: gate with a readable reason ------------------------------------------------------------

    private static void GetErrorPostfix(HandoverScreenContractMode __instance, ref string errorMessage, ref bool __result)
    {
        try
        {
            var contract = __instance.CurrentContract;
            if (GameSales.Managed(contract, out var id) is null)
            {
                return;
            }

            var items = __instance._mainScreen?.GetHandoverItems();
            var validation = GameSales.Validate(id!, GameSales.Items(items));
            if (validation is { IsValid: false })
            {
                errorMessage = validation.Message;
                __result = true;
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("gate-error", $"Handover gate failed; the game's own checks apply. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }

    // ---- P3-08: say what the order really asks for -----------------------------------------------------

    private static void RefreshPostfix(HandoverScreenContractMode __instance)
    {
        try
        {
            var order = GameSales.Managed(__instance.CurrentContract, out _)?.Order;
            var label = __instance._contractDescriptionLabel;
            if (order is null || label is null)
            {
                return;
            }

            label.text = SalesSession.Describe(order);
        }
        catch (Exception ex)
        {
            GameSales.Once("label-error", $"Could not show the category order on the handover screen. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }

    // ---- P3-12 / P3-15: observe the vanilla transaction and the end of the contract --------------------

    private static void ProcessHandoverPostfix(Contract contract)
    {
        try
        {
            var coordinator = GameSales.Coordinator;
            if (coordinator is null || !GameSales.ManagedAny(contract, out var id))
            {
                return;
            }

            var dealers = GameDealers.Coordinator;
            if (dealers?.Find(id) is not null)
            {
                if (dealers.OnHandoverProcessed(id) == CommitObservation.First)
                {
                    GameSales.Log?.Info($"Dealer order {id}: handover processed by the game (payment, dealer cut and XP are vanilla's).");
                }
            }
            else if (coordinator.OnHandoverProcessed(id) == CommitObservation.First)
            {
                GameSales.Log?.Info($"Order {id}: handover processed by the game (payment, XP and items are vanilla's).");
            }

            GameSales.InvalidateCache();
        }
        catch (Exception ex)
        {
            GameSales.Once("commit-error", $"Could not record a processed handover. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }

    private static void CompletePostfix(Contract __instance) => Ended(__instance, SaleEnd.Completed);

    private static void EndPostfix(Contract __instance) => Ended(__instance, SaleEnd.Canceled);

    private static void ExpirePostfix(Contract __instance) => Ended(__instance, SaleEnd.Expired);

    private static void FailPostfix(Contract __instance) => Ended(__instance, SaleEnd.Failed);

    private static void Ended(Contract contract, SaleEnd fallback)
    {
        try
        {
            var coordinator = GameSales.Coordinator;
            var dealers = GameDealers.Coordinator;
            if (contract is null || ((coordinator is null || coordinator.Book.Count == 0) && (dealers is null || dealers.ActiveAppointments == 0)))
            {
                return;
            }

            // The quest state is more precise than the hook (Complete may run End internally).
            var end = contract.State.ToString() switch
            {
                "Completed" => SaleEnd.Completed,
                "Failed" => SaleEnd.Failed,
                "Expired" => SaleEnd.Expired,
                "Cancelled" or "Canceled" => SaleEnd.Canceled,
                _ => fallback,
            };
            var id = GameSales.ContractId(contract);
            var ended = coordinator?.OnContractEnded(id, end);
            if (ended is not null)
            {
                GameSales.Log?.Info($"Order {id} ended ({ended.State}, {ended.CommitCount} handover(s) observed); snapshot removed.");
                GameSales.InvalidateCache();
            }

            var appointment = dealers?.OnContractEnded(id, end);
            if (appointment is not null)
            {
                GameSales.Log?.Info($"Dealer order {id} ended ({appointment.State}, {appointment.RemovedUnits} unit(s) removed, {appointment.CommitCount} handover(s) observed); reservation released.");
                GameSales.InvalidateCache();
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("end-error", $"Could not observe the end of a contract. {ex.GetType().Name}: {ex.Message}", warning: true);
        }
    }
}
