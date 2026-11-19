using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Xunit;

namespace Questionable.Tests.Windows;

public sealed class ActiveQuestComponentTests
{
    [Theory]
    [InlineData(EInteractionType.SinglePlayerDuty, "Quest battle")]
    [InlineData(EInteractionType.CompleteQuest, "Turn in")]
    [InlineData(EInteractionType.WaitForNpcAtPosition, "Wait for NPC")]
    [InlineData(EInteractionType.None, "None")]
    public void StepLabel_ReadsLikeText(EInteractionType type, string expected) =>
        Assert.Equal(expected, ActiveQuestComponent.StepLabel(type));

    [Fact]
    public void StepLabel_NoCodeNames()
    {
        foreach (EInteractionType type in Enum.GetValues<EInteractionType>())
            Assert.DoesNotMatch("[a-z][A-Z]", ActiveQuestComponent.StepLabel(type));
    }
}
