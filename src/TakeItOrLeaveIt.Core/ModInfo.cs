namespace TakeItOrLeaveIt.Core;

/// <summary>
/// Identity of the mod. These values feed the MelonLoader <c>MelonInfo</c> attribute,
/// so they must be compile-time constants.
/// </summary>
public static class ModInfo
{
    /// <summary>Mod name as shown by MelonLoader (also used as the log prefix).</summary>
    public const string Name = "TakeItOrLeaveIt";

    /// <summary>Developer / author.</summary>
    public const string Author = "T-h07";

    /// <summary>
    /// Mod version (release candidates carry an -rc.N suffix). Must equal the <c>InformationalVersion</c> in Directory.Build.props
    /// (enforced by a unit test).
    /// </summary>
    public const string Version = "1.0.0-rc.2";

    /// <summary>Current development phase, reported in diagnostics.</summary>
    public const string Phase = "08 - Release stabilization";

    /// <summary>Message logged once initialization completes successfully.</summary>
    public static string InitializedMessage => $"Version {Version} initialized successfully.";
}
