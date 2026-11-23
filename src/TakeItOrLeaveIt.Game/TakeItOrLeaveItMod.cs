using System;
using MelonLoader;
using MelonLoader.InternalUtils;
using MelonLoader.Utils;
using TakeItOrLeaveIt.Core;
using TakeItOrLeaveIt.Core.Compatibility;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Environment;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Game;
using TakeItOrLeaveIt.Game.Diagnostics;
using TakeItOrLeaveIt.Game.Discovery;
using TakeItOrLeaveIt.Game.Patching;

[assembly: MelonInfo(typeof(TakeItOrLeaveItMod), ModInfo.Name, ModInfo.Version, ModInfo.Author)]
// Developer/name strings as used by the S1API documentation for Schedule I. MelonLoader matches them
// against the running game; UNVERIFIED against a local install until the first in-game test (P00-T07).
[assembly: MelonGame("TVGS", "Schedule I")]
// Patches are applied explicitly and all-or-nothing by PatchManager, never by automatic PatchAll.
[assembly: HarmonyDontPatchAll]

namespace TakeItOrLeaveIt.Game;

/// <summary>
/// MelonLoader entry point. Phase 01: diagnostics, Harmony infrastructure (no patches) and read-only
/// product discovery. It does NOT change any gameplay (customers, dealers, products, inventory, money, XP, saves).
/// </summary>
public sealed class TakeItOrLeaveItMod : MelonMod
{
    private IModLog _log = null!;
    private PatchManager? _patches;
    private ProductDiscoveryRuntime? _discovery;

    /// <summary>Phase 01 uses the default policy (AnyUnlocked everywhere); Phase 06 adds persistence and the menu.</summary>
    private readonly InMemoryPolicyProvider _policies = new();

    /// <summary>True when initialization completed and gameplay features may be enabled.</summary>
    public static bool IsOperational { get; private set; }

    public override void OnInitializeMelon()
    {
        _log = new MelonModLog(LoggerInstance);
        _log.Info("Initializing...");

        CompatibilityReport? compatibility = null;

        var outcome = new StartupSequence(_log)
            .Step("Report environment", LogEnvironment)
            .Step("Compatibility check", () => compatibility = CheckCompatibility())
            .Step("MelonLoader integration", () => _log.Info("MelonLoader integration initialized."))
            .Step("Harmony infrastructure", () =>
            {
                // Phase 01 still registers no patch modules: discovery is read-only polling.
                _patches = new PatchManager(HarmonyInstance, _log, Array.Empty<IPatchModule>());
                _patches.ApplyAll();
            })
            .Step("Product discovery (read-only)", () => _discovery = new ProductDiscoveryRuntime(_log, _policies))
            .Run();

        IsOperational = outcome == StartupOutcome.Succeeded;

        switch (outcome)
        {
            case StartupOutcome.Succeeded:
                _log.Info("Initialization completed.");
                if (compatibility?.Status == CompatibilityStatus.SupportedWithWarnings)
                {
                    _log.Warning($"Initialized with {compatibility.Warnings.Count} compatibility warning(s); see above.");
                }

                _log.Info(ModInfo.InitializedMessage);
                _log.Info("Phase 01 running in read-only mode: products are discovered and evaluated, nothing in the game is changed.");
                break;
            case StartupOutcome.UnsupportedEnvironment:
                _log.Error("Initialization stopped: unsupported environment. The mod is inactive; the game is unaffected.");
                break;
            default:
                _log.Error("Initialization failed. The mod is inactive; the game is unaffected.");
                break;
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        // One line per scene load (not per frame): confirms the mod survives into gameplay scenes.
        if (IsOperational)
        {
            _log.Info($"Scene loaded: '{sceneName}' (index {buildIndex}). No gameplay changes in Phase 01.");
            _discovery?.PollNow();
        }
    }

    public override void OnUpdate()
    {
        // Throttled inside Tick(): a Stopwatch read per frame, a reflection poll about once per second.
        if (IsOperational)
        {
            _discovery?.Tick();
        }
    }

    public override void OnDeinitializeMelon()
    {
        _patches?.RemoveAll();
        _discovery = null;
        IsOperational = false;
    }

    private void LogEnvironment()
    {
        _log.Info($"{ModInfo.Name} {ModInfo.Version} by {ModInfo.Author} - phase {ModInfo.Phase}; build target: {BuildTarget.Runtime}.");
        _log.Info($"Game: '{UnityInformationHandler.GameName}' by '{UnityInformationHandler.GameDeveloper}', " +
                  $"version '{UnityInformationHandler.GameVersion}', Unity {UnityInformationHandler.EngineVersion}.");
        _log.Info($"MelonLoader {MelonLoader.Properties.BuildInfo.Version}; game runtime reported by loader: {LoaderRuntime()}.");
    }

    private CompatibilityReport CheckCompatibility()
    {
        var detection = GameRuntimeDetector.Detect(MelonEnvironment.GameRootDirectory, PhysicalFileProbe.Instance);
        _log.Info($"Runtime detected from game files: {detection.Runtime}.");
        foreach (var line in detection.Evidence)
        {
            _log.Info($"  evidence: {line}");
        }

        var report = CompatibilityChecker.Evaluate(new EnvironmentFacts
        {
            BuildRuntime = BuildTarget.Runtime,
            LoaderReportedRuntime = LoaderRuntime(),
            DiskDetectedRuntime = detection.Runtime,
            LoaderVersion = MelonLoader.Properties.BuildInfo.Version,
        });

        foreach (var warning in report.Warnings)
        {
            _log.Warning($"Compatibility: {warning}");
        }

        if (!report.AllowsGameplayPatches)
        {
            throw new UnsupportedEnvironmentException(string.Join(" ", report.Errors));
        }

        _log.Info($"Compatibility: {report.Status}.");
        return report;
    }

    private static GameRuntime LoaderRuntime() => MelonUtils.IsGameIl2Cpp() ? GameRuntime.Il2Cpp : GameRuntime.Mono;
}
