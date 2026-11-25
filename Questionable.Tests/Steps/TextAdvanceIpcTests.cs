using Questionable.External;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TextAdvanceIpcTests
{
    private static QuestStep Step(EInteractionType type) => new(type, 1, null, 1);

    [Theory]
    [InlineData(EInteractionType.CompleteQuest, false, true)]
    [InlineData(EInteractionType.Interact, false, false)]
    [InlineData(EInteractionType.AcceptQuest, false, false)]
    [InlineData(EInteractionType.AcceptQuest, true, true)]
    public void AutoComplete_OnlyOnTurnInSteps(EInteractionType type, bool instant, bool expected) =>
        Assert.Equal(expected, TextAdvanceIpc.CompletesQuestHere(Step(type), instant));

    [Fact]
    public void AutoComplete_WhenStepTurnsInAnother()
    {
        QuestStep step = Step(EInteractionType.Interact);
        step.TurnInQuestId = new QuestId(1);
        Assert.True(TextAdvanceIpc.CompletesQuestHere(step, false));
    }

    [Fact]
    public void Pausers_SkipPluginsQstUses() =>
        Assert.Equal(["Henchman"], TextAdvanceIpc.OutsidePausers(["Knightshopper", "Gather & Chill", "Henchman"],
            [("GatherChill", "Gather & Chill"), ("Henchman", "Henchman")]));

    [Fact]
    public void AutoComplete_WithoutQuest() => Assert.True(TextAdvanceIpc.CompletesQuestHere(null, false));
}
