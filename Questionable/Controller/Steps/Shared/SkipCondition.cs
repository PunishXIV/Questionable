using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class SkipCondition
{
    internal static CraftingItem? GetItemTarget(ElementId questId, QuestStep step)
    {
        if (step.ItemId == null)
            return null;

        var target = new CraftingItem(step.ItemId.Value, step.ItemCount ?? 0, step.ItemQuality);
        return step.InteractionType == EInteractionType.Craft && step.ItemCount is > 0
            ? QuestCraftingRequirements.ResolveItem(questId, target)
            : target;
    }

    internal sealed class Factory(Configuration configuration) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            SkipStepConditions? skipConditions = step.SkipConditions?.StepIf;
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
            {
                return null;
            }

            return new Task(step, skipConditions ?? new(), quest.Id);
        }
    }

    internal sealed record Task
    (
        QuestStep Step,
        SkipStepConditions SkipConditions,
        ElementId ElementId) : ITask
    {
        public override string ToString() => "CheckSkip";
    }

    internal sealed class Executor
    (
        ILogger<Executor> logger,
        Configuration configuration,
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        ExtraConditionUtils extraConditionUtils,
        ClassJobUtils classJobUtils) : TaskExecutor<Task>
    {
        protected override bool Start()
        {
            SkipStepConditions skipConditions = Task.SkipConditions;
            QuestStep step = Task.Step;
            ElementId elementId = Task.ElementId;

            logger.LogInformation("Checking skip conditions; {ConfiguredConditions}", string.Join(",", skipConditions));

            if (CheckFlying(step, skipConditions))
                return true;

            if (CheckChocobo(skipConditions))
                return true;

            if (CheckDiving(skipConditions))
                return true;

            if (CheckTerritory(skipConditions))
                return true;

            if (CheckQuests(skipConditions))
                return true;

            if (CheckTargetable(step, skipConditions))
                return true;

            if (CheckNameplate(step, skipConditions))
                return true;

            if (CheckDutiesUnlocked(skipConditions))
                return true;

            if (CheckDutiesCompleted(skipConditions))
                return true;

            if (CheckItem(step, skipConditions))
                return true;

            if (CheckAetheryte(step, skipConditions))
                return true;

            if (CheckAetherCurrent(step))
                return true;

            if (CheckQuestWork(elementId, step))
                return true;

            if (CheckJob(elementId, step))
                return true;

            if (CheckPosition(skipConditions))
                return true;

            if (skipConditions.ExtraCondition != null && skipConditions.ExtraCondition != EExtraSkipCondition.None &&
                extraConditionUtils.Matches(skipConditions.ExtraCondition.Value))
            {
                logger.LogInformation("Skipping step, extra condition {ExtraCondition} matches", skipConditions.ExtraCondition);
                return true;
            }

            if (CheckPickUpTurnIn(step))
                return true;

            if (CheckTaxiStand(step))
                return true;

            return false;
        }

        private bool CheckFlying(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.Flying == ELockedSkipCondition.Unlocked &&
                GameFunctions.IsFlyingUnlocked(step.TerritoryId))
            {
                logger.LogInformation("Skipping step, as flying is unlocked");
                return true;
            }

            if (skipConditions.Flying == ELockedSkipCondition.Locked &&
                !GameFunctions.IsFlyingUnlocked(step.TerritoryId))
            {
                logger.LogInformation("Skipping step, as flying is locked");
                return true;
            }

            return false;
        }

        private unsafe bool CheckChocobo(SkipStepConditions skipConditions)
        {
            if (skipConditions.Chocobo == ELockedSkipCondition.Unlocked &&
                PlayerState.Instance()->IsMountUnlocked(1))
            {
                logger.LogInformation("Skipping step, as chocobo is unlocked");
                return true;
            }

            return false;
        }

        private bool CheckTerritory(SkipStepConditions skipConditions)
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

        private bool CheckDiving(SkipStepConditions skipConditions)
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

        private bool CheckQuests(SkipStepConditions skipConditions)
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

        private bool CheckTargetable(QuestStep step, SkipStepConditions skipConditions)
        {
            if (skipConditions.NotTargetable &&
                step is { DataId: not null })
            {
                IGameObject? gameObject = gameFunctions.FindObjectByDataId(step.DataId.Value);
                if (gameObject == null)
                {
                    if ((step.Position.GetValueOrDefault() - objectTable[0]!.Position).Length() < 100)
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

        private unsafe bool CheckNameplate(QuestStep step, SkipStepConditions skipConditions)
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

        private bool CheckDutiesUnlocked(SkipStepConditions skipConditions)
        {
            if (skipConditions.DutiesUnlocked.Count > 0)
            {
                if (skipConditions.DutiesUnlocked.All(x =>
                        gameFunctions.CfcToContentId.TryGetValue(x, out var contentId) &&
                        UIState.IsInstanceContentUnlocked(contentId)))
                    return true;
            }

            return false;
        }

        private bool CheckDutiesCompleted(SkipStepConditions skipConditions)
        {
            if (skipConditions.DutiesCompleted.Count > 0)
            {
                if (skipConditions.DutiesCompleted.All(x =>
                        gameFunctions.CfcToContentId.TryGetValue(x, out var contentId) &&
                        UIState.IsInstanceContentCompleted(contentId)))
                    return true;
            }

            return false;
        }

        private unsafe bool CheckItem(QuestStep step, SkipStepConditions skipConditions)
        {
            CraftingItem? target = GetItemTarget(Task.ElementId, step);
            if (target == null)
                return false;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            int itemCount = 0;
            switch (target.Quality)
            {
                case EItemQuality.NQ:
                    itemCount = inventoryManager->GetInventoryItemCount(target.ItemId, isHq: false, checkEquipped: false);
                    break;
                case EItemQuality.HQ:
                    itemCount = inventoryManager->GetInventoryItemCount(target.ItemId, isHq: true, checkEquipped: false);
                    break;
                case EItemQuality.Any:
                    itemCount = inventoryManager->GetInventoryItemCount(target.ItemId, isHq: false, checkEquipped: false)
                                + inventoryManager->GetInventoryItemCount(target.ItemId, isHq: true, checkEquipped: false);
                    if (step.InteractionType == EInteractionType.Craft)
                        itemCount += inventoryManager->GetInventoryItemCount(target.ItemId + 500000, isHq: false, checkEquipped: false);
                    break;
            }

            if (itemCount == 0 && skipConditions.Item is { NotInInventory: true })
            {
                logger.LogInformation("Skipping step, no item with itemId {ItemId} in inventory",
                    target.ItemId);
                return true;
            }

            if (itemCount > 0 && skipConditions.Item is { NotInInventory: false })
            {
                if (step.ItemCount != null && itemCount >= target.ItemCount)
                {
                    logger.LogInformation("Skipping step, item with itemId {ItemId} in inventory",
                        target.ItemId);
                    return true;
                }
            }

            return false;
        }

        private bool CheckAetheryte(QuestStep step, SkipStepConditions skipConditions)
        {
            if (step is { Aetheryte: { } aetheryteLocation, InteractionType: EInteractionType.AttuneAetheryte } &&
                aetheryteFunctions.IsAetheryteUnlocked(aetheryteLocation))
            {
                logger.LogInformation("Skipping step, as aetheryte is unlocked");
                return true;
            }

            if (step is { Aetheryte: { } aethernetShard, InteractionType: EInteractionType.AttuneAethernetShard } &&
                aetheryteFunctions.IsAetheryteUnlocked(aethernetShard))
            {
                logger.LogInformation("Skipping step, as aethernet shard is unlocked");
                return true;
            }

            if (step is
                {
                    Aetheryte: { } favoredAetheryte, InteractionType: EInteractionType.RegisterFreeOrFavoredAetheryte
                } &&
                aetheryteFunctions.CanRegisterFreeOrFavored(favoredAetheryte) is AetheryteRegistrationResult.NotPossible)
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

        private bool CheckAetherCurrent(QuestStep step)
        {
            if (step is { InteractionType: EInteractionType.AttuneAetherCurrent } &&
                configuration.Advanced.SkipAetherCurrents)
            {
                logger.LogInformation("Skipping step, as aether currents should be skipped");
                return true;
            }

            if (step is { DataId: not null, InteractionType: EInteractionType.AttuneAetherCurrent } &&
                gameFunctions.IsAetherCurrentUnlocked(step.DataId.Value))
            {
                logger.LogInformation("Skipping step, as current is unlocked");
                return true;
            }

            return false;
        }

        private bool CheckQuestWork(ElementId elementId, QuestStep step)
        {
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(elementId);
            if (questWork != null)
            {
                if (QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags) &&
                    QuestWorkUtils.Matches(step.CompletionQuestVariablesFlags, questWork))
                {
                    logger.LogInformation("Skipping step, as quest variables match (step is complete)");
                    return true;
                }

                if (step is { SkipConditions.StepIf: { } conditions })
                {
                    if (QuestWorkUtils.Matches(conditions.CompletionQuestVariablesFlags, questWork))
                    {
                        logger.LogInformation("Skipping step, as quest variables match (step can be skipped)");
                        return true;
                    }
                }

                if (step is { RequiredQuestVariables: { } requiredQuestVariables })
                {
                    if (!QuestWorkUtils.MatchesRequired(requiredQuestVariables, questWork, logger))
                    {
                        logger.LogInformation("Skipping step, as required variables do not match");
                        return true;
                    }
                }

                if (step is { RequiredQuestAcceptedJob.Count: > 0 })
                {
                    List<Job> expectedJobs = step.RequiredQuestAcceptedJob
                        .SelectMany(x => classJobUtils.AsIndividualJobs(x, elementId)).ToList();
                    Job questJob = questWork.ClassJob;
                    logger.LogInformation("Checking quest job {QuestJob} against {ExpectedJobs}", questJob,
                        string.Join(',', expectedJobs));
                    if (questJob != Job.ADV && !expectedJobs.Contains(questJob))
                    {
                        logger.LogInformation("Skipping step, as quest was accepted on a different job");
                        return true;
                    }
                }
            }

            return false;
        }

        private unsafe bool CheckJob(ElementId elementId, QuestStep step)
        {
            if (step is { RequiredCurrentJob.Count: > 0 })
            {
                List<Job> expectedJobs =
                    step.RequiredCurrentJob.SelectMany(x => classJobUtils.AsIndividualJobs(x, elementId)).ToList();
                Job currentJob = (Job)PlayerState.Instance()->CurrentClassJobId;
                logger.LogInformation("Checking current job {CurrentJob} against {ExpectedJobs}", currentJob,
                    string.Join(',', expectedJobs));
                if (!expectedJobs.Contains(currentJob))
                {
                    logger.LogInformation("Skipping step, as step requires a different job");
                    return true;
                }
            }

            return false;
        }

        private bool CheckPosition(SkipStepConditions skipConditions)
        {
            if (skipConditions.NearPosition is { } nearPosition &&
                clientState.TerritoryType == nearPosition.TerritoryId)
            {
                if (Vector3.Distance(nearPosition.Position, objectTable[0]!.Position) <=
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
                    Vector3.Distance(notNearPosition.Position, objectTable[0]!.Position))
                {
                    logger.LogInformation("Skipping step, as we're not near the position");
                    return true;
                }
            }

            return false;
        }

        private bool CheckPickUpTurnIn(QuestStep step)
        {
            if (step.PickUpQuestId != null)
            {
                if (questFunctions.IsQuestAcceptedOrComplete(step.PickUpQuestId))
                {
                    logger.LogInformation("Skipping step, as we have already picked up the relevant quest");
                    return true;
                }

                if (QuestData.AetherCurrentQuests.Contains(step.PickUpQuestId) &&
                    (configuration.Advanced.SkipAetherCurrents ||
                     GameFunctions.IsFlyingUnlocked(step.TerritoryId))) // story skip apparently makes 1748 impossible to complete -alydev
                {
                    logger.LogInformation("Skipping step, as aether current quests should be skipped");
                    return true;
                }

                if (configuration.Advanced.SkipARealmRebornHardModePrimals &&
                    QuestData.HardModePrimals.Contains(step.PickUpQuestId))
                {
                    logger.LogInformation("Skipping step, as hard mode primal quests should be skipped");
                    return true;
                }
            }
            if (step.TurnInQuestId != null)
            {
                if (questFunctions.IsQuestComplete(step.TurnInQuestId))
                {
                    logger.LogInformation("Skipping step, as we have already completed the relevant quest");
                    return true;
                }
            }

            return false;
        }

        private unsafe bool CheckTaxiStand(QuestStep step)
        {
            UIState* uiState = UIState.Instance();
            if (step.TaxiStandId is not null)
            {
                uint? taxiStandId = step.TaxiStandId;
                if ((int)taxiStandId < 0)
                    taxiStandId += 0x120000u;
                if (uiState->IsChocoboTaxiStandUnlocked(taxiStandId.Value))
                {
                    logger.LogInformation("Skipping step, as taxi stand {TaxiStandId} is unlocked", taxiStandId);
                    return true;
                }
            }

            return false;
        }

        public override ETaskResult Update() => ETaskResult.SkipRemainingTasksForStep;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
