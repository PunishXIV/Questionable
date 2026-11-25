using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class QuestWorkValueExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this QuestWorkValue qwv) => ObjectCreationExpression(
            IdentifierName(nameof(QuestWorkValue)))
        .WithArgumentList(
            ArgumentList(
                SeparatedList<ArgumentSyntax>(
                    new SyntaxNodeOrToken[]
                    {
                        Argument(LiteralValue(qwv.High)),
                        Token(SyntaxKind.CommaToken),
                        Argument(LiteralValue(qwv.Low)),
                        Token(SyntaxKind.CommaToken),
                        Argument(LiteralValue(qwv.Mode))
                    })));
}
