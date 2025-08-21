using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class SatisfactionSupplyIdExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this SatisfactionSupplyNpcId satisfactionSupplyNpcId)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(SatisfactionSupplyNpcId)))
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(LiteralValue(satisfactionSupplyNpcId.Value)))));
    }
}
