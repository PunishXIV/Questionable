using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CityAethernetEntryTests
{
    private static QuestSequence Sequence(params uint[] territories) =>
        QuestTestData.Seq(1, [.. territories.Select(t => new QuestStep { TerritoryId = t })]);

    [Theory]
    [InlineData(128u, 129u)]
    [InlineData(131u, 130u)]
    [InlineData(133u, 132u)]
    [InlineData(419u, 418u)]
    public void NeighbourIsTheOtherHalfOfTheCity(uint territory, uint neighbour) =>
        Assert.Equal(neighbour, AetheryteShortcut.CityNeighbourTerritory(territory));

    [Fact]
    public void EntersByAethernetWhenTheSequenceStaysInThisPart()
    {
        QuestSequence sequence = Sequence(128, 128);

        Assert.True(AetheryteShortcut.ShouldEnterCityByAethernet(sequence, sequence.Steps[0], currentTerritory: 135));
    }

    [Fact]
    public void WalksWhenTheSequenceAlsoUsesTheNeighbouringPart()
    {
        QuestSequence sequence = Sequence(129, 128);

        Assert.False(AetheryteShortcut.ShouldEnterCityByAethernet(sequence, sequence.Steps[1], currentTerritory: 135));
    }

    [Fact]
    public void DoesNothingWhenAlreadyThereOrTheStepHasItsOwnAethernet()
    {
        QuestSequence sequence = Sequence(128);
        QuestStep step = sequence.Steps[0];

        Assert.False(AetheryteShortcut.ShouldEnterCityByAethernet(sequence, step, currentTerritory: 128));
        step.AethernetShortcut = new Questionable.Model.Common.AethernetShortcut();
        Assert.False(AetheryteShortcut.ShouldEnterCityByAethernet(sequence, step, currentTerritory: 135));
    }
}
