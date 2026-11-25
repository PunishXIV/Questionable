using Questionable.Windows.ConfigComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class PluginRepositoryMatchingTests
{
    [Theory]
    [InlineData("https://puni.sh/api/repository/knightmore?authKey=abc", "https://puni.sh/api/repository/knightmore")]
    [InlineData("https://puni.sh/api/repository/knightmore/", "https://puni.sh/api/repository/knightmore")]
    [InlineData(" HTTPS://PUNI.SH/api/repository/croizat?authKey=abc ", "https://puni.sh/api/repository/croizat")]
    [InlineData("https://love.puni.sh/ment.json", "https://love.puni.sh/ment.json")]
    public void Match_IgnoresQueryCaseAndSlash(string configured, string requested) =>
        Assert.True(PluginConfigComponent.IsSameRepository(configured, requested));

    [Theory]
    [InlineData("https://puni.sh/api/repository/knightmore", "https://puni.sh/api/repository/croizat")]
    [InlineData("https://puni.sh/api/repository/ice?authKey=abc", "https://puni.sh/api/repository/icebox")]
    public void DifferentRepositoriesDoNotMatch(string configured, string requested) =>
        Assert.False(PluginConfigComponent.IsSameRepository(configured, requested));

    [Fact]
    public void KeyedRepositoryIsReusedAsConfigured()
    {
        string[] configured =
        [
            "https://love.puni.sh/ment.json",
            "https://puni.sh/api/repository/knightmore?authKey=abc"
        ];

        Assert.Equal("https://puni.sh/api/repository/knightmore?authKey=abc",
            PluginConfigComponent.FindMatchingRepository(configured, "https://puni.sh/api/repository/knightmore"));
        Assert.Null(PluginConfigComponent.FindMatchingRepository(configured, "https://puni.sh/api/repository/wah"));
    }
}
