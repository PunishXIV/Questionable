using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
using Xunit;
using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Steps;

public sealed class NextQuestTests
{
    private static readonly QuestStep Talk = new() { InteractionType = EInteractionType.Interact };
    private static readonly QuestStep Last = new() { InteractionType = EInteractionType.Interact };
    private static readonly QuestStep HandIn = new() { InteractionType = EInteractionType.CompleteQuest };

    [Fact]
    public void NoHandIn_LastStepEndsQuest()
    {
        var quest = CreateQuest(new QuestId(4996), Seq(1, Talk), Seq(3, new QuestStep(), Last), Seq(255));
        Assert.True(NextQuest.EndsQuest(quest, quest.FindSequence(3)!, Last));
        Assert.False(NextQuest.EndsQuest(quest, quest.FindSequence(1)!, Talk));
    }

    [Fact]
    public void HandIn_OnlyHandInEndsQuest()
    {
        var quest = CreateQuest(new QuestId(4997), Seq(1, Last), Seq(255, HandIn));
        Assert.True(NextQuest.EndsQuest(quest, quest.FindSequence(255)!, HandIn));
        Assert.False(NextQuest.EndsQuest(quest, quest.FindSequence(1)!, Last));
    }
}
