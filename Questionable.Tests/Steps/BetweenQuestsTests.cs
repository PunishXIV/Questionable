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

    // Accept All queue of 1, 2, 3: detours before the first accept and after the last, not in between.
    [Theory]
    [InlineData(new ushort[0], new ushort[] { 1, 2, 3 }, false)]
    [InlineData(new ushort[] { 1 }, new ushort[] { 2, 3 }, true)]
    [InlineData(new ushort[] { 1, 2, 3 }, new ushort[0], false)]
    [InlineData(new ushort[] { 1 }, new ushort[0], false)]
    public void AcceptAll_DetoursOnlyAtEnds(ushort[] accepted, ushort[] ready, bool underway)
    {
        ElementId[] queue = [new QuestId(1), new QuestId(2), new QuestId(3)];
        Assert.Equal(underway, BetweenQuests.IsAcceptAllUnderway(queue,
            x => accepted.Contains(((QuestId)x).Value), x => ready.Contains(((QuestId)x).Value)));
    }
}
