using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class QuestIdExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this QuestId questId) => ObjectCreationExpression(
            IdentifierName(nameof(QuestId)))
        .WithArgumentList(
            ArgumentList(
                SingletonSeparatedList(
                    Argument(LiteralValue(questId.Value)))));
}
