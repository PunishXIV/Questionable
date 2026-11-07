using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftingRequirementTests
{
    [Fact]
    public void PartialHighQuality_CraftsFullCount()
    {
        var task = CreateCraftTask(new QuestId(648), new CraftingItem(4599, 1, EItemQuality.Any));
        var target = Assert.Single(Craft.GetItemTargets(task));
        int matchingItems = Craft.CountMatchingItems(target.Quality, normal: 9, high: 1, collectable: 0);

        Assert.False(Craft.AreItemsSatisfied([target], _ => matchingItems));
        Assert.Equal(target, SkipCondition.GetItemTarget(task.Quest.Id, task.Step));
        Assert.Equal(2, Craft.GetCraftCount(target.ItemCount - matchingItems, yield: 1));
    }

    [Theory]
    [InlineData(648, 4599u, 3, EItemQuality.HQ)]
    [InlineData(257, 4640u, 2, EItemQuality.Any)]
    [InlineData(268, 4712u, 1, EItemQuality.HQ)]
    public void QuestItems_ContainVerifiedRequirements(ushort questId, uint itemId, int itemCount, EItemQuality quality)
    {
        var items = QuestCraftingRequirements.GetItems(new QuestId(questId));

        Assert.Equal(new CraftingItem(itemId, itemCount, quality), Assert.Single(items, item => item.ItemId == itemId));
    }

    [Theory]
    [InlineData(648, 4599u, 3, EItemQuality.HQ)]
    [InlineData(257, 4640u, 2, EItemQuality.Any)]
    [InlineData(268, 4712u, 1, EItemQuality.HQ)]
    public void ExplicitTarget_UsesCountAndQuality(ushort questId, uint itemId, int itemCount, EItemQuality quality)
    {
        var id = new QuestId(questId);
        var requested = new CraftingItem(itemId, 1, EItemQuality.Any);
        var expected = new CraftingItem(itemId, itemCount, quality);

        Assert.Equal(expected, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(expected, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Theory]
    [InlineData(106, 4304u)]
    [InlineData(139, 2219u)]
    [InlineData(275, 2226u)]
    [InlineData(293, 2341u)]
    [InlineData(578, 4564u)]
    [InlineData(587, 3274u)]
    [InlineData(611, 4305u)]
    public void LevelFiveTargets_KeepThreeItems(ushort questId, uint itemId)
    {
        var id = new QuestId(questId);
        var requested = new CraftingItem(itemId, 3, EItemQuality.Any);

        Assert.Equal(requested, Assert.Single(QuestCraftingRequirements.GetItems(id)));
        Assert.Equal(requested, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(requested, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Fact]
    public void Culinarian268_RequiresAnyQualityNatron()
    {
        var id = new QuestId(268);
        var requested = new CraftingItem(5522, 1, EItemQuality.Any);

        Assert.Equal(requested, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(requested, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Theory]
    [InlineData(648, 42u)]
    [InlineData(65000, 4599u)]
    public void UnknownCustomTarget_IsUnchanged(ushort questId, uint itemId)
    {
        var id = new QuestId(questId);
        var requested = new CraftingItem(itemId, 5, EItemQuality.NQ);

        Assert.Equal(requested, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(requested, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Fact]
    public void LargerCount_KeptWithRequiredQuality()
    {
        var id = new QuestId(648);
        var requested = new CraftingItem(4599, 5, EItemQuality.Any);
        var expected = new CraftingItem(4599, 5, EItemQuality.HQ);

        Assert.Equal(expected, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(expected, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Fact]
    public void AnyQualityTarget_KeepsStricterQuality()
    {
        var id = new QuestId(257);
        var requested = new CraftingItem(4640, 1, EItemQuality.HQ);
        var expected = new CraftingItem(4640, 2, EItemQuality.HQ);

        Assert.Equal(expected, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(expected, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Fact]
    public void NonQuestSameId_GetsNoRequirements()
    {
        var id = new SatisfactionSupplyNpcId(648);
        var requested = new CraftingItem(4599, 1, EItemQuality.Any);

        Assert.Empty(QuestCraftingRequirements.GetItems(id));
        Assert.Equal(requested, QuestCraftingRequirements.ResolveItem(id, requested));
        Assert.Equal(requested, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
    }

    [Theory]
    [InlineData(3101, 22724u)]
    [InlineData(3100, 22723u)]
    [InlineData(3129, 22745u)]
    [InlineData(3123, 22741u)]
    [InlineData(3120, 22739u)]
    [InlineData(3112, 22733u)]
    [InlineData(3126, 22743u)]
    public void NamazuTargets_RequireOneHighQualityItem(ushort questId, uint itemId)
    {
        var id = new QuestId(questId);
        var requested = new CraftingItem(itemId, 1, EItemQuality.Any);
        var expected = new CraftingItem(itemId, 1, EItemQuality.HQ);

        var target = QuestCraftingRequirements.ResolveItem(id, requested);

        Assert.Equal(expected, Assert.Single(QuestCraftingRequirements.GetItems(id)));
        Assert.Equal(expected, target);
        Assert.Equal(expected, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
        Assert.False(Craft.AreItemsSatisfied([target], item => Craft.CountMatchingItems(item.Quality, 1, 0, 0)));
        Assert.True(Craft.AreItemsSatisfied([target], item => Craft.CountMatchingItems(item.Quality, 0, 1, 0)));
    }

    [Theory]
    [InlineData(2670u)]
    [InlineData(3322u)]
    public void Weaver590_AcceptsOneNormalQualityItem(uint itemId)
    {
        var id = new QuestId(590);
        var requested = new CraftingItem(itemId, 1, EItemQuality.Any);

        var target = QuestCraftingRequirements.ResolveItem(id, requested);

        Assert.Equal(requested, Assert.Single(QuestCraftingRequirements.GetItems(id), item => item.ItemId == itemId));
        Assert.Equal(requested, target);
        Assert.Equal(requested, Assert.Single(Craft.GetItemTargets(CreateCraftTask(id, requested))));
        Assert.True(Craft.AreItemsSatisfied([target], item => Craft.CountMatchingItems(item.Quality, 1, 0, 0)));
    }

    [Theory]
    [InlineData(EInteractionType.Craft, 3, EItemQuality.HQ)]
    [InlineData(EInteractionType.PurchaseItem, 1, EItemQuality.Any)]
    public void SkipItemTarget_RequirementsOnlyForCrafts(EInteractionType interactionType, int itemCount, EItemQuality quality)
    {
        var step = new QuestStep { InteractionType = interactionType, ItemId = 4599, ItemCount = 1, ItemQuality = EItemQuality.Any };

        var target = SkipCondition.GetItemTarget(new QuestId(648), step);

        Assert.Equal(new CraftingItem(4599, itemCount, quality), target);
    }

    [Fact]
    public void SkipItemTarget_WithoutItemIdIsNull()
    {
        var step = new QuestStep { InteractionType = EInteractionType.Craft, ItemCount = 1, ItemQuality = EItemQuality.Any };

        Assert.Null(SkipCondition.GetItemTarget(new QuestId(648), step));
    }

    private static Craft.Task CreateCraftTask(ElementId questId, CraftingItem requested) =>
        new(QuestTestData.CreateQuest(questId), 1,
            new QuestStep
            {
                InteractionType = EInteractionType.Craft,
                ItemId = requested.ItemId,
                ItemCount = requested.ItemCount,
                ItemQuality = requested.Quality,
            });
}
