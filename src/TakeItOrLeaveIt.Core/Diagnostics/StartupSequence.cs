using System;
using System.Collections.Generic;

namespace TakeItOrLeaveIt.Core.Diagnostics;

/// <summary>How a startup sequence ended.</summary>
public enum StartupOutcome
{
    /// <summary>Every step completed.</summary>
    Succeeded = 0,

    /// <summary>A step reported that the environment is not supported (expected, actionable condition).</summary>
    UnsupportedEnvironment = 1,

    /// <summary>A step threw an unexpected exception (software defect).</summary>
    Failed = 2,
}

/// <summary>Thrown by a startup step to signal an unsupported environment rather than a bug.</summary>
public sealed class UnsupportedEnvironmentException : Exception
{
    public UnsupportedEnvironmentException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Runs named initialization steps in order, stopping at the first failure. Each failure is
/// logged once with enough context to tell an unsupported environment from a defect, and no
/// exception escapes to the mod loader (which could otherwise disrupt other mods or the game).
/// </summary>
public sealed class StartupSequence
{
    private readonly IModLog _log;
    private readonly List<KeyValuePair<string, Action>> _steps = new();

    public StartupSequence(IModLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Name of the step that stopped the sequence, if any.</summary>
    public string? FailedStep { get; private set; }

    public StartupSequence Step(string name, Action action)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Step name is required.", nameof(name));
        }

        _steps.Add(new KeyValuePair<string, Action>(name, action ?? throw new ArgumentNullException(nameof(action))));
        return this;
    }

    public StartupOutcome Run()
    {
        FailedStep = null;
        foreach (var step in _steps)
        {
            try
            {
                step.Value();
            }
            catch (UnsupportedEnvironmentException ex)
            {
                FailedStep = step.Key;
                _log.Error($"Unsupported environment during '{step.Key}': {ex.Message} Gameplay features stay disabled.");
                return StartupOutcome.UnsupportedEnvironment;
            }
            catch (Exception ex)
            {
                FailedStep = step.Key;
                _log.Error($"Initialization step '{step.Key}' failed unexpectedly (likely a mod defect; please report it with this log). Gameplay features stay disabled.", ex);
                return StartupOutcome.Failed;
            }
        }

        return StartupOutcome.Succeeded;
    }
}
