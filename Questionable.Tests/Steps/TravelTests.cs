using Questionable.Controller.Steps.Movement;
using Questionable.Model.Questing;
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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void KeepsRoute_OnlyWhileMtrRuns(bool routeOpen, bool pathing, bool expected) =>
        Assert.Equal(expected, Travel.Executor.KeepsRoute(routeOpen, pathing));

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 14, true)]
    [InlineData(true, 15, false)]
    [InlineData(false, 0, false)]
    public void OldRoute_WaitsUpTo15Seconds(bool pathing, int seconds, bool expected) =>
        Assert.Equal(expected, Travel.Executor.WaitsForOldRoute(pathing, TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(EInteractionType.WalkTo, false, true)]
    [InlineData(EInteractionType.WaitForNpcAtPosition, false, true)]
    [InlineData(EInteractionType.WalkTo, null, false)]
    [InlineData(EInteractionType.AcceptQuest, false, false)]
    [InlineData(EInteractionType.Interact, false, false)]
    public void Follows_OnlyNoMountFollowSteps(EInteractionType type, bool? mount, bool expected) =>
        Assert.Equal(expected, Travel.Follows(new QuestStep { InteractionType = type, Mount = mount }));
}
