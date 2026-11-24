using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Environment;

namespace TakeItOrLeaveIt.Core.Compatibility;

/// <summary>Overall verdict of a compatibility check.</summary>
public enum CompatibilityStatus
{
    /// <summary>No known problems.</summary>
    Supported = 0,

    /// <summary>Likely to work, but something is untested or known to be problematic.</summary>
    SupportedWithWarnings = 1,

    /// <summary>The environment is known to be incompatible; gameplay features must stay disabled.</summary>
    Unsupported = 2,
}

/// <summary>Facts about the running environment, gathered by the game adapter.</summary>
public sealed class EnvironmentFacts
{
    /// <summary>Runtime this mod assembly was compiled for (Il2Cpp or Mono build).</summary>
    public GameRuntime BuildRuntime { get; set; }

    /// <summary>Runtime reported by the mod loader itself (most authoritative).</summary>
    public GameRuntime LoaderReportedRuntime { get; set; }

    /// <summary>Runtime inferred from files on disk; may be <see cref="GameRuntime.Unknown"/>.</summary>
    public GameRuntime DiskDetectedRuntime { get; set; }

    /// <summary>Raw loader version string, e.g. "0.7.3".</summary>
    public string? LoaderVersion { get; set; }

    /// <summary>Phase 07: the game's own version string (Unity application version), e.g. "0.4.7f12"; null when unknown.</summary>
    public string? GameVersion { get; set; }
}

/// <summary>How far a game build has been checked (docs/COMPATIBILITY.md). Never inferred from a successful compile alone.</summary>
public enum GameBuildSupport
{
    /// <summary>The version could not be read.</summary>
    Unknown,

    /// <summary>
    /// The build the interop references were exported from: every hook signature is checked by the compiler and the
    /// automated tests pass. Runtime (in-game) verification is tracked separately in the testing documents.
    /// </summary>
    BuildVerified,

    /// <summary>Any other build: not tested. The hooks are still checked at startup and the mod turns itself off if one is missing.</summary>
    NotTested,
}

/// <summary>Result of <see cref="CompatibilityChecker.Evaluate"/>.</summary>
public sealed class CompatibilityReport
{
    internal CompatibilityReport(CompatibilityStatus status, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
    {
        Status = status;
        Warnings = warnings;
        Errors = errors;
    }

    public CompatibilityStatus Status { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Actionable reasons why the environment is unsupported.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool AllowsGameplayPatches => Status != CompatibilityStatus.Unsupported;

    /// <summary>Phase 07: what is known about the running game build.</summary>
    public GameBuildSupport GameBuild { get; internal set; } = GameBuildSupport.Unknown;
}

/// <summary>
/// Decides whether the current environment is supported. Pure logic: it never touches the game,
/// so "unsupported environment" can be distinguished from "software defect" (an exception).
/// </summary>
public static class CompatibilityChecker
{
    /// <summary>MelonLoader version the mod is compiled against (LavaGang.MelonLoader NuGet package).</summary>
    public static readonly LoaderVersion BuiltAgainstLoader = new(0, 7, 3);

    /// <summary>Oldest loader line whose API surface matches what we compile against.</summary>
    public static readonly LoaderVersion MinimumLoader = new(0, 7, 0);

    /// <summary>
    /// Loader release reported by the community (S1APILoader source, mod pages) to have IL2CPP
    /// interop problems with Schedule I. Not verified locally.
    /// </summary>
    public static readonly LoaderVersion KnownProblematicIl2CppLoader = new(0, 7, 1);

    /// <summary>
    /// Phase 07: the game builds this source was compiled and tested against. Only these are "Build verified"; nothing is
    /// claimed for any other build because it compiles.
    /// </summary>
    public static readonly IReadOnlyList<string> VerifiedGameVersions = new[] { "0.4.7f12" };

    /// <summary>What is known about <paramref name="gameVersion"/>.</summary>
    public static GameBuildSupport ClassifyGameVersion(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return GameBuildSupport.Unknown;
        }

        foreach (var verified in VerifiedGameVersions)
        {
            if (string.Equals(verified, gameVersion!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return GameBuildSupport.BuildVerified;
            }
        }

        return GameBuildSupport.NotTested;
    }

    public static CompatibilityReport Evaluate(EnvironmentFacts facts)
    {
        if (facts is null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        var warnings = new List<string>();
        var errors = new List<string>();

        // 1. Build flavour must match what the loader is running.
        if (facts.LoaderReportedRuntime == GameRuntime.Unknown)
        {
            warnings.Add("The mod loader did not report the game runtime (Mono/IL2CPP).");
        }
        else if (facts.BuildRuntime != GameRuntime.Unknown && facts.BuildRuntime != facts.LoaderReportedRuntime)
        {
            errors.Add(
                $"Wrong mod build: this is the {facts.BuildRuntime} build, but the game is running on {facts.LoaderReportedRuntime}. " +
                $"Install the {facts.LoaderReportedRuntime} build of {ModInfo.Name} instead (see docs/INSTALLATION.md).");
        }

        // 2. On-disk evidence should agree with the loader.
        if (facts.DiskDetectedRuntime != GameRuntime.Unknown
            && facts.LoaderReportedRuntime != GameRuntime.Unknown
            && facts.DiskDetectedRuntime != facts.LoaderReportedRuntime)
        {
            warnings.Add(
                $"Game files look like {facts.DiskDetectedRuntime}, but the loader reports {facts.LoaderReportedRuntime}. " +
                "Leftover files from a Steam branch switch? Verifying the game files in Steam usually fixes this.");
        }

        // 3. Loader version.
        if (!Compatibility.LoaderVersion.TryParse(facts.LoaderVersion, out var loader))
        {
            warnings.Add($"Could not parse the MelonLoader version '{facts.LoaderVersion ?? "<null>"}'.");
        }
        else if (loader < MinimumLoader)
        {
            errors.Add($"MelonLoader {loader.Original} is too old. Install MelonLoader {BuiltAgainstLoader} (minimum {MinimumLoader}).");
        }
        else
        {
            if (loader == KnownProblematicIl2CppLoader && facts.LoaderReportedRuntime == GameRuntime.Il2Cpp)
            {
                warnings.Add(
                    $"MelonLoader {loader.Original} has reported IL2CPP interop problems with Schedule I. " +
                    $"MelonLoader {BuiltAgainstLoader} is recommended.");
            }

            if (loader != BuiltAgainstLoader)
            {
                warnings.Add($"MelonLoader {loader.Original} differs from the version this build was compiled against ({BuiltAgainstLoader}).");
            }
        }

        // 4. Game build (Phase 07). An untested build is a warning, not an error: every hook is still checked by its exact
        // signature at startup and a missing one turns the whole integration off.
        var build = ClassifyGameVersion(facts.GameVersion);
        if (build == GameBuildSupport.NotTested)
        {
            warnings.Add($"Schedule I {facts.GameVersion!.Trim()} is not a tested build (tested: {string.Join(", ", VerifiedGameVersions)}). " +
                         "Every game hook is checked at startup; if one is missing the mod turns itself off and the game stays vanilla. " +
                         "In multiplayer, every player should run the same game build and the same Take It version.");
        }

        var status = errors.Count > 0
            ? CompatibilityStatus.Unsupported
            : warnings.Count > 0 ? CompatibilityStatus.SupportedWithWarnings : CompatibilityStatus.Supported;

        return new CompatibilityReport(status, warnings, errors) { GameBuild = build };
    }
}
