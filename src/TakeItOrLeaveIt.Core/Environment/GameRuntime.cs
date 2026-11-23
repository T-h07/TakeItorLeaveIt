namespace TakeItOrLeaveIt.Core.Environment;

/// <summary>Unity scripting backend used by a Schedule I installation.</summary>
public enum GameRuntime
{
    /// <summary>Could not be determined from the available evidence.</summary>
    Unknown = 0,

    /// <summary>Mono backend: managed game code in <c>&lt;Game&gt;_Data/Managed/Assembly-CSharp.dll</c>.</summary>
    Mono = 1,

    /// <summary>IL2CPP backend: native <c>GameAssembly.dll</c> plus <c>global-metadata.dat</c>.</summary>
    Il2Cpp = 2,
}
