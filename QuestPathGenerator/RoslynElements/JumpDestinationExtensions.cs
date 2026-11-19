using System.Numerics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class JumpDestinationExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this JumpDestination jumpDestination) => ObjectCreationExpression(
            IdentifierName(nameof(JumpDestination)))
        .WithInitializer(
            InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SeparatedList<ExpressionSyntax>(
                    SyntaxNodeList(
                        Assignment<Vector3?>(nameof(JumpDestination.Position), jumpDestination.Position, defaultValue: null)
                            .AsSyntaxNodeOrToken(),
                        Assignment(nameof(JumpDestination.StopDistance), jumpDestination.StopDistance, defaultValue: null)
                            .AsSyntaxNodeOrToken(),
                        Assignment(nameof(JumpDestination.DelaySeconds), jumpDestination.DelaySeconds, defaultValue: null)
                            .AsSyntaxNodeOrToken(),
                        Assignment(nameof(JumpDestination.Type), jumpDestination.Type, default)
                            .AsSyntaxNodeOrToken()))));
}
