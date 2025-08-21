using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Common;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class AethernetShortcutExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this AethernetShortcut aethernetShortcut)
    {
        return ObjectCreationExpression(
                IdentifierName(nameof(AethernetShortcut)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment<EAetheryteLocation?>(nameof(AethernetShortcut.From),
                                    aethernetShortcut.From,
                                    null)
                                .AsSyntaxNodeOrToken(),
                            Assignment<EAetheryteLocation?>(nameof(AethernetShortcut.To),
                                    aethernetShortcut.To,
                                    null)
                                .AsSyntaxNodeOrToken()))));
    }
}
