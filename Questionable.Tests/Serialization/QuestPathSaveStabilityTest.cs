using System.Text.Json;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class QuestPathSaveStabilityTest
{
    [Theory]
    [ClassData(typeof(EmbeddedQuestLoader))]
    public void SavingAQuestPathProducesIdenticalJson(EmbeddedQuest embedded)
    {
        QuestRoot original;
        using (var stream = embedded.OpenStream())
        {
            original = JsonSerializer.Deserialize<QuestRoot>(stream)
                       ?? throw new Xunit.Sdk.XunitException(
                           $"'{embedded.ManifestName}' deserialized to a null QuestRoot.");
        }

        string once = JsonSerializer.Serialize(original, JsonOptions.Default);
        QuestRoot reloaded = JsonSerializer.Deserialize<QuestRoot>(once)
                             ?? throw new Xunit.Sdk.XunitException(
                                 $"Re-deserializing '{embedded.ManifestName}' returned null.");
        string twice = JsonSerializer.Serialize(reloaded, JsonOptions.Default);

        Assert.Equal(once, twice);
    }
}
