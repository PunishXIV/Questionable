using Questionable.Controller.Steps.Interactions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CaptureTests
{
    [Theory]
    [InlineData(false, 0.5f, 0.6f, 2, 2, true)]
    [InlineData(false, 0.9f, 0.6f, 2, 2, false)]
    [InlineData(false, 0.9f, 1f, 2, 2, true)]
    [InlineData(true, 0.5f, 0.6f, 2, 2, false)]
    [InlineData(false, 0.5f, 0.6f, 3, 2, false)]
    public void ShouldCapture_ChecksHpLevelAndUse(bool used, float hp, float threshold, byte target, short player,
        bool expected) =>
        Assert.Equal(expected, Capture.Executor.ShouldCapture(used, hp, threshold, target, player));
}
