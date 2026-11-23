namespace TakeItOrLeaveIt.Game.Patching;

/// <summary>
/// A cohesive group of Harmony patches for one game subsystem (e.g. "Customer handover").
/// Future phases implement this for each verified patch point in docs/PATCH_MAP.md.
/// </summary>
internal interface IPatchModule
{
    /// <summary>Short human-readable name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Applies the module's patches. Must throw if a required target method cannot be found,
    /// so the <see cref="PatchManager"/> can roll back instead of leaving a half-patched subsystem.
    /// </summary>
    void Apply(HarmonyLib.Harmony harmony);
}
