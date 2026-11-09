using Questionable.Controller.Steps.Interactions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class InteractTests
{
    [Fact]
    public void Mounted_UnmountsFirst()
    {
        Assert.True(Interact.ShouldUnmount(mounted: true, isGatheringPoint: false, tries: 0));
    }

    [Fact]
    public void OnFoot_InteractsAtOnce()
    {
        Assert.False(Interact.ShouldUnmount(mounted: false, isGatheringPoint: false, tries: 0));
    }

    [Fact]
    public void GatheringPoint_KeepsMount()
    {
        Assert.False(Interact.ShouldUnmount(mounted: true, isGatheringPoint: true, tries: 0));
    }

    [Fact]
    public void StuckOnMount_GivesUp()
    {
        Assert.True(Interact.ShouldUnmount(mounted: true, isGatheringPoint: false, Interact.MaxUnmountTries - 1));
        Assert.False(Interact.ShouldUnmount(mounted: true, isGatheringPoint: false, Interact.MaxUnmountTries));
    }
}
