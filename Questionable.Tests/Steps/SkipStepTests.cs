using Questionable.Controller;
using Questionable.Model.Questing;
using Xunit;
using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Steps;

public sealed class SkipStepTests
{
    private static readonly Domain.Quest Quest = CreateQuest(new QuestId(1),
        Seq(0, new QuestStep()),
        Seq(1, new QuestStep(), new QuestStep(), new QuestStep()));

    private static readonly Domain.Quest SimQuest = CreateQuest(new QuestId(2),
        Seq(255, new QuestStep()),
        Seq(0, new QuestStep()),
        Seq(1, new QuestStep(), new QuestStep(), new QuestStep()));

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 0, false)]
    public void LastStepOfSequence_CannotSkip(byte sequence, int step, bool expected) =>
        Assert.Equal(expected, QuestController.CanSkipStep(new QuestController.QuestProgress(Quest, sequence, step)));

    [Fact]
    public void NoQuest_CannotSkip() => Assert.False(QuestController.CanSkipStep(null));

    [Theory]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 2, 255, 0)]
    [InlineData(1, 255, 255, 0)]
    [InlineData(0, 0, 1, 0)]
    public void SimNextStep_MovesToNextSequence(byte sequence, int step, byte nextSequence, int nextStep) =>
        Assert.Equal(((byte, int)?)(nextSequence, nextStep),
            QuestStepPlanner.NextStep(new QuestController.QuestProgress(SimQuest, sequence, step)));

    [Fact]
    public void SimLastSequence_HasNoNextStep() =>
        Assert.Null(QuestStepPlanner.NextStep(new QuestController.QuestProgress(SimQuest, 255, 0)));
}
