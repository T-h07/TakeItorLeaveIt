using System;
using TakeItOrLeaveIt.Core.Compatibility;
using TakeItOrLeaveIt.Core.Environment;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

public class CompatibilityCheckerTests
{
    private static EnvironmentFacts Matching(GameRuntime runtime, string loader = "0.7.3") => new()
    {
        BuildRuntime = runtime,
        LoaderReportedRuntime = runtime,
        DiskDetectedRuntime = runtime,
        LoaderVersion = loader,
    };

    [Theory]
    [InlineData(GameRuntime.Il2Cpp)]
    [InlineData(GameRuntime.Mono)]
    public void Matching_build_and_recommended_loader_is_supported(GameRuntime runtime)
    {
        var report = CompatibilityChecker.Evaluate(Matching(runtime));

        Assert.Equal(CompatibilityStatus.Supported, report.Status);
        Assert.Empty(report.Warnings);
        Assert.True(report.AllowsGameplayPatches);
    }

    [Fact]
    public void Wrong_build_for_runtime_is_unsupported_with_actionable_message()
    {
        var facts = Matching(GameRuntime.Il2Cpp);
        facts.BuildRuntime = GameRuntime.Mono;

        var report = CompatibilityChecker.Evaluate(facts);

        Assert.Equal(CompatibilityStatus.Unsupported, report.Status);
        Assert.False(report.AllowsGameplayPatches);
        Assert.Contains(report.Errors, e => e.Contains("Install the Il2Cpp build", StringComparison.Ordinal));
    }

    [Fact]
    public void Loader_older_than_minimum_is_unsupported()
    {
        var report = CompatibilityChecker.Evaluate(Matching(GameRuntime.Il2Cpp, "0.6.6"));

        Assert.Equal(CompatibilityStatus.Unsupported, report.Status);
        Assert.Contains(report.Errors, e => e.Contains("too old", StringComparison.Ordinal));
    }

    [Fact]
    public void Known_problematic_loader_warns_on_il2cpp_only()
    {
        var il2cpp = CompatibilityChecker.Evaluate(Matching(GameRuntime.Il2Cpp, "0.7.1"));
        var mono = CompatibilityChecker.Evaluate(Matching(GameRuntime.Mono, "0.7.1"));

        Assert.Equal(CompatibilityStatus.SupportedWithWarnings, il2cpp.Status);
        Assert.Contains(il2cpp.Warnings, w => w.Contains("interop", StringComparison.Ordinal));
        Assert.DoesNotContain(mono.Warnings, w => w.Contains("interop", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0.8.0")]
    [InlineData("0.7.4")]
    [InlineData("0.7.0")]
    public void Loader_other_than_built_against_warns(string loader)
    {
        var report = CompatibilityChecker.Evaluate(Matching(GameRuntime.Mono, loader));

        Assert.Equal(CompatibilityStatus.SupportedWithWarnings, report.Status);
        Assert.Contains(report.Warnings, w => w.Contains("differs", StringComparison.Ordinal));
    }

    [Fact]
    public void Unparseable_loader_version_warns_but_does_not_block()
    {
        var report = CompatibilityChecker.Evaluate(Matching(GameRuntime.Il2Cpp, "unknown"));

        Assert.Equal(CompatibilityStatus.SupportedWithWarnings, report.Status);
    }

    [Fact]
    public void Disk_evidence_disagreeing_with_loader_warns()
    {
        var facts = Matching(GameRuntime.Il2Cpp);
        facts.DiskDetectedRuntime = GameRuntime.Mono;

        var report = CompatibilityChecker.Evaluate(facts);

        Assert.Equal(CompatibilityStatus.SupportedWithWarnings, report.Status);
        Assert.Contains(report.Warnings, w => w.Contains("branch switch", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_disk_evidence_is_not_a_warning()
    {
        var facts = Matching(GameRuntime.Il2Cpp);
        facts.DiskDetectedRuntime = GameRuntime.Unknown;

        Assert.Equal(CompatibilityStatus.Supported, CompatibilityChecker.Evaluate(facts).Status);
    }

    [Fact]
    public void Null_facts_throw()
    {
        Assert.Throws<ArgumentNullException>(() => CompatibilityChecker.Evaluate(null!));
    }
}
