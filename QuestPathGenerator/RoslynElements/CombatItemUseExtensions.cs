using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class CombatItemUseExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this CombatItemUse combatItemuse)
    {
        var emptyItemuse = new CombatItemUse();
        return ObjectCreationExpression(
                IdentifierName(nameof(CombatItemUse)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(CombatItemUse.ItemId), combatItemuse.ItemId,
                                    emptyItemuse.ItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(CombatItemUse.Condition), combatItemuse.Condition, emptyItemuse.Condition)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(combatItemuse.Value), combatItemuse.Value, emptyItemuse.Value)
                                .AsSyntaxNodeOrToken()))));
    }
}
