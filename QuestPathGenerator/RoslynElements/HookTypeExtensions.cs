using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class HookTypeExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this HookType hookType)
    {
        HookType emptyHookType = new();
        return ObjectCreationExpression(IdentifierName(nameof(HookType)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(HookType.Normal), hookType.Normal, emptyHookType.Normal)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(HookType.Double), hookType.Double, emptyHookType.Double)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(HookType.Triple), hookType.Triple, emptyHookType.Triple)
                                .AsSyntaxNodeOrToken()))));
    }
}
