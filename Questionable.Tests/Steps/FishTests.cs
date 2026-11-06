using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FishTests
{
    private readonly IFeeshIpc _feeshIpc = Substitute.For<IFeeshIpc>();
    private readonly Fish.Factory _factory;

    public FishTests()
    {
        _feeshIpc.IsAvailable().Returns(true);
        _feeshIpc.CanCatch(Arg.Any<uint>()).Returns(true);
        _factory = new Fish.Factory();
    }

    private static QuestStep FishStep(
        List<GatheredItem>? itemsToGather = null,
        List<QuestWorkValue?>? completionQuestVariablesFlags = null) =>
        new()
        {
            InteractionType = EInteractionType.Fish,
            ItemsToGather = itemsToGather ?? [],
            CompletionQuestVariablesFlags = completionQuestVariablesFlags ?? [],
        };

    private static List<QuestWorkValue?> CompletionFlags1514() =>
        [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null];

    private static GatheredItem Item(uint itemId, int itemCount, ushort collectability = 0) =>
        new() { ItemId = itemId, ItemCount = itemCount, Collectability = collectability };

    [Fact]
    public void HookTypeFilter_DeserializesTrueShorthand()
    {
        const string json = @"{""Normal"": true, ""Double"": { ""Strong"": true }}";

        var hookType = JsonNode.Parse(json)!.Deserialize<HookType>();

        Assert.NotNull(hookType!.Normal);
        Assert.True(hookType.Normal.Value.IsAllHooksets);
        Assert.NotNull(hookType.Double);
        Assert.True(hookType.Double.Value.Hookset!.Strong);
        Assert.Null(hookType.Triple);
    }

    [Fact]
    public void GatheredItem_DeserializesFishingOptionsFromQuestJson()
    {
        const string json = @"{""ItemId"": 12713, ""ItemCount"": 3, ""FishingOptions"": {""BaitId"": 28634, ""HookType"": {""Normal"": {""Weak"": true } } } }";

        var item = JsonNode.Parse(json)!.Deserialize<GatheredItem>();

        Assert.Equal(12713u, item!.ItemId);
        Assert.Equal(3, item.ItemCount);
        Assert.NotNull(item.FishingOptions);
        Assert.Equal(28634u, item.FishingOptions.BaitId);
        Assert.NotNull(item.FishingOptions.HookType);
        Assert.NotNull(item.FishingOptions.HookType.Normal);
        Assert.False(item.FishingOptions.HookType.Normal.Value.IsAllHooksets);
        Assert.True(item.FishingOptions.HookType.Normal.Value.Hookset!.Weak);
        Assert.Null(item.FishingOptions.HookType.Normal.Value.Hookset!.Strong);
        Assert.Null(item.FishingOptions.HookType.Double);
        Assert.Null(item.FishingOptions.HookType.Triple);
    }

    [Fact]
    public void FishStep_DeserializesItemsToGatherIntoFactoryTasks()
    {
        const string json = @"{""InteractionType"": ""Fish"", ""TerritoryId"": 397, ""ItemsToGather"": [" +
            @"{ ""ItemId"": 12713, ""ItemCount"": 3, ""FishingOptions"": {""BaitId"": 28634, ""HookType"": {""Double"": {""Strong"": true } } } } ] } ";

        var step = JsonNode.Parse(json)!.Deserialize<QuestStep>();
        var (quest, sequence, _) = QuestTestData.FactoryContext(new QuestId(2086), 255, step!);

        var fishTask = _factory.CreateAllTasks(quest, sequence, step!).OfType<Fish.FishTask>().Single();

        Assert.Equal(12713u, fishTask.GatheredItem!.ItemId);
        Assert.Equal(3, fishTask.GatheredItem.ItemCount);
        Assert.Equal(28634u, fishTask.GatheredItem.FishingOptions!.BaitId);
        Assert.NotNull(fishTask.GatheredItem.FishingOptions.HookType);
        Assert.Null(fishTask.GatheredItem.FishingOptions.HookType.Normal);
        Assert.NotNull(fishTask.GatheredItem.FishingOptions.HookType.Double);
        Assert.False(fishTask.GatheredItem.FishingOptions.HookType.Double.Value.IsAllHooksets);
        Assert.Null(fishTask.GatheredItem.FishingOptions.HookType.Double.Value.Hookset!.Weak);
        Assert.True(fishTask.GatheredItem.FishingOptions.HookType.Double.Value.Hookset!.Strong);
        Assert.Null(fishTask.GatheredItem.FishingOptions.HookType.Triple);
    }

    [Fact]
    public void FishStepWithoutFeesh_KeepsItemCheck()
    {
        _feeshIpc.IsAvailable().Returns(false);
        var item = Item(4874, 3);
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(1109), 1, FishStep(itemsToGather: [item]));

        var tasks = _factory.CreateAllTasks(quest, sequence, step).ToList();

        Assert.IsType<Fish.FishTask>(Assert.Single(tasks));
    }

    [Fact]
    public void NonFishStep_CreatesNoTasks()
    {
        var step = new QuestStep { InteractionType = EInteractionType.Gather };
        var (quest, sequence, fishStep) = QuestTestData.FactoryContext(new QuestId(1), 1, step);

        var tasks = _factory.CreateAllTasks(quest, sequence, fishStep).ToList();

        Assert.Empty(tasks);
    }

    [Fact]
    public void FishStep_WithSingleItem_DefersPreparationUntilObjectiveAndProviderChecks()
    {
        var item = Item(4874, 3);
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(1109), 1, FishStep(itemsToGather: [item]));

        var tasks = _factory.CreateAllTasks(quest, sequence, step).ToList();

        var fishTask = Assert.IsType<Fish.FishTask>(Assert.Single(tasks));
        Assert.Same(quest, fishTask.Quest);
        Assert.Same(item, fishTask.GatheredItem);
        Assert.False(fishTask.HasCompletionQuestVariablesFlags);
    }

    [Fact]
    public void FishStep_WithMultipleItems_CreatesFishTaskPerItem()
    {
        var firstItem = Item(4874, 3);
        var secondItem = Item(2586, 1);
        var thirdItem = Item(5467, 5, collectability: 350);
        var (quest, sequence, step) = QuestTestData.FactoryContext(
            new QuestId(1),
            1,
            FishStep(itemsToGather: [firstItem, secondItem, thirdItem]));

        var tasks = _factory.CreateAllTasks(quest, sequence, step).ToList();

        var fishTasks = tasks.Cast<Fish.FishTask>().ToList();
        Assert.Equal(3, fishTasks.Count);
        Assert.Same(firstItem, fishTasks[0].GatheredItem);
        Assert.Same(secondItem, fishTasks[1].GatheredItem);
        Assert.Same(thirdItem, fishTasks[2].GatheredItem);
        Assert.All(fishTasks, task => Assert.Same(quest, task.Quest));
    }

    [Fact]
    public void FishStep_WithoutItemsToGather_CreatesFishTaskWithCompletionFlags()
    {
        var completionFlags = CompletionFlags1514();
        var (quest, sequence, step) = QuestTestData.FactoryContext(
            new QuestId(1514),
            1,
            FishStep(completionQuestVariablesFlags: completionFlags));

        var tasks = _factory.CreateAllTasks(quest, sequence, step).ToList();

        var fishTask = Assert.IsType<Fish.FishTask>(Assert.Single(tasks));
        Assert.Same(quest, fishTask.Quest);
        Assert.Null(fishTask.GatheredItem);
        Assert.True(fishTask.HasCompletionQuestVariablesFlags);
        Assert.Equal(completionFlags, fishTask.CompletionQuestVariablesFlags);
        Assert.Equal("Fish*", fishTask.ToString());
    }

    [Fact]
    public void FeeshCatchableStep_SkipsQuestionableTravel()
    {
        var step = FishStep(itemsToGather: [Item(4874, 3)]);

        Assert.True(Fish.IsHandledByFeesh(step, _feeshIpc));
    }

    [Fact]
    public void CollectableFish_KeepsQuestionableTravelWithoutAskingFeesh()
    {
        var step = FishStep(itemsToGather: [Item(43906, 6, collectability: 433)]);

        Assert.False(Fish.IsHandledByFeesh(step, _feeshIpc));
        _feeshIpc.DidNotReceiveWithAnyArgs().CanCatch(default);
    }

    [Fact]
    public void FishWithoutKnownSpot_KeepsQuestionableTravel()
    {
        _feeshIpc.CanCatch(2001410).Returns(false);
        var step = FishStep(itemsToGather: [Item(4874, 3), Item(2001410, 2)]);

        Assert.False(Fish.IsHandledByFeesh(step, _feeshIpc));
    }

    [Fact]
    public void UnavailableFeeshOrFlagOnlyStep_KeepsQuestionableTravel()
    {
        Assert.False(Fish.IsHandledByFeesh(FishStep(completionQuestVariablesFlags: CompletionFlags1514()), _feeshIpc));

        _feeshIpc.IsAvailable().Returns(false);
        Assert.False(Fish.IsHandledByFeesh(FishStep(itemsToGather: [Item(4874, 3)]), _feeshIpc));
    }

    [Fact]
    public void NonFishStep_IsNeverHandledByFeesh()
    {
        var step = new QuestStep { InteractionType = EInteractionType.Gather, ItemsToGather = [Item(4874, 3)] };

        Assert.False(Fish.IsHandledByFeesh(step, _feeshIpc));
    }
}
