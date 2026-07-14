using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class FishingOptionsExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this FishingOptions fishingOptions)
    {
        FishingOptions emptyOptions = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(FishingOptions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(FishingOptions.BaitId), fishingOptions.BaitId, emptyOptions.BaitId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(FishingOptions.HookType), fishingOptions.HookType, emptyOptions.HookType)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(FishingOptions.Preset), fishingOptions.Preset, emptyOptions.Preset)
                                .AsSyntaxNodeOrToken()))));
    }
}
