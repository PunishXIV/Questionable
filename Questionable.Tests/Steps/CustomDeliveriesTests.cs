using Questionable.Functions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CustomDeliveriesTests
{
    [Theory]
    [InlineData(6, 540, 1080, 180, 3)]
    [InlineData(2, 540, 1080, 180, 2)]
    [InlineData(6, 0, 0, 180, 6)]
    [InlineData(6, 100, 1080, 0, 6)]
    public void TurnInsLeft_StopsAtNextRank(int left, int current, int needed, int perTurnIn, int expected) =>
        Assert.Equal(expected, CustomDeliveries.TurnInsLeft(left, current, needed, perTurnIn));

    [Fact]
    public void EstimateScrips_StopsAtWeeklyLimit()
    {
        Dictionary<byte, int> total = CustomDeliveries.EstimateScrips(
        [
            (6, [new ScripReward(2, 100), new ScripReward(6, 50)]),
            (6, [new ScripReward(4, 80)]),
        ], weeklyLeft: 8);

        Assert.Equal(600, total[2]);
        Assert.Equal(300, total[6]);
        Assert.Equal(160, total[4]);
    }

    [Fact]
    public void ScripGain_CountsWhatTheCapLoses() =>
        Assert.Equal(200, new ScripGain("Purple Crafters' Scrip", 600, 3600, 4000).Lost);
}
