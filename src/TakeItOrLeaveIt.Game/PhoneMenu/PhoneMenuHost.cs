using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TakeItOrLeaveIt.Game.PhoneMenu;

/// <summary>
/// Boundary between the mod and the S1API phone app. The mod only calls these methods inside try/catch,
/// so a missing S1API (types fail to load when first used) disables the menu instead of breaking the mod.
/// </summary>
internal static class PhoneMenuHost
{
    /// <summary>True when an assembly named "S1API" is loaded in this process.</summary>
    public static bool IsS1ApiLoaded()
        => AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, "S1API", StringComparison.Ordinal));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Tick() => TakeItOrLeaveItPhoneApp.Current?.Tick();
}
