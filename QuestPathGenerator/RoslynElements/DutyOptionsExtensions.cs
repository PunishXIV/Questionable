using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class DutyOptionsExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this DutyOptions dutyOptions)
    {
        var emptyOptions = new DutyOptions();
        return ObjectCreationExpression(
                IdentifierName(nameof(DutyOptions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(DutyOptions.Enabled),
                                    dutyOptions.Enabled, emptyOptions.Enabled)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DutyOptions.ContentFinderConditionId),
                                    dutyOptions.ContentFinderConditionId, emptyOptions.ContentFinderConditionId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DutyOptions.LowPriority),
                                    dutyOptions.LowPriority, emptyOptions.LowPriority)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(DutyOptions.Notes), dutyOptions.Notes)
                                .AsSyntaxNodeOrToken()))));
    }
}
