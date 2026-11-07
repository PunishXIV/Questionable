using Questionable.Controller.Steps.Shared;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftingBatchTests
{
    private static readonly HashSet<uint> Carpenter538Outputs = [1937, 1862, 2039];
    private static readonly HashSet<uint> Culinarian268Outputs = [4712, 4733, 4749];

    [Theory]
    [InlineData(5376u, 2)]
    [InlineData(1942u, 3)]
    public void Carpenter539_RejectsBothStages(uint itemId, int sequence)
    {
        var task = LoadTask(539, itemId);

        Assert.Equal(sequence, task.Sequence);
        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint> { 5376, 1942 }));
    }

    [Theory]
    [InlineData(1937u)]
    [InlineData(1862u)]
    [InlineData(2039u)]
    public void Carpenter538_ResolvesAllThreeCorrectedOutputs(uint itemId)
    {
        var task = LoadTask(538, itemId);
        var outputs = CraftingBatch.Resolve(task, Carpenter538Outputs);

        Assert.NotNull(outputs);
        Assert.Equal(3, outputs.Count);
        Assert.True(Carpenter538Outputs.SetEquals(outputs.Select(x => x.ItemId)));
        foreach (var craft in task.Quest.AllSteps().Where(x => x.Step.InteractionType == EInteractionType.Craft))
        {
            var expected = Assert.Single(Craft.GetItemTargets(new Craft.Task(task.Quest, craft.Sequence.Sequence, craft.Step)));
            Assert.Equal(expected, outputs.Single(x => x.ItemId == expected.ItemId));
        }

        Assert.False(Craft.AreItemsSatisfied(outputs, x => x.ItemId == 2039 ? 0 : x.ItemCount));
    }

    [Theory]
    [InlineData(5522u)]
    [InlineData(4712u)]
    [InlineData(4850u)]
    public void Culinarian268_ResolvesFinalGroupEvenAtIntermediateSteps(uint itemId)
    {
        var task = LoadTask(268, itemId);
        var outputs = CraftingBatch.Resolve(task, Culinarian268Outputs);

        Assert.NotNull(outputs);
        Assert.Equal(3, outputs.Count);
        Assert.True(Culinarian268Outputs.SetEquals(outputs.Select(x => x.ItemId)));
        Assert.DoesNotContain(outputs, x => x.ItemId is 5522 or 4850);
        var inventory = outputs.ToDictionary(x => x.ItemId, x => x.ItemCount);
        Assert.True(Craft.AreItemsSatisfied(outputs, x => inventory.GetValueOrDefault(x.ItemId)));

        if (itemId == 4850)
        {
            var sequence = task.Quest.FindSequence(task.Sequence)!;
            Assert.True(sequence.Steps.FindIndex(x => x.ItemId == 4712) < sequence.Steps.IndexOf(task.Step));
            Assert.Equal(0, inventory.GetValueOrDefault(4850u));
        }
    }

    [Fact]
    public void ExplicitCraft_RequiresMatchingRecipeData()
    {
        var task = LoadTask(538, 1937);

        Assert.Null(CraftingBatch.Resolve(task, null));
        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint>()));
        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint> { 1937, 1862 }));
        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint> { 1937, 1862, 2039, 999999 }));
    }

    [Fact]
    public void UnknownQuestData_RejectsMatchingRecipe()
    {
        var step = CraftStep(2219, 1);
        var quest = QuestTestData.CreateQuest(new QuestId(ushort.MaxValue), QuestTestData.Seq(255, step));

        Assert.Null(CraftingBatch.Resolve(new Craft.Task(quest, 255, step), new HashSet<uint> { 2219 }));
    }

    [Fact]
    public void MissingFinalOutputInBlock_RejectsList()
    {
        var task = LoadTask(538, 1937);
        task.Quest.FindSequence(task.Sequence)!.Steps.RemoveAll(x => x.ItemId == 2039);

        Assert.Null(CraftingBatch.Resolve(task, Carpenter538Outputs));
    }

    [Fact]
    public void InteractionBetweenCrafts_BreaksBatchEvenInSameSequence()
    {
        var task = LoadTask(538, 1937);
        task.Quest.FindSequence(task.Sequence)!.Steps.Insert(1, new QuestStep { InteractionType = EInteractionType.Interact });

        Assert.Null(CraftingBatch.Resolve(task, Carpenter538Outputs));
    }

    [Fact]
    public void TaskMustReferenceACraftInTheCurrentBlock()
    {
        var task = LoadTask(538, 1937);
        var detached = CraftStep(task.ItemId!.Value, task.ItemCount!.Value);

        Assert.Null(CraftingBatch.Resolve(task with { Step = detached }, Carpenter538Outputs));
        Assert.Null(CraftingBatch.Resolve(task with { Sequence = 255 }, Carpenter538Outputs));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing-item")]
    [InlineData("missing-count")]
    [InlineData("required-variables")]
    [InlineData("completion-flags")]
    [InlineData("required-job")]
    [InlineData("skip-territory")]
    [InlineData("skip-missing-item")]
    public void UncertainCraftAnywhereInBlock_RejectsList(string condition)
    {
        var task = LoadTask(538, 1937);
        var other = task.Quest.FindSequence(task.Sequence)!.Steps.Single(x => x.ItemId == 2039);
        switch (condition)
        {
            case "disabled":
                other.Disabled = true;
                break;
            case "missing-item":
                other.ItemId = null;
                break;
            case "missing-count":
                other.ItemCount = null;
                break;
            case "required-variables":
                other.RequiredQuestVariables = [[new QuestWorkValue(16)]];
                break;
            case "completion-flags":
                other.CompletionQuestVariablesFlags = [null, null, null, null, null, new QuestWorkValue(16)];
                break;
            case "required-job":
                other.RequiredCurrentJob = [EExtendedClassJob.Carpenter];
                break;
            case "skip-territory":
                other.SkipConditions = new SkipConditions { StepIf = new SkipStepConditions { InTerritory = [132] } };
                break;
            case "skip-missing-item":
                other.SkipConditions = new SkipConditions
                {
                    StepIf = new SkipStepConditions { Item = new SkipItemConditions { NotInInventory = true } },
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(condition));
        }

        Assert.Null(CraftingBatch.Resolve(task, Carpenter538Outputs));
    }

    [Theory]
    [InlineData(EItemQuality.Any)]
    [InlineData(EItemQuality.NQ)]
    [InlineData(EItemQuality.HQ)]
    public void DuplicateInventoryThresholds_MergeByMaximumWithoutMutatingSteps(EItemQuality quality)
    {
        var first = CraftStep(2219, 5, quality);
        var second = CraftStep(2219, 9, quality);
        var task = DuplicateTask(first, second);

        var outputs = CraftingBatch.Resolve(task, new HashSet<uint> { 2219 });

        Assert.NotNull(outputs);
        Assert.Equal(new CraftingItem(2219, 9, quality), Assert.Single(outputs));
        Assert.Equal(5, first.ItemCount);
        Assert.Equal(9, second.ItemCount);
    }

    [Theory]
    [InlineData(EItemQuality.NQ, 5, false)]
    [InlineData(EItemQuality.NQ, 5, true)]
    [InlineData(EItemQuality.HQ, 5, false)]
    [InlineData(EItemQuality.HQ, 5, true)]
    [InlineData(EItemQuality.NQ, 9, false)]
    [InlineData(EItemQuality.NQ, 9, true)]
    [InlineData(EItemQuality.HQ, 9, false)]
    [InlineData(EItemQuality.HQ, 9, true)]
    public void DuplicateQualities_KeepTheStrictRequirementWhenItCoversAnyCount(
        EItemQuality quality, int anyCount, bool strictFirst)
    {
        var strict = CraftStep(2219, 9, quality);
        var any = CraftStep(2219, anyCount);
        var task = strictFirst ? DuplicateTask(strict, any) : DuplicateTask(any, strict);

        var outputs = CraftingBatch.Resolve(task, new HashSet<uint> { 2219 });

        Assert.NotNull(outputs);
        Assert.Equal(new CraftingItem(2219, 9, quality), Assert.Single(outputs));
    }

    [Theory]
    [InlineData(EItemQuality.NQ, false)]
    [InlineData(EItemQuality.NQ, true)]
    [InlineData(EItemQuality.HQ, false)]
    [InlineData(EItemQuality.HQ, true)]
    public void DuplicateQualities_RejectListWhenAnyCountExceedsStrictCount(EItemQuality quality, bool strictFirst)
    {
        var strict = CraftStep(2219, 5, quality);
        var any = CraftStep(2219, 9);
        var task = strictFirst ? DuplicateTask(strict, any) : DuplicateTask(any, strict);

        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint> { 2219 }));
        Assert.Equal(5, strict.ItemCount);
        Assert.Equal(quality, strict.ItemQuality);
        Assert.Equal(9, any.ItemCount);
        Assert.Equal(EItemQuality.Any, any.ItemQuality);
    }

    [Theory]
    [InlineData(EItemQuality.NQ, EItemQuality.HQ)]
    [InlineData(EItemQuality.HQ, EItemQuality.NQ)]
    public void DuplicateStrictNqAndHqTargets_RejectList(EItemQuality first, EItemQuality second)
    {
        var task = DuplicateTask(CraftStep(2219, 5, first), CraftStep(2219, 9, second));

        Assert.Null(CraftingBatch.Resolve(task, new HashSet<uint> { 2219 }));
    }

    [Fact]
    public void CorrectedHqMetadata_DoesNotHideAnExplicitNqConflict()
    {
        var task = LoadTask(538, 1937);
        task.Step.ItemQuality = EItemQuality.NQ;

        Assert.Equal(EItemQuality.HQ, Assert.Single(Craft.GetItemTargets(task)).Quality);
        Assert.Null(CraftingBatch.Resolve(task, Carpenter538Outputs));
    }

    [Fact]
    public void KnownList_ReturnsExistingTargets()
    {
        var task = LoadTask(141, null);

        var outputs = CraftingBatch.Resolve(task, null);

        Assert.NotNull(outputs);
        Assert.Equal(Craft.GetItemTargets(task).ToArray(), outputs.ToArray());
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public void LegacyUnknownList_PreservesEmptyCompatibilityWithoutSatisfiedObjective()
    {
        var step = new QuestStep { InteractionType = EInteractionType.Craft };
        var quest = QuestTestData.CreateQuest(new QuestId(ushort.MaxValue), QuestTestData.Seq(255, step));

        var outputs = CraftingBatch.Resolve(new Craft.Task(quest, 255, step), null);

        Assert.NotNull(outputs);
        Assert.Empty(outputs);
        Assert.False(Craft.AreItemsSatisfied(outputs, _ => 0));
    }

    private static QuestStep CraftStep(uint itemId, int count, EItemQuality quality = EItemQuality.Any) =>
        new() { InteractionType = EInteractionType.Craft, ItemId = itemId, ItemCount = count, ItemQuality = quality };

    private static Craft.Task DuplicateTask(params QuestStep[] steps)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(139), QuestTestData.Seq(255, steps));
        return new Craft.Task(quest, 255, steps[0]);
    }

    private static Craft.Task LoadTask(ushort questId, uint? itemId)
    {
        string resource = typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
            .Single(x => x.Contains($"/{questId}_", StringComparison.Ordinal));
        var quest = QuestTestData.LoadQuest(new QuestId(questId), new EmbeddedQuest(resource));
        var craft = quest.AllSteps().Single(x => x.Step.InteractionType == EInteractionType.Craft && x.Step.ItemId == itemId);
        return new Craft.Task(quest, craft.Sequence.Sequence, craft.Step);
    }
}
