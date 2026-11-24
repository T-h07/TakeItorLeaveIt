using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.InternalUtils;
using MelonLoader.Utils;
using TakeItOrLeaveIt.Core;
using TakeItOrLeaveIt.Core.Compatibility;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Environment;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
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
/// MelonLoader entry point. Phase 04: diagnostics, read-only product discovery, the mod menu (S1API phone
/// app) with per-save settings, and player sales: customer contracts accepted while the mod is on become
/// category orders validated at handover (IL2CPP builds with local game references). Payment, XP, items and
/// contract completion stay with the game. Hired dealers fulfil the same category orders from reserved stock
/// (Phase 04); products and game saves are not changed.
/// </summary>
public sealed class TakeItOrLeaveItMod : MelonMod
{
    private IModLog _log = null!;
    private PatchManager? _patches;
    private ProductDiscoveryRuntime? _discovery;

    /// <summary>Saved policy of the active save; published by the menu controller after each successful save.</summary>
    private readonly InMemoryPolicyProvider _policies = new();
    private ModMenuController? _menu;
    private PlayerSalesHost? _sales;

    /// <summary>Menu state for the in-game UI (null if initialization failed).</summary>
    internal static ModMenuController? Menu { get; private set; }

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
            .Step("Product discovery (read-only)", () => _discovery = new ProductDiscoveryRuntime(_log, _policies))
            .Step("Mod menu settings", CreateMenu)
            .Step("Player sales", CreatePlayerSales)
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
                _log.Info($"Phase 05: player sales {(_sales?.Status.HooksActive == true ? "hooks active" : "unavailable in this build")}; " +
                          $"dealer sales {(_sales?.DealerStatus.HooksActive == true ? "hooks active" : "unavailable in this build")}.");
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
            _log.Info($"Scene loaded: '{sceneName}' (index {buildIndex}).");
            _discovery?.PollNow();
        }
    }

    public override void OnUpdate()
    {
        // Throttled inside Tick(): a Stopwatch read per frame, a reflection poll about once per second.
        if (IsOperational)
        {
            _discovery?.Tick();
            TickSales();
            TickPhoneMenu();
        }
    }

    private bool _phoneMenuChecked;
#if PHONE_APP
    private bool _phoneMenuDisabled;
