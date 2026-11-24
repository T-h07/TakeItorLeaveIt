using System;
using MelonLoader;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Diagnostics;

/// <summary>
/// Adapts <see cref="IModLog"/> to the mod's MelonLoader logger. MelonLoader already prefixes
/// every line with the mod name, producing e.g. <c>[TakeItOrLeaveIt] Initializing...</c>.
/// Phase 08: every entry passes through <see cref="LogText.Entry"/>, so absolute paths (which contain the Windows user
/// name) are reduced to file names and no single entry can exceed <see cref="LogText.MaxEntryLength"/>.
/// </summary>
internal sealed class MelonModLog : IModLog
{
    private readonly MelonLogger.Instance _logger;

    public MelonModLog(MelonLogger.Instance logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Info(string message) => _logger.Msg(LogText.Entry(message));

    public void Warning(string message) => _logger.Warning(LogText.Entry(message));

    public void Error(string message) => _logger.Error(LogText.Entry(message));

    public void Error(string message, Exception exception) => _logger.Error(LogText.Entry(message, exception));
}
