using System.Numerics;
using Questionable.Controller.Steps.Movement;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TravelTests
{
    private static readonly Vector3 Goal = new(100, 0, 0);

    [Theory]
    [InlineData(true, 0, 10, true)]
    [InlineData(true, 7, 10, false)]
    [InlineData(true, 15, 10, false)]
    [InlineData(false, 5, 10, false)]
    [InlineData(false, 3, 10, true)]
    public void KeepWalking_StaysBehindNpc(bool walking, float player, float npc, bool expected) =>
        Assert.Equal(expected, Travel.Executor.KeepWalking(walking, new Vector3(player, 0, 0), new Vector3(npc, 0, 0), Goal));
}
