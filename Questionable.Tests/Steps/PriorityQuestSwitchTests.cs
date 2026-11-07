using Questionable.Controller;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class PriorityQuestSwitchTests
{
    private static readonly QuestId Priority = new(1234);

    private static QuestController.QuestProgress Started(ushort id, bool interruptible)
    {
        var quest = QuestTestData.CreateQuest(new QuestId(id));
        quest.Root.Interruptible = interruptible;
        return new(quest, sequence: 1);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void Switch_OnlyFromInterruptibleOutsideDuty(bool interruptible, bool inDuty, bool expected)
    {
        Assert.Equal(expected, QuestSelectionController.CanSwitchToPriorityQuest(Started(962, interruptible), Priority, inDuty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleOrSameQuest_AlwaysSwitches(bool inDuty)
    {
        Assert.True(QuestSelectionController.CanSwitchToPriorityQuest(null, Priority, inDuty));
        Assert.True(QuestSelectionController.CanSwitchToPriorityQuest(Started(1234, interruptible: false), Priority, inDuty));
    }
}
