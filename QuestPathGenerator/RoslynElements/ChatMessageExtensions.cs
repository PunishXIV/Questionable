using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class ChatMessageExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this ChatMessage chatMessage)
    {
        ChatMessage emptyMessage = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(ChatMessage)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(ChatMessage.ExcelSheet), chatMessage.ExcelSheet,
                                    emptyMessage.ExcelSheet)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(ChatMessage.Key), chatMessage.Key,
                                    emptyMessage.Key)
                                .AsSyntaxNodeOrToken()))));
    }
}
