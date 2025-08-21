using System.Numerics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class JumpDestinationExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this JumpDestination jumpDestination)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(JumpDestination)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment<Vector3?>(nameof(JumpDestination.Position), jumpDestination.Position,
                                    null)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(JumpDestination.StopDistance), jumpDestination.StopDistance,
                                    null)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(JumpDestination.DelaySeconds), jumpDestination.DelaySeconds,
                                    null)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(JumpDestination.Type), jumpDestination.Type, default)
                                .AsSyntaxNodeOrToken()))));
    }
}
