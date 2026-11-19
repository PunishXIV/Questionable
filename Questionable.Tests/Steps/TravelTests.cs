using Questionable.Controller.Steps.Movement;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TravelTests
{
    [Theory]
    [InlineData(true, 3.5f, false, true)]
    [InlineData(true, 3, false, false)]
    [InlineData(false, 4.5f, false, false)]
    [InlineData(false, 5.5f, false, true)]
    [InlineData(true, 2.5f, true, true)]
    [InlineData(false, 2.5f, true, true)]
    [InlineData(false, 2, true, false)]
    public void KeepWalking_StaysNearNpc(bool walking, float gap, bool resting, bool expected) =>
        Assert.Equal(expected, Travel.Executor.KeepWalking(walking, gap, resting));
}
