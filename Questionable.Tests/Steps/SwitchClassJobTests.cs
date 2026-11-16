using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class SwitchClassJobTests
{
    [Fact]
    public void LaterStep_FindsEarlierSwitch()
    {
        var switchStep = new QuestStep { InteractionType = EInteractionType.SwitchClass, TargetClass = EExtendedClassJob.ConfiguredCombatJob };
        var fight = new QuestStep { InteractionType = EInteractionType.Combat };
        var talk = new QuestStep { InteractionType = EInteractionType.Interact };
        var sequence = QuestTestData.Seq(1, talk, switchStep, fight);

        Assert.Same(switchStep, SwitchClassJob.EarlierSwitch(sequence, fight));
        Assert.Null(SwitchClassJob.EarlierSwitch(sequence, talk));
        Assert.Null(SwitchClassJob.EarlierSwitch(sequence, switchStep));
    }
}
