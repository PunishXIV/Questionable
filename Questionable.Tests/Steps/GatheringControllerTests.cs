using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class GatheringControllerTests
{
    [Theory]
    [InlineData(4.9, false, false)]
    [InlineData(5.0, false, true)]
    [InlineData(12.0, true, false)]
    public void IsStalled_FiveMinutesNotGathering(double minutes, bool gathering, bool expected) =>
        Assert.Equal(expected, GatheringController.IsStalled(TimeSpan.FromMinutes(minutes), gathering));
}
