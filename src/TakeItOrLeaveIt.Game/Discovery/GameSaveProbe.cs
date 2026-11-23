using TakeItOrLeaveIt.Core.Discovery;

namespace TakeItOrLeaveIt.Game.Discovery;

/// <summary>
/// Observes the save lifecycle. <b>Read-only.</b>
/// <para>Game members used (PROBABLE, from S1API source; to be confirmed in game by P01-T05/T07):
/// <c>ScheduleOne.Persistence.LoadManager.Instance</c>, <c>.IsGameLoaded</c>, <c>.IsLoading</c> and
/// <c>.LoadedGameFolderPath</c>. The folder path is hashed immediately and never logged.</para>
/// </summary>
internal sealed class GameSaveProbe
{
    internal const string LoadManagerType = "ScheduleOne.Persistence.LoadManager";

    public SaveObservation Observe()
    {
        var type = GameReflection.FindGameType(LoadManagerType);
        if (type is null)
        {
            return SaveObservation.Unsupported($"Type {LoadManagerType} not found.");
        }

        if (!GameReflection.TryGetStatic(type, "Instance", out var manager))
        {
            return SaveObservation.Unsupported("LoadManager.Instance not found.");
        }

        if (manager is null)
        {
            return SaveObservation.NoGame(); // singleton not created yet (e.g. during startup)
        }

        if (!GameReflection.TryGet(manager, "IsGameLoaded", out var loadedObj) || loadedObj is not bool isLoaded ||
            !GameReflection.TryGet(manager, "IsLoading", out var loadingObj) || loadingObj is not bool isLoading)
        {
            return SaveObservation.Unsupported("LoadManager.IsGameLoaded / IsLoading not readable.");
        }

        string? identity = null;
        if (GameReflection.TryGet(manager, "LoadedGameFolderPath", out var pathObj))
        {
            identity = SaveIdentity.FromFolderPath(pathObj as string);
        }

        int? productCount = null;
        if ((isLoaded && !isLoading) &&
            GameProductDiscoverySource.TryGetDiscoveredList(out var list, out _, out _))
        {
            productCount = GameReflection.CountOf(list);
        }

        return SaveObservation.Of(isLoaded, isLoading, identity, productCount);
    }
}
