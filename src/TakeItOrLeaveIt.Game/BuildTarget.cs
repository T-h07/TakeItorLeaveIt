using TakeItOrLeaveIt.Core.Environment;

namespace TakeItOrLeaveIt.Game;

/// <summary>Which Unity scripting backend this assembly was compiled for.</summary>
internal static class BuildTarget
{
#if IL2CPP_BUILD
    public const GameRuntime Runtime = GameRuntime.Il2Cpp;
#elif MONO_BUILD
    public const GameRuntime Runtime = GameRuntime.Mono;
#else
#error Define IL2CPP_BUILD or MONO_BUILD (set by the project file per target framework).
#endif
}
