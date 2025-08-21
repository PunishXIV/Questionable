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
        var emptySkip = new SkipConditions();
        return ObjectCreationExpression(
                IdentifierName(nameof(SkipConditions)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SkipConditions.StepIf), skipConditions.StepIf,
                                    emptySkip.StepIf)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipConditions.AetheryteShortcutIf),
                                    skipConditions.AetheryteShortcutIf, emptySkip.AetheryteShortcutIf)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(skipConditions.AethernetShortcutIf),
                                    skipConditions.AethernetShortcutIf, emptySkip.AethernetShortcutIf)
                                .AsSyntaxNodeOrToken()))));
    }

    public static ExpressionSyntax ToExpressionSyntax(this SkipStepConditions skipStepConditions)
    {
        var emptyStep = new SkipStepConditions();
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
                            Assignment(nameof(SkipStepConditions.AetheryteLocked),
                                    skipStepConditions.AetheryteLocked, emptyStep.AetheryteLocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipStepConditions.AetheryteUnlocked),
                                    skipStepConditions.AetheryteUnlocked, emptyStep.AetheryteUnlocked)
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
        var emptyItem = new SkipItemConditions();
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
        var emptyCondition = new NearPositionCondition();
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

    public static ExpressionSyntax ToExpressionSyntax(this SkipAetheryteCondition skipAetheryteCondition)
    {
        var emptyAetheryte = new SkipAetheryteCondition();
        return ObjectCreationExpression(
                IdentifierName(nameof(SkipAetheryteCondition)))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(SkipAetheryteCondition.Never), skipAetheryteCondition.Never,
                                emptyAetheryte.Never),
                            Assignment(nameof(SkipAetheryteCondition.InSameTerritory),
                                skipAetheryteCondition.InSameTerritory, emptyAetheryte.InSameTerritory),
                            AssignmentList(nameof(SkipAetheryteCondition.InTerritory),
                                skipAetheryteCondition.InTerritory),
                            AssignmentList(nameof(SkipAetheryteCondition.QuestsAccepted),
                                skipAetheryteCondition.QuestsAccepted),
                            AssignmentList(nameof(skipAetheryteCondition.QuestsCompleted),
                                skipAetheryteCondition.QuestsCompleted),
                            Assignment(nameof(SkipAetheryteCondition.AetheryteLocked),
                                    skipAetheryteCondition.AetheryteLocked, emptyAetheryte.AetheryteLocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipAetheryteCondition.AetheryteUnlocked),
                                    skipAetheryteCondition.AetheryteUnlocked,
                                    emptyAetheryte.AetheryteUnlocked)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(SkipAetheryteCondition.RequiredQuestVariablesNotMet),
                                    skipAetheryteCondition.RequiredQuestVariablesNotMet,
                                    emptyAetheryte.RequiredQuestVariablesNotMet)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(skipAetheryteCondition.NearPosition), skipAetheryteCondition.NearPosition,
                                emptyAetheryte.NearPosition)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(skipAetheryteCondition.NotNearPosition), skipAetheryteCondition.NotNearPosition,
                                    emptyAetheryte.NotNearPosition)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(skipAetheryteCondition.ExtraCondition), skipAetheryteCondition.ExtraCondition,
                                emptyAetheryte.ExtraCondition)
                                .AsSyntaxNodeOrToken()))));
    }
}
