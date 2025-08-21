using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class ComplexCombatDataExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this ComplexCombatData complexCombatData)
    {
        var emptyData = new ComplexCombatData();
        return ObjectCreationExpression(
                IdentifierName(nameof(ComplexCombatData)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(ComplexCombatData.DataId), complexCombatData.DataId,
                                    emptyData.DataId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(ComplexCombatData.NameId), complexCombatData.NameId,
                                    emptyData.NameId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(ComplexCombatData.MinimumKillCount),
                                    complexCombatData.MinimumKillCount, emptyData.MinimumKillCount)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(ComplexCombatData.RewardItemId),
                                    complexCombatData.RewardItemId,
                                    emptyData.RewardItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(ComplexCombatData.RewardItemCount),
                                    complexCombatData.RewardItemCount,
                                    emptyData.RewardItemCount)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(ComplexCombatData.CompletionQuestVariablesFlags),
                                complexCombatData.CompletionQuestVariablesFlags),
                            Assignment(nameof(ComplexCombatData.IgnoreQuestMarker),
                                    complexCombatData.IgnoreQuestMarker,
                                    emptyData.IgnoreQuestMarker)
                                .AsSyntaxNodeOrToken()))));
    }
}
