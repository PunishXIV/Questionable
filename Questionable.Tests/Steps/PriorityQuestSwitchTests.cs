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
    public void SwitchesAwayOnlyFromInterruptibleQuestsOutsideDuties(bool interruptible, bool inDuty, bool expected)
    {
        Assert.Equal(expected, QuestController.CanSwitchToPriorityQuest(Started(962, interruptible), Priority, inDuty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingStartedOrAlreadyOnThePriorityQuestAlwaysSwitches(bool inDuty)
    {
        Assert.True(QuestController.CanSwitchToPriorityQuest(null, Priority, inDuty));
        Assert.True(QuestController.CanSwitchToPriorityQuest(Started(1234, interruptible: false), Priority, inDuty));
    }
}
