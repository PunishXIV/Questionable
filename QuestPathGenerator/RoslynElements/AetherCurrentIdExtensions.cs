using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class AetherCurrentIdExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this AetherCurrentId aetherCurrentId) => ObjectCreationExpression(
            IdentifierName(nameof(AetherCurrentId)))
        .WithArgumentList(
            ArgumentList(
                SingletonSeparatedList(
                    Argument(LiteralValue(aetherCurrentId.Value)))));
}
