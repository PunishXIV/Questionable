using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class UnlockLinkIdExtension
{
    public static ExpressionSyntax ToExpressionSyntax(this UnlockLinkId unlockLinkId)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(UnlockLinkId)))
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(LiteralValue(unlockLinkId.Value)))));
    }
}
