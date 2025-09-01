using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Numerics;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class Vector3Extensions
{
    public static ExpressionSyntax ToExpressionSyntax(this Vector3 vector)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(Vector3)))
            .WithArgumentList(
                ArgumentList(
                    SeparatedList<ArgumentSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                            Argument(LiteralValue(vector.X)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(vector.Y)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(vector.Z))
                        })));
    }
}
