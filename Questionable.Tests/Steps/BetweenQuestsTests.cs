using System.Numerics;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class BetweenQuestsTests
{
    private static readonly Vector3 Giver = new(1, 2, 3);

    [Fact]
    public void AcceptStep_AllowsDetour() =>
        Assert.True(BetweenQuests.IsAcceptStep(new QuestStep(EInteractionType.AcceptQuest, 1, Giver, 1), false));

    [Fact]
    public void AcceptedQuest_NoDetour() =>
        Assert.False(BetweenQuests.IsAcceptStep(new QuestStep(EInteractionType.AcceptQuest, 1, Giver, 1), true));

    [Fact]
    public void NoPosition_NoDetour() =>
        Assert.False(BetweenQuests.IsAcceptStep(new QuestStep(EInteractionType.AcceptQuest, 1, null, 1), false));

    [Fact]
    public void MidQuestStep_NoDetour() =>
        Assert.False(BetweenQuests.IsAcceptStep(new QuestStep(EInteractionType.Interact, 1, Giver, 1), false));
}
