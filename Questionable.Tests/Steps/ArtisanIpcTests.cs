using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ArtisanIpcTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void StopFromQueue_OnlyNewStops(bool before, bool after, bool expected) =>
        Assert.Equal(expected, ArtisanIpc.StopFromQueue(before, after));
}
