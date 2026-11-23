using System;

namespace TakeItOrLeaveIt.Core.Diagnostics;

/// <summary>
/// Minimal logging abstraction so game-independent code can report diagnostics without
/// depending on MelonLoader. The game project adapts this to <c>MelonLogger.Instance</c>.
/// </summary>
public interface IModLog
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);

    /// <summary>Logs an error together with the full exception (type, message, stack trace).</summary>
    void Error(string message, Exception exception);
}
