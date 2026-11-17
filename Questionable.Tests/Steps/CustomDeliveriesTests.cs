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
}