#endif

    /// <summary>Drives the S1API phone app (IL2CPP builds made with local references only).</summary>
    private void TickPhoneMenu()
    {
#if PHONE_APP
        if (_phoneMenuDisabled)
        {
            return;
        }

        try
        {
            if (!_phoneMenuChecked)
            {
                _phoneMenuChecked = true;
                if (!PhoneMenu.PhoneMenuHost.IsS1ApiLoaded())
                {
                    _phoneMenuDisabled = true;
                    _log.Warning("S1API is not installed: the in-game phone menu is unavailable. Install S1API (see docs/INSTALLATION.md). Everything else keeps working.");
                    return;
                }

                _log.Info("S1API detected: the 'Take It' app is available on the in-game phone.");
            }

            PhoneMenu.PhoneMenuHost.Tick();
        }
        catch (Exception ex)
        {
            _phoneMenuDisabled = true;
            _log.Error($"Phone menu disabled after an error (is a compatible S1API installed?): {ex}");
        }
#else
        if (!_phoneMenuChecked)
        {
            _phoneMenuChecked = true;
            _log.Info("This build does not include the in-game phone menu (Mono build or built without S1API references).");
        }
#endif
    }

    private bool _salesDisabled;

    private void TickSales()
    {
        if (_sales is null || _salesDisabled)
        {
            return;
        }

        try
        {
            _sales.Tick();
#if GAME_PATCHES
            Sales.SalesSession.Tick(_discovery?.Catalog.State.IsReady == true);
#endif
        }
        catch (Exception ex)
        {
            _salesDisabled = true;
            _log.Error($"Player sales housekeeping stopped after an error (hooks stay as they are; restart the game to retry): {ex}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        _patches?.RemoveAll();
#if GAME_PATCHES
        Sales.PlayerSalesPatches.MarkRemoved();
        Sales.DealerSalesPatches.MarkRemoved();
        Sales.OfferPatches.MarkRemoved();
        Sales.OfferPatches.Filter = null;
        Sales.GameSales.Coordinator = null;
        Sales.GameDealers.Coordinator = null;
        Sales.GameDealers.Notifier = null;
        Sales.GameDealers.Reset();
#endif
        _sales?.Shutdown();
        _sales = null;
        _menu?.Dispose();
        _menu = null;
        Menu = null;
        _discovery = null;
        IsOperational = false;
    }

    private void CreateMenu()
    {
        // Mod-owned folder under MelonLoader's UserData; never inside the game's own save folders.
        var root = Path.Combine(MelonEnvironment.UserDataDirectory, ModInfo.Name);
        _menu = new ModMenuController(_discovery!.Catalog, new JsonPolicyStore(root), _policies, _log)
        {
            HostDiagnostics = new[]
            {
                $"Mod: {ModInfo.Name} {ModInfo.Version} ({BuildTarget.Runtime} build)",
                $"Game: {UnityInformationHandler.GameName} {UnityInformationHandler.GameVersion} (Unity {UnityInformationHandler.EngineVersion})",
                $"MelonLoader {MelonLoader.Properties.BuildInfo.Version}; runtime {LoaderRuntime()}",
            },
        };
        Menu = _menu;
        _log.Info("Mod menu settings initialized (stored per save under UserData/TakeItOrLeaveIt/saves).");
    }

    private void CreatePlayerSales()
    {
        var root = Path.Combine(MelonEnvironment.UserDataDirectory, ModInfo.Name);
        _sales = new PlayerSalesHost(_log, _policies, _discovery!.Catalog, root, () => _menu);
        _menu!.SalesStatus = _sales.StatusLines;

        var modules = new List<IPatchModule>();
#if GAME_PATCHES
        Sales.GameSales.Coordinator = _sales.Coordinator;
        Sales.GameSales.Log = _log;
        Sales.GameSales.CatalogGeneration = () => _discovery.Catalog.State.Snapshot?.Generation ?? -1;
        Sales.SalesSession.Catalog = () => _discovery.Catalog.State.Snapshot;
        Sales.GameDealers.Coordinator = _sales.Dealers;
        Sales.GameDealers.Notifier = _sales.Notifier;
        _menu.Dealers = Sales.GameDealers.SeenDealers;
        Sales.OfferPatches.Filter = _sales.Offers;
        _sales.Coordinator.Prices = Sales.GamePrices.Source;
        _sales.Dealers.Prices = Sales.GamePrices.Source;
        Sales.GameDealerMessagingAdapter.Probe();
        modules.Add(new Sales.PlayerSalesPatches());
        modules.Add(new Sales.DealerSalesPatches());
        modules.Add(new Sales.OfferPatches());
#endif
        _patches = new PatchManager(HarmonyInstance, _log, modules);
        try
        {
            _patches.ApplyAll();
        }
        catch (Exception)
        {
            // PatchManager already removed every patch and logged the cause; the rest of the mod keeps working.
#if GAME_PATCHES
            Sales.GameSales.Coordinator = null;
            Sales.GameDealers.Coordinator = null;
            Sales.GameDealers.Notifier = null;
            Sales.OfferPatches.Filter = null;
#endif
            _sales.SetHooks(false, false, false, false, "a game method could not be patched (see the error above); vanilla sales rules apply");
            return;
        }

#if GAME_PATCHES
        _sales.SetHooks(Sales.PlayerSalesPatches.Applied, Sales.DealerSalesPatches.Applied, Sales.OfferPatches.Applied,
            Sales.GameDealerMessagingAdapter.Available, Sales.GameDealerMessagingAdapter.UnavailableReason ?? "not applied");
        if (!Sales.GameDealerMessagingAdapter.Available)
        {
            _log.Warning($"Dealer restock messages are off: {Sales.GameDealerMessagingAdapter.UnavailableReason}. Sales are not affected.");
        }
#else
        _sales.SetHooks(false, false, false, false, BuildTarget.Runtime == GameRuntime.Mono
            ? "the Mono build has no game references; use the IL2CPP build"
            : "built without local game references (see docs/BUILDING.md)");
#endif
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
