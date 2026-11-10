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

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 0, false)]
    public void LastStepOfSequence_CannotSkip(byte sequence, int step, bool expected) =>
        Assert.Equal(expected, QuestController.CanSkipStep(new QuestController.QuestProgress(Quest, sequence, step)));

    [Fact]
    public void NoQuest_CannotSkip() => Assert.False(QuestController.CanSkipStep(null));
}
