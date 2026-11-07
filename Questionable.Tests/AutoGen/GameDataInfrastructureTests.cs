using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.AutoGen;

public sealed class GameDataInfrastructureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOptionalInstall_IsSkipped(string? configured)
    {
        var fact = new GameDataFactAttribute(configured, () => null);

        Assert.Contains(GameDataFixture.GamePathEnvVar, fact.Skip);
        Assert.Null(GameDataFixture.ResolveSqPackDirectory(configured, () => null));
    }

    [Fact]
    public void DiscoveredInstallation_IsNotSkipped()
    {
        var fact = new GameDataFactAttribute(null, () => "installed/game/sqpack");

        Assert.Null(fact.Skip);
        Assert.Equal("installed/game/sqpack",
            GameDataFixture.ResolveSqPackDirectory(null, () => "installed/game/sqpack"));
    }

    [Fact]
    public void InvalidExplicitInstall_FailsOutright()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"questionable-missing-{Guid.NewGuid():N}");
        string? Discover() => throw new InvalidOperationException("Explicit paths must not fall back.");

        Assert.Null(new GameDataFactAttribute(missing, Discover).Skip);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameDataFixture.ResolveSqPackDirectory(missing, Discover));
        Assert.Contains(GameDataFixture.GamePathEnvVar, exception.Message);
        Assert.Contains(missing, exception.Message);
    }

    [Fact]
    public void FailedGeneration_CountsInAccuracy()
    {
        QuestRoot hand = TwoTargetQuest();
        List<string> mismatches = [];
        var success = QuestPathGenerationAccuracyTests.CompareQuest(hand, hand, "working", mismatches);
        var failure = QuestPathGenerationAccuracyTests.CompareQuest(hand, null, "broken", mismatches);

        Assert.Equal((2, 2, true), success);
        Assert.Equal((2, 0, false), failure);
        Assert.Equal(0.5, (double)(success.Matched + failure.Matched) / (success.Compared + failure.Compared));
        Assert.Contains(mismatches, x => x.Contains("broken: generation returned no path", StringComparison.Ordinal));
    }

    [Fact]
    public void PartialGeneration_FailsQuestAgreement()
    {
        QuestRoot hand = TwoTargetQuest();
        QuestRoot generated = new() { QuestSequence = [hand.QuestSequence[0]] };

        Assert.Equal((2, 1, false),
            QuestPathGenerationAccuracyTests.CompareQuest(hand, generated, "partial", []));
    }

    [Fact]
    public void UntargetedFailure_FailsQuestAgreement()
    {
        Assert.Equal((0, 0, false),
            QuestPathGenerationAccuracyTests.CompareQuest(new QuestRoot(), null, "empty", []));
    }

    private static QuestRoot TwoTargetQuest() => new()
    {
        QuestSequence =
        [
            new() { Sequence = 0, Steps = [new() { DataId = 100 }] },
            new() { Sequence = 1, Steps = [new() { DataId = 200 }] },
        ],
    };
}
