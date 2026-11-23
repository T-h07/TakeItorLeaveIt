using System;
using MelonLoader;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Diagnostics;

/// <summary>
/// Adapts <see cref="IModLog"/> to the mod's MelonLoader logger. MelonLoader already prefixes
/// every line with the mod name, producing e.g. <c>[TakeItOrLeaveIt] Initializing...</c>.
/// </summary>
internal sealed class MelonModLog : IModLog
{
    private readonly MelonLogger.Instance _logger;

    public MelonModLog(MelonLogger.Instance logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Info(string message) => _logger.Msg(message);

    public void Warning(string message) => _logger.Warning(message);

    public void Error(string message) => _logger.Error(message);

    public void Error(string message, Exception exception) => _logger.Error(message, exception);
}
