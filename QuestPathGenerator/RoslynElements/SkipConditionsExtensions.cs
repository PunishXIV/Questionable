using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class SkipConditionsExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this SkipConditions skipConditions)
    {
        SkipConditions emptySkip = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(SkipConditions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SkipConditions.StepIf), skipConditions.StepIf,
                                    emptySkip.StepIf)
                                .AsSyntaxNodeOrToken()))));
    }

    public static ExpressionSyntax ToExpressionSyntax(this SkipStepConditions skipStepConditions)
    {
        SkipStepConditions emptyStep = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(SkipStepConditions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SkipStepConditions.Never), skipStepConditions.Never,
                                    emptyStep.Never)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.CompletionQuestVariablesFlags),
                                    skipStepConditions.CompletionQuestVariablesFlags)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.Flying), skipStepConditions.Flying,
                                    emptyStep.Flying)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.Diving), skipStepConditions.Diving,
                                    emptyStep.Diving)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.Chocobo), skipStepConditions.Chocobo,
                                    emptyStep.Chocobo)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.NotTargetable),
                                    skipStepConditions.NotTargetable, emptyStep.NotTargetable)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.InTerritory),
                                skipStepConditions.InTerritory).AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.NotInTerritory),
                                skipStepConditions.NotInTerritory).AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.Item), skipStepConditions.Item,
                                    emptyStep.Item)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.QuestsAccepted),
                                skipStepConditions.QuestsAccepted).AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.QuestsCompleted),
                                skipStepConditions.QuestsCompleted).AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.NotNamePlateIconId),
                                skipStepConditions.NotNamePlateIconId).AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.DutiesUnlocked),
                                skipStepConditions.DutiesUnlocked).AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(SkipStepConditions.DutiesCompleted),
                                skipStepConditions.DutiesCompleted).AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.AetheryteLocked),
                                    skipStepConditions.AetheryteLocked, emptyStep.AetheryteLocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.AetheryteUnlocked),
                                    skipStepConditions.AetheryteUnlocked, emptyStep.AetheryteUnlocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.AetherCurrentUnlocked),
                                    skipStepConditions.AetherCurrentUnlocked, emptyStep.AetherCurrentUnlocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.NearPosition),
                                    skipStepConditions.NearPosition, emptyStep.NearPosition)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.NotNearPosition),
                                    skipStepConditions.NotNearPosition, emptyStep.NotNearPosition)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.ExtraCondition),
                                    skipStepConditions.ExtraCondition, emptyStep.ExtraCondition)
                                .AsSyntaxNodeOrToken()))));
    }

    public static ExpressionSyntax ToExpressionSyntax(this SkipItemConditions skipItemCondition)
    {
        SkipItemConditions emptyItem = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(SkipItemConditions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SkipItemConditions.NotInInventory),
                                skipItemCondition.NotInInventory,
                                emptyItem.NotInInventory)))));
    }

    public static ExpressionSyntax ToExpressionSyntax(this NearPositionCondition nearPositionCondition)
    {
        NearPositionCondition emptyCondition = new();
        return ObjectCreationExpression(
                IdentifierName(nameof(NearPositionCondition)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(NearPositionCondition.Position),
                                    nearPositionCondition.Position, emptyCondition.Position)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(NearPositionCondition.MaximumDistance),
                                    nearPositionCondition.MaximumDistance, emptyCondition.MaximumDistance)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(NearPositionCondition.TerritoryId),
                                    nearPositionCondition.TerritoryId, emptyCondition.TerritoryId)
                                .AsSyntaxNodeOrToken()))));
    }

}
