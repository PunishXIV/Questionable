using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class QuestStepExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this QuestStep step)
    {
        var emptyStep = new QuestStep();
        return ObjectCreationExpression(
                IdentifierName(nameof(QuestStep)))
            .WithArgumentList(
                ArgumentList(
                    SeparatedList<ArgumentSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                            Argument(LiteralValue(step.InteractionType)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(step.DataId)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(step.Position)),
                            Token(SyntaxKind.CommaToken),
                            Argument(LiteralValue(step.TerritoryId))
                        })))
            .WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SeparatedList<ExpressionSyntax>(
                        SyntaxNodeList(
                            Assignment(nameof(QuestStep.StopDistance), step.StopDistance,
                                    emptyStep.StopDistance)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.NpcWaitDistance), step.NpcWaitDistance,
                                    emptyStep.NpcWaitDistance)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.TargetTerritoryId), step.TargetTerritoryId,
                                    emptyStep.TargetTerritoryId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.DelaySecondsAtStart), step.DelaySecondsAtStart,
                                    emptyStep.DelaySecondsAtStart)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.PickUpItemId), step.PickUpItemId, emptyStep.PickUpItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Disabled), step.Disabled, emptyStep.Disabled)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.DisableNavmesh), step.DisableNavmesh,
                                    emptyStep.DisableNavmesh)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Mount), step.Mount, emptyStep.Mount)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Fly), step.Fly, emptyStep.Fly)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Land), step.Land, emptyStep.Land)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Sprint), step.Sprint, emptyStep.Sprint)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.IgnoreDistanceToObject),
                                    step.IgnoreDistanceToObject, emptyStep.IgnoreDistanceToObject)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.RestartNavigationIfCancelled),
                                    step.RestartNavigationIfCancelled, emptyStep.RestartNavigationIfCancelled)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Comment), step.Comment, emptyStep.Comment)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Aetheryte), step.Aetheryte, emptyStep.Aetheryte)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.AethernetShard), step.AethernetShard,
                                    emptyStep.AethernetShard)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.AetheryteShortcut), step.AetheryteShortcut,
                                    emptyStep.AetheryteShortcut)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.AethernetShortcut), step.AethernetShortcut,
                                    emptyStep.AethernetShortcut)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.AetherCurrentId), step.AetherCurrentId,
                                    emptyStep.AetherCurrentId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.ItemId), step.ItemId, emptyStep.ItemId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.GroundTarget), step.GroundTarget,
                                    emptyStep.GroundTarget)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.ItemCount), step.ItemCount, emptyStep.ItemCount)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Emote), step.Emote, emptyStep.Emote)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.ChatMessage), step.ChatMessage,
                                    emptyStep.ChatMessage)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Action), step.Action, emptyStep.Action)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.Status), step.Status, emptyStep.Status)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.TargetClass), step.TargetClass,
                                    emptyStep.TargetClass)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.TaxiStandId), step.TaxiStandId,
                                    emptyStep.TaxiStandId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.EnemySpawnType), step.EnemySpawnType,
                                    emptyStep.EnemySpawnType)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.KillEnemyDataIds), step.KillEnemyDataIds)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.ComplexCombatData), step.ComplexCombatData)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.CombatItemUse), step.CombatItemUse,
                                    emptyStep.CombatItemUse)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.CombatDelaySecondsAtStart),
                                    step.CombatDelaySecondsAtStart,
                                    emptyStep.CombatDelaySecondsAtStart)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.JumpDestination), step.JumpDestination,
                                    emptyStep.JumpDestination)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.DutyOptions), step.DutyOptions,
                                    emptyStep.DutyOptions)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.SinglePlayerDutyOptions), step.SinglePlayerDutyOptions,
                                emptyStep.SinglePlayerDutyOptions)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.SkipConditions), step.SkipConditions,
                                    emptyStep.SkipConditions)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.RequiredQuestVariables),
                                    step.RequiredQuestVariables)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.RequiredCurrentJob),
                                    step.RequiredCurrentJob)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.RequiredQuestAcceptedJob),
                                    step.RequiredQuestAcceptedJob)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.ItemsToGather),
                                step.ItemsToGather),
                            AssignmentList(nameof(QuestStep.CompletionQuestVariablesFlags),
                                    step.CompletionQuestVariablesFlags)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.DialogueChoices), step.DialogueChoices)
                                .AsSyntaxNodeOrToken(),
                            AssignmentList(nameof(QuestStep.PointMenuChoices), step.PointMenuChoices)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.PurchaseMenu), step.PurchaseMenu, emptyStep.PurchaseMenu)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.PickUpQuestId), step.PickUpQuestId,
                                    emptyStep.PickUpQuestId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.TurnInQuestId), step.TurnInQuestId,
                                    emptyStep.TurnInQuestId)
                                .AsSyntaxNodeOrToken(),
                            Assignment(nameof(QuestStep.NextQuestId), step.NextQuestId,
                                    emptyStep.NextQuestId)
                                .AsSyntaxNodeOrToken()))));
    }
}
