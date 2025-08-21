using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Gathering;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class GatheringNodeExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this GatheringNode gatheringNode)
    {
        var emptyLocation = new GatheringNode();
        return ObjectCreationExpression(
                IdentifierName(nameof(GatheringNode)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(GatheringNode.DataId), gatheringNode.DataId,
                                    emptyLocation.DataId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheringNode.Fly), gatheringNode.Fly, emptyLocation.Fly)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(GatheringNode.Locations), gatheringNode.Locations)
                                .AsSyntaxNodeOrToken()))));
    }
}
