using Questionable.Model.Questing;
using Questionable.QuestPathGenerator;
using Xunit;

namespace QuestPathGenerator.Tests;

public sealed class QuestGeneratorTest
{
    [Fact]
    public void SyntaxNodeListWithNullValues()
    {
        var complexCombatData = new ComplexCombatData
        {
            DataId = 47,
            IgnoreQuestMarker = true,
            MinimumKillCount = 1,
        };

        var list =
            RoslynShortcuts.SyntaxNodeList(
                RoslynShortcuts.AssignmentList(nameof(ComplexCombatData.CompletionQuestVariablesFlags),
                    complexCombatData.CompletionQuestVariablesFlags)).ToList();

        Assert.Empty(list);
    }
}
