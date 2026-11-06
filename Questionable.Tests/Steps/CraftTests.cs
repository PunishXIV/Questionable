using ECommons.ExcelServices;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.Domain;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftTests
{
    [Fact]
    public void Factory_CarriesRequestedQualityAndObjectiveIntoTask()
    {
        var (quest, sequence, step) = QuestTestData.FactoryContext(new QuestId(1), 1,
            new QuestStep { InteractionType = EInteractionType.Craft, ItemId = 42, ItemCount = 5, ItemQuality = EItemQuality.HQ });

        var task = new Craft.Factory().CreateAllTasks(quest, sequence, step).OfType<Craft.CraftTask>().Single();

        Assert.True(Craft.HasItemTarget(task));
        Assert.Equal(42u, task.ItemId);
        Assert.Equal(5, task.ItemCount);
        Assert.Equal(EItemQuality.HQ, task.Step.ItemQuality);
    }

    [Theory]
    [InlineData(42u, null)]
    [InlineData(null, 5)]
    [InlineData(0u, 5)]
    [InlineData(42u, 0)]
    [InlineData(42u, -1)]
    public void UnverifiedTarget_StopsBeforeStart(uint? itemId, int? itemCount)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(1)), 1,
            new QuestStep { ItemId = itemId, ItemCount = itemCount });

        Assert.Throws<TaskException>(() => Craft.HasItemTarget(task));
    }

    [Fact]
    public void EmptyTargetWithFlags_KeepsObjective()
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(1)), 1,
            new QuestStep { CompletionQuestVariablesFlags = [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null] });

        Assert.False(Craft.HasItemTarget(task));
        Assert.True(Craft.IsQuestObjectiveMet(task, true, null));
        Assert.False(Craft.IsQuestObjectiveMet(task, false, null));
    }

    [Fact]
    public void UnknownListOnlyQuest_IsAllowedButHasNoObjectiveForEarlySkip()
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(65000)), 255, new QuestStep());

        Assert.False(Craft.HasItemTarget(task));
        Assert.Empty(Craft.GetItemTargets(task));
        Assert.False(Craft.IsQuestObjectiveMet(task, false, null));
        Assert.False(Craft.IsQuestObjectiveMet(task, true, null));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Carpenter139_RequiresThreeShields(int owned, bool expected)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(139)), 255, new QuestStep());

        var targets = Craft.GetItemTargets(task);

        Assert.Equal(new CraftingItem(2219, 3, EItemQuality.Any), Assert.Single(targets));
        Assert.Equal(expected, Craft.AreItemsSatisfied(targets, _ => owned));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    public void Carpenter141_RequiresBothItems(int firstCount, int secondCount, bool expected)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(141)), 2, new QuestStep());
        var counts = new Dictionary<uint, int> { [1823] = firstCount, [1895] = secondCount };

        var targets = Craft.GetItemTargets(task);

        Assert.Collection(targets,
            item => Assert.Equal(new CraftingItem(1823, 1, EItemQuality.Any), item),
            item => Assert.Equal(new CraftingItem(1895, 1, EItemQuality.Any), item));
        Assert.Equal(expected, Craft.AreItemsSatisfied(targets, item => counts[item.ItemId]));
    }

    [Theory]
    [InlineData(12849u, 0, 0, 0, false)]
    [InlineData(12849u, 1, 0, 0, false)]
    [InlineData(12849u, 0, 0, 1, false)]
    [InlineData(12862u, 0, 0, 0, false)]
    [InlineData(12862u, 1, 0, 0, false)]
    [InlineData(12862u, 0, 0, 1, false)]
    [InlineData(12855u, 0, 0, 0, false)]
    [InlineData(12855u, 1, 0, 0, false)]
    [InlineData(12855u, 0, 0, 1, false)]
    [InlineData(12849u, 0, 1, 0, true)]
    public void Culinarian2045_RequiresAllThreeHighQualityDishes(uint changedItemId, int normal, int high, int collectable, bool expected)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(2045)), 255, new QuestStep());

        var targets = Craft.GetItemTargets(task);

        Assert.Equal(3, targets.Count);
        Assert.Contains(new CraftingItem(12849, 1, EItemQuality.HQ), targets);
        Assert.Contains(new CraftingItem(12862, 1, EItemQuality.HQ), targets);
        Assert.Contains(new CraftingItem(12855, 1, EItemQuality.HQ), targets);
        Assert.Equal(expected, Craft.AreItemsSatisfied(targets, item => item.ItemId == changedItemId
            ? Craft.CountMatchingItems(item.Quality, normal, high, collectable)
            : Craft.CountMatchingItems(item.Quality, 0, 1, 0)));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    public void Culinarian2044_AcceptsEitherTartQualityWithHighQualityTea(int normalTarts, int highTarts, bool expected)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(2044)), 255, new QuestStep());

        var targets = Craft.GetItemTargets(task);

        Assert.Collection(targets,
            item => Assert.Equal(new CraftingItem(12842, 1, EItemQuality.HQ), item),
            item => Assert.Equal(new CraftingItem(12846, 1, EItemQuality.Any), item));
        Assert.Equal(expected, Craft.AreItemsSatisfied(targets, item => item.ItemId == 12842
            ? Craft.CountMatchingItems(item.Quality, 0, 1, 0)
            : Craft.CountMatchingItems(item.Quality, normalTarts, highTarts, 0)));
    }

    [Fact]
    public void ExplicitTarget_TakesPriorityOverMappedQuestItems()
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(2045)), 255,
            new QuestStep { ItemId = 42, ItemCount = 5, ItemQuality = EItemQuality.NQ });

        var targets = Craft.GetItemTargets(task);

        Assert.Equal(new CraftingItem(42, 5, EItemQuality.NQ), Assert.Single(targets));
        Assert.False(Craft.AreItemsSatisfied(targets, _ => 4));
        Assert.True(Craft.AreItemsSatisfied(targets, _ => 5));
    }

    [Theory]
    [InlineData(139, 2)]
    [InlineData(141, 255)]
    [InlineData(2044, 2)]
    [InlineData(2045, 2)]
    [InlineData(1, 255)]
    public void UnmappedQuestOrSequence_HasNoTargetsAndCannotSatisfyItems(ushort questId, byte sequence)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(questId)), sequence, new QuestStep());

        var targets = Craft.GetItemTargets(task);

        Assert.Empty(targets);
        Assert.False(Craft.AreItemsSatisfied(targets, _ => int.MaxValue));
    }

    [Fact]
    public void ListOnlyNonQuest_IsStillRejected()
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new SatisfactionSupplyNpcId(1)), 1, new QuestStep());

        Assert.Throws<TaskException>(() => Craft.HasItemTarget(task));
    }

    [Fact]
    public void OnlyConfirmedMissingList_AllowsRecipeFallback()
    {
        var ipc = Substitute.For<IArtisanIpc>();
        ipc.CraftList(65537).Returns(ArtisanStartResult.MissingList);

        Assert.False(Craft.TryStartQuestList(ipc, new QuestId(1)));
        ipc.Received(1).CraftList(65537);
        Assert.Single(ipc.ReceivedCalls());
    }

    [Fact]
    public void SubmittedList_DoesNotFallBackOrGuessAlternateId()
    {
        var ipc = Substitute.For<IArtisanIpc>();
        ipc.CraftList(65537).Returns(ArtisanStartResult.Submitted);

        Assert.True(Craft.TryStartQuestList(ipc, new QuestId(1)));
        ipc.Received(1).CraftList(65537);
        Assert.Single(ipc.ReceivedCalls());
    }

    [Fact]
    public void ListRejections_DoNotFallBack()
    {
        foreach (var result in new[] { ArtisanStartResult.Busy, ArtisanStartResult.Unavailable, ArtisanStartResult.Failed })
        {
            var ipc = Substitute.For<IArtisanIpc>();
            ipc.CraftList(65537).Returns(result);

            Assert.Throws<TaskException>(() => Craft.TryStartQuestList(ipc, new QuestId(1)));
            Assert.Single(ipc.ReceivedCalls());
        }
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 2)]
    [InlineData(1, 3, 1)]
    [InlineData(5, 1, 5)]
    [InlineData(int.MaxValue, 3, 715827883)]
    public void CraftQuantity_IsCeilingOfMissingItemsOverYield(int missing, int yield, int expected)
    {
        Assert.Equal(expected, Craft.GetCraftCount(missing, yield));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    public void InvalidCraftQuantity_IsRejected(int missing, int yield)
    {
        Assert.Throws<TaskException>(() => Craft.GetCraftCount(missing, yield));
    }

    [Fact]
    public void RecipeSelection_IncludesCulinarianFallbackAndPrefersCurrentCrafter()
    {
        Assert.Equal(42u, Craft.SelectRecipe(Job.CRP, [0, 0, 0, 0, 0, 0, 0, 42]));
        Assert.Equal(42u, Craft.SelectRecipe(Job.CUL, [1, 0, 0, 0, 0, 0, 0, 42]));
        Assert.Equal(1u, Craft.SelectRecipe(Job.CRP, [1, 0, 0, 0, 0, 0, 0, 42]));
    }

    [Fact]
    public void QualityCount_DoesNotCountNormalOrCollectableItemsAsHighQuality()
    {
        Assert.Equal(0, Craft.CountMatchingItems(EItemQuality.HQ, 5, 0, 3));
        Assert.Equal(5, Craft.CountMatchingItems(EItemQuality.NQ, 5, 2, 3));
        Assert.Equal(10, Craft.CountMatchingItems(EItemQuality.Any, 5, 2, 3));
    }

    [Fact]
    public void IdleStart_WaitsForCraftAndItems()
    {
        var progress = new Craft.Progress(ArtisanStatus.Idle);

        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.Zero));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(5)));
        Assert.False(progress.Update(ArtisanStatus.Busy, false, TimeSpan.FromSeconds(6)));
        Assert.False(progress.Update(ArtisanStatus.Idle, true, TimeSpan.FromSeconds(20)));
        Assert.True(progress.Update(ArtisanStatus.Idle, true, TimeSpan.FromSeconds(22)));
    }

    [Fact]
    public void IdleStartWithoutItems_TimesOut()
    {
        var progress = new Craft.Progress(ArtisanStatus.Idle);

        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.Zero));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Idle, false, Craft.Progress.StartTimeout));
    }

    [Fact]
    public void QueuedStartWithoutCrafting_TimesOut()
    {
        var progress = new Craft.Progress(ArtisanStatus.Queued);

        Assert.False(progress.Update(ArtisanStatus.Queued, false, TimeSpan.Zero));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Queued, false, Craft.Progress.StartTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyListOnlyIdleWithoutActiveExecution_TimesOut(bool objectiveMet)
    {
        var progress = new Craft.Progress(ArtisanStatus.Idle, requireObjective: false);

        Assert.False(progress.Update(ArtisanStatus.Idle, objectiveMet, TimeSpan.Zero));
        Assert.False(progress.Update(ArtisanStatus.Idle, objectiveMet, Craft.Progress.IdleGrace));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Idle, objectiveMet, Craft.Progress.StartTimeout));
    }

    [Fact]
    public void LegacyListOnlyQueuedWorkWithoutActiveExecution_TimesOut()
    {
        var progress = new Craft.Progress(ArtisanStatus.Queued, requireObjective: false);

        Assert.False(progress.Update(ArtisanStatus.Queued, false, TimeSpan.Zero));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(1)));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(3)));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Queued, false, Craft.Progress.StartTimeout));
    }

    [Fact]
    public void LegacyList_CompletesAfterCraftingStops()
    {
        var progress = new Craft.Progress(ArtisanStatus.Idle, requireObjective: false);

        Assert.False(progress.Update(ArtisanStatus.Busy, false, TimeSpan.FromSeconds(1)));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(20)));
        Assert.False(progress.Update(ArtisanStatus.Queued, false, TimeSpan.FromSeconds(21)));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(22)));
        Assert.True(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(24)));
    }

    [Fact]
    public void LegacyList_KeepsInitialBusyState()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy, requireObjective: false);

        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.Zero));
        Assert.True(progress.Update(ArtisanStatus.Idle, false, Craft.Progress.IdleGrace));
    }

    [Fact]
    public void InitialBusyThenIdle_FailsWithoutItems()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy);

        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.Zero));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Idle, false, Craft.Progress.IdleGrace));
    }

    [Fact]
    public void HqRequestedButOnlyNqProduced_FailsWhenListStops()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy);
        bool objectiveMet = Craft.CountMatchingItems(EItemQuality.HQ, 5, 0, 0) >= 5;

        Assert.False(progress.Update(ArtisanStatus.Idle, objectiveMet, TimeSpan.Zero));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Idle, objectiveMet, Craft.Progress.IdleGrace));
    }

    [Theory]
    [InlineData(1823u)]
    [InlineData(1895u)]
    public void PartialMultiItemOutput_FailsWhenListStops(uint missingItemId)
    {
        var task = new Craft.CraftTask(QuestTestData.CreateQuest(new QuestId(141)), 2, new QuestStep());
        var targets = Craft.GetItemTargets(task);
        var progress = new Craft.Progress(ArtisanStatus.Idle);
        bool objectiveMet = Craft.AreItemsSatisfied(targets, item => item.ItemId == missingItemId ? 0 : 1);

        Assert.False(objectiveMet);
        Assert.False(progress.Update(ArtisanStatus.Busy, objectiveMet, TimeSpan.Zero));
        Assert.False(progress.Update(ArtisanStatus.Idle, objectiveMet, TimeSpan.FromSeconds(20)));
        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Idle, objectiveMet, TimeSpan.FromSeconds(20) + Craft.Progress.IdleGrace));
    }

    [Fact]
    public void SufficientOutput_StillWaitsForQueuedCleanupToEnd()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy);

        Assert.False(progress.Update(ArtisanStatus.Queued, true, TimeSpan.FromMinutes(1)));
        Assert.False(progress.Update(ArtisanStatus.Idle, true, TimeSpan.FromSeconds(61)));
        Assert.True(progress.Update(ArtisanStatus.Idle, true, TimeSpan.FromSeconds(63)));
    }

    [Fact]
    public void BriefIdleBetweenCrafts_DoesNotFailOrComplete()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy);

        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.Zero));
        Assert.False(progress.Update(ArtisanStatus.Busy, false, TimeSpan.FromSeconds(1)));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(2)));
        Assert.False(progress.Update(ArtisanStatus.Idle, false, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void PausedRun_FailsRatherThanClaimingCancellationOrSuccess()
    {
        var progress = new Craft.Progress(ArtisanStatus.Busy);

        Assert.Throws<TaskException>(() => progress.Update(ArtisanStatus.Paused, true, TimeSpan.Zero));
    }
}
