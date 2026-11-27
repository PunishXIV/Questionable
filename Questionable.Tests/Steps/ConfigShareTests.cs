using Questionable.Controller;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ConfigShareTests
{
    [Theory]
    [InlineData("8koSFyMXD27Znpts", 1u, true)]
    [InlineData("8koSFyMXD27Znpt", 1u, false)]
    [InlineData("8koSFyMXD27Znp!s", 1u, false)]
    [InlineData("8koSFyMXD27Znpts", 0u, false)]
    [InlineData("8koSFyMXD27Znpts", 0x80u, false)]
    public void IsValid_CodeAndParts(string code, uint categories, bool expected) =>
        Assert.Equal(expected, ConfigShare.IsValid(code, categories));

    [Fact]
    public void IsDue_OncePerCharacterAndCode()
    {
        var settings = new Configuration.CharacterConfiguration { ShareCode = "8koSFyMXD27Znpts", ShareCategories = 0x7F };
        Assert.False(ConfigShare.IsDue(settings, 1, tried: false));
        settings.ApplyShare = true;
        Assert.True(ConfigShare.IsDue(settings, 1, tried: false));
        Assert.False(ConfigShare.IsDue(settings, 1, tried: true));
        settings.ShareApplied[1] = "8koSFyMXD27Znpts";
        Assert.False(ConfigShare.IsDue(settings, 1, tried: false));
        settings.ShareCode = "AAAAAAAAAAAAAAAA";
        Assert.True(ConfigShare.IsDue(settings, 1, tried: false));
    }
}
