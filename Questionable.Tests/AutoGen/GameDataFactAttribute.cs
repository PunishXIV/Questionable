// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu

using Xunit;

namespace Questionable.Tests.AutoGen;

public sealed class GameDataFactAttribute : FactAttribute
{
    public GameDataFactAttribute()
        : this(Environment.GetEnvironmentVariable(GameDataFixture.GamePathEnvVar),
            GameDataFixture.FindInstalledSqPackDirectory)
    {
    }

    internal GameDataFactAttribute(string? configured, Func<string?> discover)
    {
        // An explicit path must reach fixture initialization so invalid paths fail instead of being skipped.
        if (string.IsNullOrEmpty(configured) && discover() == null)
            Skip = $"No FFXIV installation found; set {GameDataFixture.GamePathEnvVar} to run this test.";
    }
}
