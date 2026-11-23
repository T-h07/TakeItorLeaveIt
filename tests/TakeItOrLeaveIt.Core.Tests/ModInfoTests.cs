using System.Reflection;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

public class ModInfoTests
{
    [Fact]
    public void Identity_matches_project_requirements()
    {
        Assert.Equal("TakeItOrLeaveIt", ModInfo.Name);
        Assert.Equal("T-h07", ModInfo.Author);
        Assert.Equal("0.0.1-dev", ModInfo.Version);
    }

    [Fact]
    public void ModInfo_version_matches_assembly_informational_version()
    {
        // Guards against bumping Directory.Build.props without updating the MelonInfo constant (or vice versa).
        var informational = typeof(ModInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(ModInfo.Version, informational);
    }

    [Fact]
    public void Initialized_message_contains_version()
    {
        Assert.Equal("Version 0.0.1-dev initialized successfully.", ModInfo.InitializedMessage);
    }
}
