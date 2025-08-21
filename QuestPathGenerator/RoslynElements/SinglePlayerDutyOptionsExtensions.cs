using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class SinglePlayerDutyOptionsExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this SinglePlayerDutyOptions dutyOptions)
    {
        var emptyOptions = new SinglePlayerDutyOptions();
        return ObjectCreationExpression(
                IdentifierName(nameof(SinglePlayerDutyOptions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SinglePlayerDutyOptions.Enabled),
                                    dutyOptions.Enabled, emptyOptions.Enabled)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SinglePlayerDutyOptions.Notes), dutyOptions.Notes)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SinglePlayerDutyOptions.Index),
                                    dutyOptions.Index, emptyOptions.Index)
                                .AsSyntaxNodeOrToken()))));
    }
}
