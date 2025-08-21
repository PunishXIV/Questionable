using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class PurchaseMenuExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this PurchaseMenu purchaseMenu)
    {
        PurchaseMenu emptyMenu = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(PurchaseMenu)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(PurchaseMenu.ExcelSheet), purchaseMenu.ExcelSheet,
                                    emptyMenu.ExcelSheet)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(PurchaseMenu.Key), purchaseMenu.Key,
                                    emptyMenu.Key)
                                .AsSyntaxNodeOrToken()))));
    }
}
