using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Gathering;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class GatheringLocationExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this GatheringLocation location)
    {
        var emptyLocation = new GatheringLocation();
        return ObjectCreationExpression(
                IdentifierName(nameof(GatheringLocation)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(GatheringLocation.Position), location.Position,
                                emptyLocation.Position).AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheringLocation.MinimumAngle), location.MinimumAngle,
                                emptyLocation.MinimumAngle).AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheringLocation.MaximumAngle), location.MaximumAngle,
                                emptyLocation.MaximumAngle).AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheringLocation.MinimumDistance),
                                    location.MinimumDistance, emptyLocation.MinimumDistance)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(GatheringLocation.MaximumDistance),
                                    location.MaximumDistance, emptyLocation.MaximumDistance)
                                .AsSyntaxNodeOrToken()))));
    }
}
