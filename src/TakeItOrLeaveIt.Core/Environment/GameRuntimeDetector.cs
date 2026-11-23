using System;
using System.Collections.Generic;
using System.IO;

namespace TakeItOrLeaveIt.Core.Environment;

/// <summary>Result of inspecting a game directory for Unity scripting-backend markers.</summary>
public sealed class RuntimeDetectionResult
{
    public RuntimeDetectionResult(GameRuntime runtime, IReadOnlyList<string> evidence)
    {
        Runtime = runtime;
        Evidence = evidence;
    }

    public GameRuntime Runtime { get; }

    /// <summary>Human-readable list of the markers that were checked and what was found.</summary>
    public IReadOnlyList<string> Evidence { get; }
}

/// <summary>
/// Classifies a Unity game installation as Mono or IL2CPP from files on disk (read-only).
/// These markers are standard Unity player layouts; they are clues, not signatures, so
/// conflicting or missing evidence yields <see cref="GameRuntime.Unknown"/> rather than a guess.
/// </summary>
public static class GameRuntimeDetector
{
    /// <summary>Executable name of Schedule I on Windows.</summary>
    public const string ScheduleOneExecutable = "Schedule I.exe";

    public static RuntimeDetectionResult Detect(string gameDirectory, IFileProbe probe, string executableName = ScheduleOneExecutable)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("Game directory must be provided.", nameof(gameDirectory));
        }

        if (probe is null)
        {
            throw new ArgumentNullException(nameof(probe));
        }

        var evidence = new List<string>();
        var dataDirName = Path.GetFileNameWithoutExtension(executableName) + "_Data";
        var dataDir = Path.Combine(gameDirectory, dataDirName);

        var exePresent = probe.FileExists(Path.Combine(gameDirectory, executableName));
        evidence.Add($"{executableName}: {(exePresent ? "present" : "missing")}");

        var dataPresent = probe.DirectoryExists(dataDir);
        evidence.Add($"{dataDirName}/: {(dataPresent ? "present" : "missing")}");

        var managedAssembly = probe.FileExists(Path.Combine(dataDir, "Managed", "Assembly-CSharp.dll"));
        evidence.Add($"{dataDirName}/Managed/Assembly-CSharp.dll: {(managedAssembly ? "present" : "missing")}");

        var gameAssembly = probe.FileExists(Path.Combine(gameDirectory, "GameAssembly.dll"));
        evidence.Add($"GameAssembly.dll: {(gameAssembly ? "present" : "missing")}");

        var metadata = probe.FileExists(Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat"));
        evidence.Add($"{dataDirName}/il2cpp_data/Metadata/global-metadata.dat: {(metadata ? "present" : "missing")}");

        var looksIl2Cpp = gameAssembly && metadata;
        var looksMono = managedAssembly;

        GameRuntime runtime;
        if (looksIl2Cpp && !looksMono)
        {
            runtime = GameRuntime.Il2Cpp;
        }
        else if (looksMono && !gameAssembly && !metadata)
        {
            runtime = GameRuntime.Mono;
        }
        else
        {
            runtime = GameRuntime.Unknown;
            evidence.Add(looksIl2Cpp || looksMono || gameAssembly || metadata
                ? "Conflicting or partial markers (possible leftover files after a branch switch); runtime not classified."
                : "No Unity scripting-backend markers found; is this the game directory?");
        }

        return new RuntimeDetectionResult(runtime, evidence);
    }
}
