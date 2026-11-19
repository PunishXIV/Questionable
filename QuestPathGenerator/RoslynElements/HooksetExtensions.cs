using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class HooksetExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this Hookset hookset)
    {
        Hookset emptyHookset = new();
        return ObjectCreationExpression(IdentifierName(nameof(Hookset)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(Hookset.Weak), hookset.Weak, emptyHookset.Weak)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(Hookset.Strong), hookset.Strong, emptyHookset.Strong)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(Hookset.Legendary), hookset.Legendary, emptyHookset.Legendary)
                                .AsSyntaxNodeOrToken()))));
    }
}
