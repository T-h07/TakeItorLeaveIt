using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using TakeItOrLeaveIt.Game.Patching;
using CandidateList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Tuple<Il2CppScheduleOne.Product.ProductDefinition, int>>;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Policy-based customer offers (docs/PHASE_04_PATCH_ANALYSIS.md §9, hook O1). Postfix on
/// <c>Customer.GetOrderableProductsWithQuantities(Dealer)</c>, the single candidate pool behind
/// <c>GetWeightedRandomProduct</c> → <c>TryGenerateContract</c> (player offers with a null dealer, dealer offers with
/// the dealer) and <c>ShouldTryApproachPlayer</c>. It removes candidates the live Take It settings do not permit
/// before the game chooses and prices an offer, so the chosen product, its price, the message and the journal all
/// come from vanilla and stay consistent. The list is the one the method just built; no shared customer data,
/// product definition or price is changed.
/// </summary>
internal sealed class OfferPatches : IPatchModule
{
    public string Name => "Customer offers (policy filter)";

    public static bool Applied { get; private set; }

    public static OfferFilter? Filter { get; set; }

    public void Apply(HarmonyLib.Harmony harmony)
    {
        Applied = false;
        var pool = typeof(Customer).GetMethod(nameof(Customer.GetOrderableProductsWithQuantities),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Dealer) }, null);
        if (pool is null || pool.ReturnType != typeof(CandidateList))
        {
            throw new MissingMethodException($"{typeof(Customer).FullName}.GetOrderableProductsWithQuantities(Dealer) with the expected return type not found in this game build.");
        }

        harmony.Patch(pool, postfix: new HarmonyMethod(typeof(OfferPatches).GetMethod(nameof(CandidatesPostfix), BindingFlags.Static | BindingFlags.NonPublic)
                                                       ?? throw new MissingMethodException(nameof(OfferPatches), nameof(CandidatesPostfix))));
        Applied = true;
    }

    public static void MarkRemoved() => Applied = false;

    /// <param name="__0">The game's <c>dealer</c> argument: null when the customer orders from the player, else the
    /// dealer whose rules and switch apply (Phase 05; meaning PROBABLE, see PHASE_05_TESTING.md).</param>
    /// <param name="__result">The game's candidate list, filtered in place.</param>
    private static void CandidatesPostfix(Dealer? __0, CandidateList __result)
    {
        var filter = Filter;
        if (filter is null || __result is null)
        {
            return;
        }

        try
        {
            var scope = __0 is null ? SalesScope.Player : SalesScope.Dealer(GameDealers.DealerId(__0));
            var removed = filter.Filter(__result.Count, i => __result[i]?.Item1?.ID, i => __result.RemoveAt(i), scope);
            if (removed > 0)
            {
                GameSales.Once($"offer-filter:{filter.CandidatesRemoved / 50}",
                    $"Customer offers: {filter.CandidatesRemoved} candidate(s) removed by the Take It settings so far (of {filter.CandidatesSeen} seen).");
            }
        }
        catch (Exception ex)
        {
            GameSales.Once("offer-filter-error", $"Customer offer filter failed for one offer; that offer used the game's own candidates. {LogText.Describe(ex)}", warning: true);
        }
    }
}
