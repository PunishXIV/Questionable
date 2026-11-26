using Questionable.Controller.Steps.Interactions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class InteractTests
{
    [Fact]
    public void Retry_WaitsWhileBusyThenGivesUp()
    {
        DateTime start = new(2026, 1, 1);
        InteractRetry retry = new(start);

        Assert.False(retry.Due(start.AddSeconds(1), occupied: false));
        Assert.False(retry.Due(start.AddSeconds(10), occupied: true));
        Assert.False(retry.Due(start.AddSeconds(11), occupied: false));
        for (int i = 1; i <= InteractRetry.MaxTries; i++)
            Assert.True(retry.Due(start.AddSeconds(10 + 4 * i), occupied: false));
        Assert.Throws<Questionable.Controller.Steps.TaskException>(() => retry.Due(start.AddMinutes(5), occupied: false));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(4, 480)]
    [InlineData(9, 480)]
    public void DetourBackoff_DoublesUpToEightHours(int failures, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), Questionable.Controller.Steps.Shared.BetweenQuests.Backoff.Delay(failures));

    [Fact]
    public void DetourBackoff_PerCharacter()
    {
        ulong character = 1;
        var backoff = new Questionable.Controller.Steps.Shared.BetweenQuests.Backoff(() => character);
        backoff.Failed();
        Assert.True(backoff.Waiting);
        character = 2;
        Assert.False(backoff.Waiting);
    }

    [Theory]
    [InlineData(Questionable.Model.Questing.EInteractionType.CompleteQuest, true, true)]
    [InlineData(Questionable.Model.Questing.EInteractionType.CompleteQuest, false, false)]
    [InlineData(Questionable.Model.Questing.EInteractionType.AcceptQuest, true, false)]
    public void PreventCompletion_SkipsTurnInExtras(Questionable.Model.Questing.EInteractionType type, bool prevent, bool expected)
    {
        Configuration configuration = new();
        configuration.Advanced.PreventQuestCompletion = prevent;

        Assert.Equal(expected, Say.IsPreventedTurnIn(new() { InteractionType = type }, configuration));
    }

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
