using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Gathering;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class GatheringNodeGroupExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this GatheringNodeGroup nodeGroup)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(GatheringNodeGroup)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            AssignmentList(nameof(GatheringNodeGroup.Nodes), nodeGroup.Nodes)
                                .AsSyntaxNodeOrToken()))));
    }
}
