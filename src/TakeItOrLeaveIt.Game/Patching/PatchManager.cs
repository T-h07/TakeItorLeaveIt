using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Patching;

/// <summary>
/// Applies Harmony patch modules explicitly (automatic PatchAll is disabled via
/// <c>[assembly: HarmonyDontPatchAll]</c>). All-or-nothing: if any module fails, every patch
/// owned by this mod is removed so the game never runs with a partially applied integration.
/// </summary>
internal sealed class PatchManager
{
    private readonly HarmonyLib.Harmony _harmony;
    private readonly IModLog _log;
    private readonly IReadOnlyList<IPatchModule> _modules;

    public PatchManager(HarmonyLib.Harmony harmony, IModLog log, IEnumerable<IPatchModule> modules)
    {
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modules = (modules ?? throw new ArgumentNullException(nameof(modules))).ToList();
    }

    public bool Applied { get; private set; }

    /// <summary>Applies every registered module, or none of them.</summary>
    public void ApplyAll()
    {
        if (_modules.Count == 0)
        {
            _log.Info("No gameplay patches in this build (the player-sales hooks need local game references; see docs/BUILDING.md).");
            Applied = true;
            return;
        }

        foreach (var module in _modules)
        {
            try
            {
                module.Apply(_harmony);
                _log.Info($"Patch module applied: {module.Name}");
            }
            catch (Exception ex)
            {
                _log.Error($"Patch module '{module.Name}' failed; removing all {Core.ModInfo.Name} patches to keep vanilla behaviour.", ex);
                RemoveAll();
                throw;
            }
        }

        var count = _harmony.GetPatchedMethods().Count();
        _log.Info($"Harmony patches active: {count} method(s) across {_modules.Count} module(s).");
        Applied = true;
    }

    /// <summary>Removes every patch created by this mod's Harmony instance.</summary>
    public void RemoveAll()
    {
        try
        {
            _harmony.UnpatchSelf();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to remove Harmony patches.", ex);
        }

        Applied = false;
    }
}
