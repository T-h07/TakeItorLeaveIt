using System;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Phase 07 game reads for the session model (docs/PHASE_07_NETWORK_ANALYSIS.md §3). Polled every two seconds on the
/// main thread. Every read is guarded: an exception makes the value unknown, and unknown fails closed.
/// Members used (all checked by the compiler against the installed game's interop assemblies):
/// <c>InstanceFinder.IsServer</c>, <c>InstanceFinder.IsClient</c>, <c>Player.PlayerList</c>, <c>Player.Local</c>.
/// </summary>
internal static class GameNetwork
{
    public static NetworkReading Read(bool catalogReady, string? saveIdentity, bool patchesApplied, bool compatibilityOk, string? compatibilityProblem)
    {
        var reading = new NetworkReading
        {
            PatchesApplied = patchesApplied,
            CompatibilityOk = compatibilityOk,
            CompatibilityProblem = compatibilityProblem,
        };

        try
        {
            reading.IsServer = Il2CppFishNet.InstanceFinder.IsServer;
            reading.IsClient = Il2CppFishNet.InstanceFinder.IsClient;
        }
        catch (Exception ex)
        {
            GameSales.Once("network-read", $"Could not read the network state; Take It stays vanilla. {LogText.Describe(ex)}", warning: true);
            reading.IsServer = null;
            reading.IsClient = null;
        }

        try
        {
            reading.PlayerCount = Player.PlayerList?.Count ?? -1;
        }
        catch (Exception)
        {
            reading.PlayerCount = -1;
        }

        var inGame = catalogReady;
        if (!inGame && reading.IsServer == false && reading.IsClient == true)
        {
            try
            {
                inGame = Player.Local is not null;
            }
            catch (Exception)
            {
                inGame = false;
            }
        }

        reading.SaveLoaded = inGame;
        reading.SaveIdentity = reading.IsServer == true ? saveIdentity : null;
        return reading;
    }
}

/// <summary>
/// The rules channel: the game's Steam lobby data (docs/PHASE_07_NETWORK_ANALYSIS.md §4). Steam lets only the lobby
/// owner write lobby data, and the game makes the lobby owner the host (<c>Lobby.IsHost</c>), so a value read as a
/// client was written by the host. Members used: <c>Lobby.Instance</c>, <c>Lobby.IsInLobby</c>, <c>Lobby.IsHost</c>,
/// <c>Lobby.LobbyID</c>, <c>SteamMatchmaking.SetLobbyData/GetLobbyData/DeleteLobbyData/GetLobbyOwner</c>,
/// <c>SteamUser.GetSteamID</c>. Runtime behaviour is NOT verified in game yet (Phase 08, MP-SYNC scenarios).
/// </summary>
internal sealed class SteamLobbyRulesChannel : IRulesChannel
{
    public static readonly SteamLobbyRulesChannel Instance = new();

    private SteamLobbyRulesChannel()
    {
    }

    public bool IsOpen => TryLobby(out _);

    public bool IsOwner
    {
        get
        {
            try
            {
                return TryLobby(out var lobby) && Lobby.Instance.IsHost && SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == SteamUser.GetSteamID().m_SteamID;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public string? Identity => TryLobby(out var lobby) ? lobby.m_SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public bool Publish(string? payload)
    {
        try
        {
            if (!TryLobby(out var lobby))
            {
                return false;
            }

            return payload is null
                ? SteamMatchmaking.DeleteLobbyData(lobby, PolicySyncProtocol.Key)
                : SteamMatchmaking.SetLobbyData(lobby, PolicySyncProtocol.Key, payload);
        }
        catch (Exception ex)
        {
            GameSales.Once("rules-publish", $"Sharing Take It rules with clients failed. {LogText.Describe(ex)}", warning: true);
            return false;
        }
    }

    public string? Read(out bool fromHost)
    {
        fromHost = false;
        try
        {
            if (!TryLobby(out var lobby))
            {
                return null;
            }

            var owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            var self = SteamUser.GetSteamID().m_SteamID;
            // Only the owner can write lobby data; as a client, the owner is the host and not this game.
            fromHost = owner != 0 && owner != self && !Lobby.Instance.IsHost;
            var value = SteamMatchmaking.GetLobbyData(lobby, PolicySyncProtocol.Key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            GameSales.Once("rules-read", $"Reading the host's Take It rules failed. {LogText.Describe(ex)}", warning: true);
            return null;
        }
    }

    private static bool TryLobby(out CSteamID lobby)
    {
        lobby = default;
        try
        {
            var instance = Lobby.Instance;
            if (instance is null || !instance.IsInLobby || instance.LobbyID == 0)
            {
                return false;
            }

            lobby = new CSteamID(instance.LobbyID);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
