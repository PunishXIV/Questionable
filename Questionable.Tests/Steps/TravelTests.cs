using Questionable.Controller.Steps.Movement;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TravelTests
{
    [Theory]
    [InlineData(true, 5, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 3, false)]
    [InlineData(false, 4.5f, true)]
    public void KeepWalking_StaysNearNpc(bool walking, float gap, bool expected) =>
        Assert.Equal(expected, Travel.Executor.KeepWalking(walking, gap));
}
