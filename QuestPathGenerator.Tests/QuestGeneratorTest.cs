using Microsoft.CodeAnalysis;
using Questionable.Model.Questing;
using Questionable.QuestPathGenerator;
using Xunit;
namespace QuestPathGenerator.Tests;

public sealed class QuestGeneratorTest
{
    [Fact]
    public void SyntaxNodeListWithNullValues()
    {
        ComplexCombatData complexCombatData = new()
        {
            DataId = 47,
            IgnoreQuestMarker = true,
            MinimumKillCount = 1
        };

        List<SyntaxNodeOrToken> list =
            RoslynShortcuts.SyntaxNodeList(
                RoslynShortcuts.AssignmentList(nameof(ComplexCombatData.CompletionQuestVariablesFlags),
                    complexCombatData.CompletionQuestVariablesFlags)).ToList();

        Assert.Empty(list);
    }
}
