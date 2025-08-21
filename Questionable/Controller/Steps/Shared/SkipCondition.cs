using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LLib.GameData;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class SkipCondition
{
    internal sealed class Factory(Configuration configuration) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            var skipConditions = step.SkipConditions?.StepIf;
            if (skipConditions is { Never: true })
                return null;

            if ((skipConditions == null || !skipConditions.HasSkipConditions()) &&
                !QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags) &&
                step.RequiredQuestVariables.Count == 0 &&
                step.TaxiStandId == null &&
                step.PickUpQuestId == null &&
                step.NextQuestId == null &&
                step.RequiredCurrentJob.Count == 0 &&
                step.RequiredQuestAcceptedJob.Count == 0 &&
                !(step.InteractionType == EInteractionType.AttuneAetherCurrent && configuration.Advanced.SkipAetherCurrents))
                return null;

            return new SkipTask(step, skipConditions ?? new(), quest.Id);
        }
    }

    internal sealed record SkipTask(
        QuestStep Step,
        SkipStepConditions SkipConditions,
        ElementId ElementId) : ITask
    {
        public override string ToString() => "CheckSkip";
    }

    internal sealed class CheckSkip(
        ILogger<CheckSkip> logger,
        Configuration configuration,
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        IClientState clientState,
        ICondition condition,
        ExtraConditionUtils extraConditionUtils,
        ClassJobUtils classJobUtils) : TaskExecutor<SkipTask>
    {
        protected override bool Start()
        {
            var skipConditions = Task.SkipConditions;
            var step = Task.Step;
            var elementId = Task.ElementId;

            logger.LogInformation("Checking skip conditions; {ConfiguredConditions}", string.Join(",", skipConditions));

            if (CheckFlyingCondition(step, skipConditions))
                return true;

            if (CheckUnlockedMountCondition(skipConditions))
                return true;

            if (CheckDivingCondition(skipConditions))
                return true;

            if (CheckTerritoryCondition(skipConditions))
                return true;

            if (CheckQuestConditions(skipConditions))
                return true;

            if (CheckTargetableCondition(step, skipConditions))
                return true;

            if (CheckNameplateCondition(step, skipConditions))
                return true;

            if (CheckItemCondition(step, skipConditions))
                return true;

            if (CheckAetheryteCondition(step, skipConditions))
                return true;

            if (CheckAetherCurrentCondition(step))
                return true;

            if (CheckQuestWorkConditions(elementId, step))
                return true;

            if (CheckJobCondition(elementId, step))
                return true;

            if (CheckPositionCondition(skipConditions))
                return true;

            if (skipConditions.ExtraCondition != null && skipConditions.ExtraCondition != EExtraSkipCondition.None &&
                extraConditionUtils.MatchesExtraCondition(skipConditions.ExtraCondition.Value))
            {
                logger.LogInformation("Skipping step, extra condition {} matches", skipConditions.ExtraCondition);
                return true;
            }

            if (CheckPickUpTurnInQuestIds(step))
                return true;

            if (CheckTaxiStandUnlocked(step))
                return true;

            return false;
        }

        private bool CheckFlyingCondition(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.Flying == ELockedSkipCondition.Unlocked &&
                gameFunctions.IsFlyingUnlocked(step.TerritoryId))
            {
                logger.LogInformation("Skipping step, as flying is unlocked");
                return true;
            }

            if (skipConditions.Flying == ELockedSkipCondition.Locked &&
                !gameFunctions.IsFlyingUnlocked(step.TerritoryId))
            {
                logger.LogInformation("Skipping step, as flying is locked");
                return true;
            }

            return false;
        }

        private unsafe bool CheckUnlockedMountCondition(SkipStepConditions skipConditions)
        {
            if (skipConditions.Chocobo == ELockedSkipCondition.Unlocked &&
                PlayerState.Instance()->IsMountUnlocked(1))
            {
                logger.LogInformation("Skipping step, as chocobo is unlocked");
                return true;
            }

            return false;
        }

        private bool CheckTerritoryCondition(SkipStepConditions skipConditions)
        {
            if (skipConditions.InTerritory.Count > 0 &&
                skipConditions.InTerritory.Contains(clientState.TerritoryType))
            {
                logger.LogInformation("Skipping step, as in a skip.InTerritory");
                return true;
            }

            if (skipConditions.NotInTerritory.Count > 0 &&
                !skipConditions.NotInTerritory.Contains(clientState.TerritoryType))
            {
                logger.LogInformation("Skipping step, as not in a skip.NotInTerritory");
                return true;
            }

            return false;
        }

        private bool CheckDivingCondition(SkipStepConditions skipConditions)
        {
            if (skipConditions.Diving == true && condition[ConditionFlag.Diving])
            {
                logger.LogInformation("Skipping step, as you're currently diving underwater");
                return true;
            }

            if (skipConditions.Diving == false && !condition[ConditionFlag.Diving])
            {
                logger.LogInformation("Skipping step, as you're not currently diving underwater");
                return true;
            }

            return false;
        }

        private bool CheckQuestConditions(SkipStepConditions skipConditions)
        {
            if (skipConditions.QuestsCompleted.Count > 0 &&
                skipConditions.QuestsCompleted.All(questFunctions.IsQuestComplete))
            {
                logger.LogInformation("Skipping step, all prequisite quests are complete");
                return true;
            }

            if (skipConditions.QuestsAccepted.Count > 0 &&
                skipConditions.QuestsAccepted.All(questFunctions.IsQuestAccepted))
            {
                logger.LogInformation("Skipping step, all prequisite quests are accepted");
                return true;
            }

            return false;
        }

        private bool CheckTargetableCondition(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.NotTargetable &&
                step is { DataId: not null })
            {
                IGameObject? gameObject = gameFunctions.FindObjectByDataId(step.DataId.Value);
                if (gameObject == null)
                {
                    if ((step.Position.GetValueOrDefault() - clientState.LocalPlayer!.Position).Length() < 100)
                    {
                        logger.LogInformation("Skipping step, object is not nearby (but we are)");
                        return true;
                    }
                }
                else if (!gameObject.IsTargetable)
                {
                    logger.LogInformation("Skipping step, object is not targetable");
                    return true;
                }
            }

            return false;
        }

        private unsafe bool CheckNameplateCondition(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.NotNamePlateIconId.Count > 0 &&
                step is { DataId: not null })
            {
                IGameObject? target = gameFunctions.FindObjectByDataId(step.DataId.Value);
                if (target != null)
                {
                    GameObject* gameObject = (GameObject*)target.Address;
                    if (!skipConditions.NotNamePlateIconId.Contains(gameObject->NamePlateIconId))
                    {
                        logger.LogInformation("Skipping step, object has icon id {IconId}",
                            gameObject->NamePlateIconId);
                        return true;
                    }
                }
            }

            return false;
        }

        private unsafe bool CheckItemCondition(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.Item is { NotInInventory: true } && step is { ItemId: not null })
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager->GetInventoryItemCount(step.ItemId.Value) == 0 &&
                    inventoryManager->GetInventoryItemCount(step.ItemId.Value, true) == 0)
                {
                    logger.LogInformation("Skipping step, no item with itemId {ItemId} in inventory",
                        step.ItemId.Value);
                    return true;
                }
            }

            return false;
        }

        private bool CheckAetheryteCondition(QuestStep step, SkipStepConditions skipConditions)
        {
            if (step is { Aetheryte: {} aetheryteLocation, InteractionType: EInteractionType.AttuneAetheryte } &&
                aetheryteFunctions.IsAetheryteUnlocked(aetheryteLocation))
            {
                logger.LogInformation("Skipping step, as aetheryte is unlocked");
                return true;
            }

            if (step is { Aetheryte: {} aethernetShard, InteractionType: EInteractionType.AttuneAethernetShard } &&
                aetheryteFunctions.IsAetheryteUnlocked(aethernetShard))
            {
                logger.LogInformation("Skipping step, as aethernet shard is unlocked");
                return true;
            }

            if (step is
                {
                    Aetheryte: { } favoredAetheryte, InteractionType: EInteractionType.RegisterFreeOrFavoredAetheryte
                } &&
                aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(favoredAetheryte) is AetheryteRegistrationResult.NotPossible)
            {
                logger.LogInformation("Skipping step, already registered all possible free or favored aetherytes");
                return true;
            }

            if (skipConditions.AetheryteLocked != null &&
                !aetheryteFunctions.IsAetheryteUnlocked(skipConditions.AetheryteLocked.Value))
            {
                logger.LogInformation("Skipping step, as aetheryte is locked");
                return true;
            }

            if (skipConditions.AetheryteUnlocked != null &&
                aetheryteFunctions.IsAetheryteUnlocked(skipConditions.AetheryteUnlocked.Value))
            {
                logger.LogInformation("Skipping step, as aetheryte is unlocked");
                return true;
            }

            return false;
        }

        private bool CheckAetherCurrentCondition(QuestStep step)
        {
            if (step is { DataId: not null, InteractionType: EInteractionType.AttuneAetherCurrent } &&
                gameFunctions.IsAetherCurrentUnlocked(step.DataId.Value))
            {
                logger.LogInformation("Skipping step, as current is unlocked");
                return true;
            }

            if (step is { InteractionType: EInteractionType.AttuneAetherCurrent } &&
                configuration.Advanced.SkipAetherCurrents)
            {
                logger.LogInformation("Skipping step, as aether currents should be skipped");
                return true;
            }

            return false;
        }

        private bool CheckQuestWorkConditions(ElementId elementId, QuestStep step)
        {
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(elementId);
            if (questWork != null)
            {
                if (QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags) &&
                    QuestWorkUtils.MatchesQuestWork(step.CompletionQuestVariablesFlags, questWork))
                {
                    logger.LogInformation("Skipping step, as quest variables match (step is complete)");
                    return true;
                }

                if (step is { SkipConditions.StepIf: { } conditions })
                {
                    if (QuestWorkUtils.MatchesQuestWork(conditions.CompletionQuestVariablesFlags, questWork))
                    {
                        logger.LogInformation("Skipping step, as quest variables match (step can be skipped)");
                        return true;
                    }
                }

                if (step is { RequiredQuestVariables: { } requiredQuestVariables })
                {
                    if (!QuestWorkUtils.MatchesRequiredQuestWorkConfig(requiredQuestVariables, questWork, logger))
                    {
                        logger.LogInformation("Skipping step, as required variables do not match");
                        return true;
                    }
                }

                if (step is { RequiredQuestAcceptedJob.Count: > 0 })
                {
                    List<EClassJob> expectedJobs = step.RequiredQuestAcceptedJob
                        .SelectMany(x => classJobUtils.AsIndividualJobs(x, elementId)).ToList();
                    EClassJob questJob = questWork.ClassJob;
                    logger.LogInformation("Checking quest job {QuestJob} against {ExpectedJobs}", questJob,
                        string.Join(",", expectedJobs));
                    if (questJob != EClassJob.Adventurer && !expectedJobs.Contains(questJob))
                    {
                        logger.LogInformation("Skipping step, as quest was accepted on a different job");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CheckJobCondition(ElementId elementId, QuestStep step)
        {
            if (step is { RequiredCurrentJob.Count: > 0 })
            {
                List<EClassJob> expectedJobs =
                    step.RequiredCurrentJob.SelectMany(x => classJobUtils.AsIndividualJobs(x, elementId)).ToList();
                EClassJob currentJob = (EClassJob)clientState.LocalPlayer!.ClassJob.RowId;
                logger.LogInformation("Checking current job {CurrentJob} against {ExpectedJobs}", currentJob,
                    string.Join(",", expectedJobs));
                if (!expectedJobs.Contains(currentJob))
                {
                    logger.LogInformation("Skipping step, as step requires a different job");
                    return true;
                }
            }

            return false;
        }

        private bool CheckPositionCondition(SkipStepConditions skipConditions)
        {
            if (skipConditions.NearPosition is { } nearPosition &&
                clientState.TerritoryType == nearPosition.TerritoryId)
            {
                if (Vector3.Distance(nearPosition.Position, clientState.LocalPlayer!.Position) <=
                    nearPosition.MaximumDistance)
                {
                    logger.LogInformation("Skipping step, as we're near the position");
                    return true;
                }
            }

            if (skipConditions.NotNearPosition is { } notNearPosition &&
                clientState.TerritoryType == notNearPosition.TerritoryId)
            {
                if (notNearPosition.MaximumDistance <=
                    Vector3.Distance(notNearPosition.Position, clientState.LocalPlayer!.Position))
                {
                    logger.LogInformation("Skipping step, as we're not near the position");
                    return true;
                }
            }

            return false;
        }

        private bool CheckPickUpTurnInQuestIds(QuestStep step)
        {
            if (step.PickUpQuestId != null && questFunctions.IsQuestAcceptedOrComplete(step.PickUpQuestId))
            {
                logger.LogInformation("Skipping step, as we have already picked up the relevant quest");
                return true;
            }

            if (step.TurnInQuestId != null && questFunctions.IsQuestComplete(step.TurnInQuestId))
            {
                logger.LogInformation("Skipping step, as we have already completed the relevant quest");
                return true;
            }

            if (step.PickUpQuestId != null &&
                configuration.Advanced.SkipAetherCurrents &&
                QuestData.AetherCurrentQuests.Contains(step.PickUpQuestId))
            {
                logger.LogInformation("Skipping step, as aether current quests should be skipped");
                return true;
            }

            if (step.PickUpQuestId != null &&
                configuration.Advanced.SkipARealmRebornHardModePrimals &&
                QuestData.HardModePrimals.Contains(step.PickUpQuestId))
            {
                logger.LogInformation("Skipping step, as hard mode primal quests should be skipped");
                return true;
            }

            return false;
        }

        private unsafe bool CheckTaxiStandUnlocked(QuestStep step)
        {
            UIState* uiState = UIState.Instance();
            if (step.TaxiStandId is { } taxiStandId &&
                uiState->IsChocoboTaxiStandUnlocked(taxiStandId))
            {
                logger.LogInformation("Skipping step, as taxi stand {TaxiStandId} is unlocked", taxiStandId);
                return true;
            }

            return false;
        }

        public override ETaskResult Update() => ETaskResult.SkipRemainingTasksForStep;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
