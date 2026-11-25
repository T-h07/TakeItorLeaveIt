using System;
using System.Reflection;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Sends a dealer's restock text through the game's own phone conversation with that NPC (docs/PHASE_04_PATCH_ANALYSIS.md
/// §10): <c>NPC.MSGConversation.SendMessage(new Message(text, ESenderType.Other, true, id), notify: true, network: false)</c>.
/// The same calls the game's dealer uses for its payment text (<c>Dealer.SubmitPayment</c>) and S1API uses for NPC
/// texts; the game itself raises the in-game phone notification. No Harmony patch: if the calls are missing,
/// messages are off and sales are unaffected.
/// </summary>
internal static class GameDealerMessagingAdapter
{
    private static readonly Random Ids = new();

    public static bool Available { get; private set; }

    public static string? UnavailableReason { get; private set; }

    /// <summary>Checks once that the conversation getter, message constructor and send method exist.</summary>
    public static void Probe()
    {
        try
        {
            var getter = typeof(NPC).GetProperty(nameof(NPC.MSGConversation), BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            var send = typeof(MSGConversation).GetMethod(nameof(MSGConversation.SendMessage), BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(Message), typeof(bool), typeof(bool) }, null);
            var ctor = typeof(Message).GetConstructor(new[] { typeof(string), typeof(Message.ESenderType), typeof(bool), typeof(int) });
            Available = getter is not null && send is not null && ctor is not null;
            UnavailableReason = Available ? null : "the game's messaging methods were not found in this build";
        }
        catch (Exception ex)
        {
            Available = false;
            UnavailableReason = $"messaging check failed ({ex.GetType().Name})";
        }
    }

    /// <summary>True only when the game accepted the message; false (with a reason) otherwise. Never throws.</summary>
    public static bool TrySend(Dealer dealer, string text, out string? failure)
    {
        failure = null;
        if (!Available)
        {
            failure = UnavailableReason ?? "messaging unavailable";
            return false;
        }

        try
        {
            var conversation = dealer?.MSGConversation;
            if (conversation is null)
            {
                failure = "the dealer has no phone conversation yet";
                return false;
            }

            int id;
            lock (Ids)
            {
                id = Ids.Next(1, int.MaxValue);
            }

            conversation.SendMessage(new Message(text, Message.ESenderType.Other, true, id), true, false);
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{LogText.Describe(ex)}";
            return false;
        }
    }
}
