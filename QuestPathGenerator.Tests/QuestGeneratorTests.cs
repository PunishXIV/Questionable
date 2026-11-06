using Microsoft.CodeAnalysis;
using Questionable.Model.Questing;
using Questionable.QuestPathGenerator;
using Xunit;
namespace QuestPathGenerator.Tests;

public sealed class QuestGeneratorTests
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

    [Fact]
    public void NestedQuestWorkListsRenderAsCollections()
    {
        QuestStep step = new()
        {
            TerritoryId = 1,
            InteractionType = EInteractionType.WalkTo,
            RequiredQuestVariables = [[new QuestWorkValue(1, 2, EQuestWorkMode.Exact)], null, []],
            CompletionQuestVariablesFlags = [null, new QuestWorkValue(0x12)],
        };

        string code = RoslynShortcuts.LiteralValue(step).NormalizeWhitespace().ToFullString();

        Assert.Equal(
            """
            new QuestStep(EInteractionType.WalkTo, null, null, 1U)
            {
                RequiredQuestVariables = [[new QuestWorkValue(1, 2, EQuestWorkMode.Exact)], null, []],
                CompletionQuestVariablesFlags = [null, new QuestWorkValue(1, 2, EQuestWorkMode.Bitwise)]
            }
            """.ReplaceLineEndings(), code.ReplaceLineEndings());
    }
}
