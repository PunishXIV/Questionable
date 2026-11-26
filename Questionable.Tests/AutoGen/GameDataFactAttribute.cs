using Xunit;

namespace Questionable.Tests.AutoGen;

public sealed class GameDataFactAttribute : FactAttribute
{
    public GameDataFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(GameDataFixture.GamePathEnvVar)) &&
            GameDataFixture.FindInstalledSqPackDirectory() == null)
            Skip = $"No FFXIV installation found; set {GameDataFixture.GamePathEnvVar} to run this test.";
    }
}
