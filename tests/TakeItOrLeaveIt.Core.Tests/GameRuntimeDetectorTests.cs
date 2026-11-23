using System;
using TakeItOrLeaveIt.Core.Environment;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

public class GameRuntimeDetectorTests
{
    private static FakeFileProbe BaseInstall() => new FakeFileProbe()
        .File("Schedule I.exe")
        .Dir("Schedule I_Data");

    [Fact]
    public void Il2Cpp_layout_is_detected()
    {
        var probe = BaseInstall()
            .File("GameAssembly.dll")
            .File("Schedule I_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        var result = GameRuntimeDetector.Detect(FakeFileProbe.Root, probe);

        Assert.Equal(GameRuntime.Il2Cpp, result.Runtime);
        Assert.Contains(result.Evidence, e => e.StartsWith("GameAssembly.dll: present", StringComparison.Ordinal));
    }

    [Fact]
    public void Mono_layout_is_detected()
    {
        var probe = BaseInstall().File("Schedule I_Data", "Managed", "Assembly-CSharp.dll");

        var result = GameRuntimeDetector.Detect(FakeFileProbe.Root, probe);

        Assert.Equal(GameRuntime.Mono, result.Runtime);
    }

    [Fact]
    public void Conflicting_markers_are_not_guessed()
    {
        // e.g. leftover files after switching Steam branches
        var probe = BaseInstall()
            .File("GameAssembly.dll")
            .File("Schedule I_Data", "il2cpp_data", "Metadata", "global-metadata.dat")
            .File("Schedule I_Data", "Managed", "Assembly-CSharp.dll");

        var result = GameRuntimeDetector.Detect(FakeFileProbe.Root, probe);

        Assert.Equal(GameRuntime.Unknown, result.Runtime);
        Assert.Contains(result.Evidence, e => e.Contains("Conflicting", StringComparison.Ordinal));
    }

    [Fact]
    public void GameAssembly_without_metadata_is_unknown()
    {
        var probe = BaseInstall().File("GameAssembly.dll");

        Assert.Equal(GameRuntime.Unknown, GameRuntimeDetector.Detect(FakeFileProbe.Root, probe).Runtime);
    }

    [Fact]
    public void Empty_directory_is_unknown_with_explanation()
    {
        var result = GameRuntimeDetector.Detect(FakeFileProbe.Root, new FakeFileProbe());

        Assert.Equal(GameRuntime.Unknown, result.Runtime);
        Assert.Contains(result.Evidence, e => e.Contains("No Unity scripting-backend markers", StringComparison.Ordinal));
    }

    [Fact]
    public void Evidence_lists_every_marker_checked()
    {
        var result = GameRuntimeDetector.Detect(FakeFileProbe.Root, BaseInstall().File("Schedule I_Data", "Managed", "Assembly-CSharp.dll"));

        Assert.Equal(5, result.Evidence.Count);
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        Assert.Throws<ArgumentException>(() => GameRuntimeDetector.Detect(" ", new FakeFileProbe()));
        Assert.Throws<ArgumentNullException>(() => GameRuntimeDetector.Detect(FakeFileProbe.Root, null!));
    }
}
