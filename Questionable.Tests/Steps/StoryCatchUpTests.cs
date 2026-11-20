using Questionable.Controller.Steps.Interactions;
using Questionable.Data;
using Questionable.Model.Common;
using Xunit;
using Accessory = Questionable.Controller.Steps.Interactions.StoryCatchUp.Accessory;

namespace Questionable.Tests.Steps;

public sealed class StoryCatchUpTests
{
    private static TerritoryData.CfcData Duty(uint id, byte level, EContentType type = EContentType.Dungeons) =>
        new(id, $"Duty {id}", 1000 + id, 0, level, type, level);

    private static readonly TerritoryData.CfcData[] Duties =
    [
        Duty(1, 15), Duty(2, 24), Duty(3, 32), Duty(4, 28, EContentType.Trials), Duty(5, 50),
    ];

    [Fact]
    public void Dungeon_HighestAtOrBelowLevel() =>
        Assert.Equal(3u, StoryCatchUp.PickDungeon(Duties, 33, _ => true, _ => true)?.Id);

    [Fact]
    public void Dungeon_SkipsNoSupportOrPath() =>
        Assert.Equal(1u, StoryCatchUp.PickDungeon(Duties, 33, x => x != 3, x => x != 2)?.Id);

    [Fact]
    public void Dungeon_NoneLowEnough() =>
        Assert.Null(StoryCatchUp.PickDungeon(Duties, 10, _ => true, _ => true));

    [Fact]
    public void Accessories_BestPerSlotIfBetter()
    {
        Accessory[] offered = [new(10, 8, 130), new(11, 8, 120), new(20, 9, 100), new(30, 11, 130)];

        Assert.Equal([10u, 30u], StoryCatchUp.PickAccessories(offered, slot => slot == 9 ? 110 : 90));
    }

    [Theory]
    [InlineData(101, 100, false, 0, 0, true)]
    [InlineData(100, 100, true, 450, 300, true)]
    [InlineData(100, 100, false, 450, 300, false)]
    [InlineData(100, 100, true, 300, 300, false)]
    public void Gained_CountsTomestonesForUpgrades(int itemLevel, int lastItemLevel, bool upgradesLeft, int tomestones,
        int lastTomestones, bool expected) =>
        Assert.Equal(expected, StoryCatchUp.Gained(itemLevel, lastItemLevel, upgradesLeft, tomestones, lastTomestones));
}
