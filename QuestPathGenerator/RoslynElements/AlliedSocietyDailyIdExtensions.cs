using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class AlliedSocietyDailyIdExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this AlliedSocietyDailyId alliedSocietyDailyId)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(AlliedSocietyDailyId)))
            .WithArgumentList(
                ArgumentList(
                    SeparatedList<ArgumentSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                            Argument(LiteralValue(alliedSocietyDailyId.AlliedSociety)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(alliedSocietyDailyId.Rank)),
                        })));
    }
}
