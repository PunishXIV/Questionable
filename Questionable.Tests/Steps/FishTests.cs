using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FishTests
{
    private static QuestStep Buy() => new() { InteractionType = EInteractionType.PurchaseItem, ItemId = 2591 };
    private static QuestStep Walk() => new() { InteractionType = EInteractionType.WalkTo };
    private static QuestStep Talk() => new() { InteractionType = EInteractionType.Interact };

    private static QuestStep Fishing(uint fishId) => new()
    {
        InteractionType = EInteractionType.Fish,
        ItemsToGather = [new GatheredItem { ItemId = fishId, ItemCount = 5 }],
    };

    [Fact]
    public void BaitAndWalk_LeadToFish()
    {
        QuestStep buy = Buy(), walk = Walk();
        var sequence = QuestTestData.Seq(4, [buy, walk, Fishing(4882), Talk()]);

        Assert.Equal(4882u, Assert.Single(Fish.FishAhead(sequence, buy)).ItemId);
        Assert.Equal(4882u, Assert.Single(Fish.FishAhead(sequence, walk)).ItemId);
    }

    [Fact]
    public void BackToBackFishing_AllCount()
    {
        QuestStep buy = Buy();
        var sequence = QuestTestData.Seq(4, [buy, Fishing(1), Fishing(2), Walk(), Fishing(3)]);

        Assert.Equal([1u, 2u], Fish.FishAhead(sequence, buy).Select(x => x.ItemId));
    }

    [Fact]
    public void TalkBetween_NoFishAhead()
    {
        QuestStep buy = Buy();
        var sequence = QuestTestData.Seq(4, [buy, Talk(), Fishing(1)]);

        Assert.Empty(Fish.FishAhead(sequence, buy));
    }

    [Fact]
    public void TurnIn_NoFishAhead()
    {
        QuestStep talk = Talk();
        var sequence = QuestTestData.Seq(4, [talk, Fishing(1)]);

        Assert.Empty(Fish.FishAhead(sequence, talk));
    }
}
