using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class GatheredItemExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this GatheredItem gatheredItem)
    {
        var emptyItem = new GatheredItem();
        return ObjectCreationExpression(
                IdentifierName(nameof(GatheredItem)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(GatheredItem.ItemId), gatheredItem.ItemId, emptyItem.ItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheredItem.AlternativeItemId),
                                    gatheredItem.AlternativeItemId,
                                    emptyItem.AlternativeItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheredItem.ItemCount), gatheredItem.ItemCount,
                                    emptyItem.ItemCount)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheredItem.Collectability), gatheredItem.Collectability,
                                    emptyItem.Collectability)
                                .AsSyntaxNodeOrToken()))));
    }
}
