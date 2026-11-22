using ECommons.ExcelServices;
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

    [Theory]
    [InlineData(Job.DRK, Job.BLU, true)]
    [InlineData(Job.DRK, Job.DRK, false)]
    [InlineData(Job.ADV, Job.BLU, false)]
    public void NeedsSwitch_JobQuest(Job accepted, Job current, bool expected) =>
        Assert.Equal(expected, SwitchClassJob.NeedsSwitch(accepted, current, [Job.DRK]));

    [Fact]
    public void NeedsSwitch_ClassQuestOnJob_Stays() =>
        Assert.False(SwitchClassJob.NeedsSwitch(Job.GLA, Job.PLD, [Job.GLA, Job.PLD]));

    [Fact]
    public void NeedsSwitch_AnyJobQuest_Stays() =>
        Assert.False(SwitchClassJob.NeedsSwitch(Job.WAR, Job.BLU, [Job.WAR, Job.BLU, Job.DRK]));
}
