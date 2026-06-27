using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FishTests
{
  private readonly Fish.Factory _factory = new();

  // --- helpers ---

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

  // --- CreateAllTasks ---

  [Fact]
  public void NonFishStep_CreatesNoTasks()
  {
    var step = new QuestStep { InteractionType = EInteractionType.Gather };
    var (quest, sequence, fishStep) = QuestTestData.FactoryContext(new QuestId(1), 1, step);

    var tasks = _factory.CreateAllTasks(quest, sequence, fishStep).ToList();

    Assert.Empty(tasks);
  }

  [Fact]
  public void FishStep_WithSingleItem_CreatesUnmountAndFishTask()
  {
    var item = Item(4874, 3);
    var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(1109), 1, FishStep(itemsToGather: [item]));

    var tasks = _factory.CreateAllTasks(quest, sequence, step).ToList();

    Assert.Equal(2, tasks.Count);
    Assert.IsType<Mount.UnmountTask>(tasks[0]);

    var fishTask = Assert.IsType<Fish.FishTask>(tasks[1]);
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

    Assert.Equal(4, tasks.Count);
    Assert.IsType<Mount.UnmountTask>(tasks[0]);

    var fishTasks = tasks.Skip(1).Cast<Fish.FishTask>().ToList();
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

    Assert.Equal(2, tasks.Count);
    Assert.IsType<Mount.UnmountTask>(tasks[0]);

    var fishTask = Assert.IsType<Fish.FishTask>(tasks[1]);
    Assert.Same(quest, fishTask.Quest);
    Assert.Null(fishTask.GatheredItem);
    Assert.True(fishTask.HasCompletionQuestVariablesFlags);
    Assert.Equal(completionFlags, fishTask.CompletionQuestVariablesFlags);
    Assert.Equal("Fish*", fishTask.ToString());
  }
}
