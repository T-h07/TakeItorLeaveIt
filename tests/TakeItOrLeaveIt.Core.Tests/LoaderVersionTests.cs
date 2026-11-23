using TakeItOrLeaveIt.Core.Compatibility;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

public class LoaderVersionTests
{
    [Theory]
    [InlineData("0.7.3", 0, 7, 3)]
    [InlineData("v0.7.3", 0, 7, 3)]
    [InlineData("0.7.3-ci.1234", 0, 7, 3)]
    [InlineData("0.7.3 Open-Beta", 0, 7, 3)]
    [InlineData("0.7.3.0", 0, 7, 3)]
    [InlineData("0.7", 0, 7, 0)]
    [InlineData("  0.6.6  ", 0, 6, 6)]
    public void Parses_common_loader_version_formats(string text, int major, int minor, int patch)
    {
        Assert.True(LoaderVersion.TryParse(text, out var v));
        Assert.Equal(new LoaderVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("seven")]
    [InlineData("7")]
    [InlineData("1.2.3.4.5")]
    public void Rejects_invalid_versions(string? text)
    {
        Assert.False(LoaderVersion.TryParse(text, out _));
    }

    [Fact]
    public void Compares_numerically_not_lexically()
    {
        Assert.True(new LoaderVersion(0, 10, 0) > new LoaderVersion(0, 7, 3));
        Assert.True(new LoaderVersion(0, 7, 1) < new LoaderVersion(0, 7, 3));
        Assert.True(new LoaderVersion(1, 0, 0) >= new LoaderVersion(0, 99, 99));
    }

    [Fact]
    public void Keeps_original_text()
    {
        LoaderVersion.TryParse("0.7.3-ci.9", out var v);
        Assert.Equal("0.7.3-ci.9", v.Original);
        Assert.Equal("0.7.3", v.ToString());
    }
}
