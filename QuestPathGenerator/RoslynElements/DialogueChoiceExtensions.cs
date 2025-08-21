using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class DialogueChoiceExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this DialogueChoice dialogueChoice)
    {
        DialogueChoice emptyChoice = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(DialogueChoice)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment<EDialogChoiceType?>(nameof(DialogueChoice.Type), dialogueChoice.Type,
                                    null)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.ExcelSheet), dialogueChoice.ExcelSheet,
                                    emptyChoice.ExcelSheet)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.Prompt), dialogueChoice.Prompt,
                                    emptyChoice.Prompt)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.Yes), dialogueChoice.Yes, emptyChoice.Yes)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.Answer), dialogueChoice.Answer,
                                    emptyChoice.Answer)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.PromptIsRegularExpression),
                                    dialogueChoice.PromptIsRegularExpression,
                                    emptyChoice.PromptIsRegularExpression)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.AnswerIsRegularExpression),
                                    dialogueChoice.AnswerIsRegularExpression,
                                    emptyChoice.AnswerIsRegularExpression)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.DataId), dialogueChoice.DataId,
                                    emptyChoice.DataId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(DialogueChoice.SpecialCondition), dialogueChoice.SpecialCondition,
                                    emptyChoice.SpecialCondition)
                                .AsSyntaxNodeOrToken()))));
    }
}
