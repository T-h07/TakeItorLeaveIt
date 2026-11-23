using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Diagnostics;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

public class StartupSequenceTests
{
    [Fact]
    public void Runs_all_steps_in_order()
    {
        var order = new List<string>();
        var log = new RecordingLog();

        var outcome = new StartupSequence(log)
            .Step("a", () => order.Add("a"))
            .Step("b", () => order.Add("b"))
            .Step("c", () => order.Add("c"))
            .Run();

        Assert.Equal(StartupOutcome.Succeeded, outcome);
        Assert.Equal(new[] { "a", "b", "c" }, order);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void Unexpected_exception_is_reported_as_defect_and_stops_later_steps()
    {
        var log = new RecordingLog();
        var laterRan = false;
        var boom = new InvalidOperationException("boom");
        var sequence = new StartupSequence(log)
            .Step("first", () => { })
            .Step("broken", () => throw boom)
            .Step("later", () => laterRan = true);

        var outcome = sequence.Run();

        Assert.Equal(StartupOutcome.Failed, outcome);
        Assert.Equal("broken", sequence.FailedStep);
        Assert.False(laterRan);
        var error = Assert.Single(log.Errors);
        Assert.Same(boom, error.Exception);
        Assert.Contains("defect", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_environment_is_distinguished_from_defect()
    {
        var log = new RecordingLog();
        var sequence = new StartupSequence(log)
            .Step("compat", () => throw new UnsupportedEnvironmentException("Wrong build."));

        var outcome = sequence.Run();

        Assert.Equal(StartupOutcome.UnsupportedEnvironment, outcome);
        var error = Assert.Single(log.Errors);
        Assert.Null(error.Exception);
        Assert.Contains("Unsupported environment", error.Message, StringComparison.Ordinal);
        Assert.Contains("Wrong build.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_sequence_succeeds()
    {
        Assert.Equal(StartupOutcome.Succeeded, new StartupSequence(new RecordingLog()).Run());
    }

    [Fact]
    public void Invalid_registration_throws()
    {
        var sequence = new StartupSequence(new RecordingLog());
        Assert.Throws<ArgumentException>(() => sequence.Step("", () => { }));
        Assert.Throws<ArgumentNullException>(() => sequence.Step("x", null!));
        Assert.Throws<ArgumentNullException>(() => new StartupSequence(null!));
    }
}
