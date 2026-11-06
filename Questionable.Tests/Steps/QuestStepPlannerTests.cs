using Questionable.Controller;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestStepPlannerTests
{
    private static readonly QuestId QuestId = new(123);

    private static QuestController.QuestProgress Progress(byte sequence, int step, int stepCount = 2, ElementId? id = null) =>
        new(QuestTestData.CreateQuest(id ?? QuestId,
            QuestTestData.Seq(1, Enumerable.Range(0, stepCount).Select(_ => new QuestStep()).ToArray()),
            QuestTestData.Seq(2)), sequence, step);

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(3, 0, 1)]
    [InlineData(1, 255, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 0, 0)]
    [InlineData(2, 5, 0)]
    public void CheckStepClassifiesTheCurrentStep(byte sequence, int step, int expected)
    {
        Assert.Equal((EStepState)expected, QuestStepPlanner.CheckStep(Progress(sequence, step)));
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 255)]
    [InlineData(0, 1, 255)]
    public void AdvanceMovesToTheNextStepOrCompletes(int step, int stepCount, int expectedNext)
    {
        var progress = Progress(1, step, stepCount);
        QuestSequence sequence = progress.FindSequence()!;

        StepAdvance advance = QuestStepPlanner.Advance(progress, sequence, progress.FindStep(), null, null);

        Assert.Equal(new StepAdvance(EStepAdvance.Advanced, expectedNext), advance);
    }

    [Fact]
    public void AdvanceIgnoresStaleOrMissingProgress()
    {
        var progress = Progress(1, 0);
        QuestSequence sequence = progress.FindSequence()!;
        QuestStep step = progress.FindStep()!;

        Assert.Equal(EStepAdvance.NoStep, QuestStepPlanner.Advance(null, sequence, step, null, null).Kind);
        Assert.Equal(EStepAdvance.NoStep, QuestStepPlanner.Advance(progress, null, step, null, null).Kind);
        Assert.Equal(EStepAdvance.NoStep, QuestStepPlanner.Advance(progress, sequence, null, null, null).Kind);
        Assert.Equal(EStepAdvance.OtherQuest, QuestStepPlanner.Advance(progress, sequence, step, new QuestId(124), 1).Kind);
        Assert.Equal(EStepAdvance.OtherSequence, QuestStepPlanner.Advance(progress, sequence, step, QuestId, 2).Kind);
        Assert.Equal(EStepAdvance.Advanced, QuestStepPlanner.Advance(progress, sequence, step, QuestId, 1).Kind);
    }

    [Fact]
    public void GatheringDeliveryIsDoneOnlyAfterItsTurnInSequence()
    {
        ElementId npc = new SatisfactionSupplyNpcId(1);
        var gathering = QuestController.ECurrentQuestType.Gathering;

        Assert.True(QuestStepPlanner.IsGatheringDeliveryDone((Progress(1, 255, id: npc), gathering)));
        Assert.False(QuestStepPlanner.IsGatheringDeliveryDone((Progress(1, 0, id: npc), gathering)));
        Assert.False(QuestStepPlanner.IsGatheringDeliveryDone((Progress(2, 255, id: npc), gathering)));
        Assert.False(QuestStepPlanner.IsGatheringDeliveryDone((Progress(1, 255, id: npc), QuestController.ECurrentQuestType.Normal)));
        Assert.False(QuestStepPlanner.IsGatheringDeliveryDone((Progress(1, 255), gathering)));
        Assert.False(QuestStepPlanner.IsGatheringDeliveryDone(null));
    }
}
