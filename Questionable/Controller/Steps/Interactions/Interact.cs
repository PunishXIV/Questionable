using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Questionable.Controller.Steps.Interactions;

// TODO: refactor — heavy nesting (22 lines indented ≥6 levels, max indent ~12 levels).
internal static class Interact
{
    internal sealed class Factory(AutomatonIpc automatonIpc, Configuration configuration, RedoUtil redoUtil) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
                or EInteractionType.SinglePlayerDuty)
            {
                if (step.InteractionType is EInteractionType.AcceptQuest && sequence.Sequence > 0 && redoUtil.IsRedoActive())
                {
                    // Can't accept other quests during NG+
                    yield break;
                }
                if (step.InteractionType is EInteractionType.CompleteQuest ||
                    (step.InteractionType is EInteractionType.AcceptQuest && quest.GetQuestInfo().CompletesInstantly)) // instant quest
                {
                    yield return new LogQuestCompletion.Task(quest);
                    if (configuration.Advanced.PreventQuestCompletion)
                    {
                        if (configuration.Advanced.AbandonQuestBeforeCompletion)
                            yield return new AbandonQuest.Task(quest);
                        yield break;
                    }
                }

                if (step.Emote != null)
                    yield break;

                if (step.ChatMessage != null)
                    yield break;

                if (step.ItemId != null)
                    yield break;

                if (step.DataId == null)
                    yield break;
            }
            else if (step.InteractionType == EInteractionType.PurchaseItem)
            {
                if (step.DataId == null)
                    yield break;
            }
            else if (step.InteractionType == EInteractionType.Snipe)
            {
                if (!automatonIpc.IsAutoSnipeEnabled)
                    yield break;
            }
            else if (step.InteractionType == EInteractionType.UnlockTaxiStand)
            {
                if (step.TaxiStandId == null)
                    yield break;
            }
            else if (step.InteractionType != EInteractionType.Interact)
                yield break;
            if (!step.DataId.HasValue)
                throw new ArgumentNullException(nameof(step.DataId));

            // if we're fast enough, it is possible to get the smalltalk prompt
            if (sequence.Sequence == 0 && sequence.Steps.IndexOf(step) == 0)
                yield return new WaitAtEnd.WaitDelay();

            yield return new Task(
                step.DataId.Value,
                quest,
                step.InteractionType,
                step.TargetTerritoryId != null || quest.Id is SatisfactionSupplyNpcId ||
                step.SkipConditions is { StepIf.Never: true } || step.InteractionType == EInteractionType.PurchaseItem || step.DataId == 1052475,
                step.PickUpItemId,
                step.TaxiStandId,
                step.SkipConditions?.StepIf,
                step.CompletionQuestVariablesFlags);
        }
    }

    internal sealed record Task
    (
        uint DataId,
        Quest? Quest,
        EInteractionType InteractionType,
        bool SkipMarkerCheck = false,
        uint? PickUpItemId = null,
        uint? TaxiStandId = null,
        SkipStepConditions? SkipConditions = null,
        List<QuestWorkValue?>? CompletionQuestVariablesFlags = null) : ITask
    {
        public List<QuestWorkValue?> CompletionQuestVariablesFlags { get; } = CompletionQuestVariablesFlags ?? [];

        public bool HasCompletionQuestVariablesFlags { get; } =
            Quest != null &&
            CompletionQuestVariablesFlags != null &&
            QuestWorkUtils.HasCompletionFlags(CompletionQuestVariablesFlags);

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"Interact{(HasCompletionQuestVariablesFlags ? "*" : "")}({DataId})";
    }

    internal sealed class DoInteract
    (
        GameFunctions gameFunctions,
        CameraFunctions cameraFunctions,
        Configuration configuration,
        ICondition condition,
        IObjectTable objectTable,
        ClassJobUtils classJobUtils,
        ILogger<DoInteract> logger)
        : TaskExecutor<Task>, IConditionChangeAware
    {
        private DateTime _continueAt = DateTime.MinValue;
        private EInteractionState _interactionState = EInteractionState.None;
        private bool _needsFacing;
        private bool _needsUnmount;
        private bool _reportedGameObjNull;
        private ushort _unequipItem;

        /// <summary>
        ///     A slight delay when we think an interaction has ended, to make sure that we're processing "Action cancelled"
        ///     prior to the next step (in case we're attacked).
        /// </summary>
        private bool delayedFinalCheck;

        public Quest? Quest => Task.Quest;
        public EInteractionType InteractionType { get; set; }

        public override ETaskResult Update()
        {
            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;

            if (_unequipItem != 0)
            {
                logger.LogDebug($"unequipping item {_unequipItem}");
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    var armoryType = InventoryType.ArmorySoulCrystal;
                    ushort srcSlot = 13;
                    InventoryContainer* armoryContainer = inventoryManager->GetInventoryContainer(armoryType);
                    if (UnequipItem.DoUnequip.TryFindFirstEmptySlot(armoryContainer, out ushort targetSlot))
                    {
                        logger.LogDebug($"Moving {_unequipItem} to {targetSlot} in soul crystal inv");
                        _ = inventoryManager->MoveItemSlot(InventoryType.EquippedItems, srcSlot, armoryType, targetSlot, a6: true);
                        //if (result != 0)
                        //    throw new Exception($"UnequipItem failed to move {_unequipItem} to {targetSlot} in soul crystal inv");
                    }
                    else
                    {
                        logger.LogWarning("Armory container {ArmoryType} is full, cannot unequip item {ItemId}",
                            armoryType, _unequipItem);
                        throw new Exception("Unable to unequip gear - armory chest is full.");
                    }
                }
                _unequipItem = 0;
            }
            if (_needsUnmount)
            {
                if (condition[ConditionFlag.Mounted])
                {
                    gameFunctions.Unmount();
                    _continueAt = DateTime.Now.AddSeconds(1);
                    return ETaskResult.StillRunning;
                }

                _needsUnmount = false;
            }
            else if (Task.PickUpItemId is { } pickUpItemId)
            {
                logger.LogDebug("PickUpItemId {PickUpItemId}", pickUpItemId);
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetInventoryItemCount(pickUpItemId) > 0)
                        return ETaskResult.TaskComplete;
                }
            }
            else if (Task.TaxiStandId is { } taxiStandId)
            {
                logger.LogDebug("TaxiStandId {TaxiStandId}", taxiStandId);
                unsafe
                {
                    UIState* uiState = UIState.Instance();
                    if (uiState->IsChocoboTaxiStandUnlocked(taxiStandId))
                        return ETaskResult.TaskComplete;
                }
            }
            else if (InteractionType == EInteractionType.Gather && condition[ConditionFlag.Gathering])
                return ETaskResult.TaskComplete;
            else if (Quest != null && Task.HasCompletionQuestVariablesFlags)
            {
                QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(Quest.Id);

                if (questWork != null && QuestWorkUtils.MatchesQuestWork(Task.CompletionQuestVariablesFlags, questWork))
                    return ETaskResult.TaskComplete;
            }
            else if (ProgressContext != null)
            {
                if (ProgressContext.WasInterrupted())
                    return ETaskResult.StillRunning;

                if (ProgressContext.WasSuccessful() ||
                                         _interactionState == EInteractionState.InteractionConfirmed)
                {
                    if (delayedFinalCheck)
                        return ETaskResult.TaskComplete;

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    delayedFinalCheck = true;
                    return ETaskResult.StillRunning;
                }
            }

            IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId);
            //if (gameObject == null || !gameObject.IsTargetable || !HasAnyMarker(gameObject))
            if (gameObject == null)
            {
                if (!_reportedGameObjNull)
                {
                    logger.LogDebug("gameObject is null");
                    _reportedGameObjNull = true;
                }

                return ETaskResult.StillRunning;
            }

            _reportedGameObjNull = false;

            if (_needsFacing)
            {
                logger.LogInformation("Facing target");
                cameraFunctions.Face(gameObject.Position);
                _continueAt = DateTime.Now.AddSeconds(0.2);
                _needsFacing = false;
                return ETaskResult.StillRunning;
            }

            if (objectTable[0] is IPlayerCharacter player && Task.Quest != null && InteractionType == EInteractionType.AcceptQuest)
            {
                List<Job> acceptableJobs = [.. Task.Quest.Info.ClassJobs];
                Job playerJob = (Job)player.ClassJob.Value.RowId;
                Job targetJob = acceptableJobs[0];
                logger.LogDebug($"{Task.Quest.Id} acceptableJobs: {string.Join(',', acceptableJobs.Select(j => j.ToString()))}");
                if (acceptableJobs.Count >= 1 && !acceptableJobs.Contains(playerJob))
                {
                    if (!acceptableJobs[0].IsCrafter() && !acceptableJobs[0].IsGatherer())
                    {
                        targetJob = classJobUtils.LookupConfiguredJob(EExtendedClassJob.ConfiguredCombatJob);
                        if (acceptableJobs.Contains(targetJob))
                            acceptableJobs = [.. acceptableJobs.Prepend(targetJob)];
                        else
                            logger.LogInformation("Normal quest, but configured job {CombatJob} is not valid for {QuestId}, changing to {AcceptableJob}",
                                targetJob, Task.Quest.Id, acceptableJobs[0]);
                    }
                    if (acceptableJobs[0].IsCrafter())
                    {
                        targetJob = classJobUtils.LookupConfiguredJob(EExtendedClassJob.ConfiguredCraftingJob);
                        if (acceptableJobs.Contains(targetJob))
                            acceptableJobs = [.. acceptableJobs.Prepend(targetJob)];
                        else
                            logger.LogInformation("Crafting quest, but configured job {CraftingJob} is not valid for {QuestId}, changing to {AcceptableJob}",
                                targetJob, Task.Quest.Id, acceptableJobs[0]);
                    }
                    else if (acceptableJobs[0].IsGatherer())
                    {
                        targetJob = classJobUtils.LookupConfiguredJob(EExtendedClassJob.ConfiguredGatheringJob);
                        if (acceptableJobs.Contains(targetJob))
                            acceptableJobs = [.. acceptableJobs.Prepend(targetJob)];
                        else
                            logger.LogInformation("Gathering quest, but configured job {GatheringJob} is not valid for {QuestId}, changing to {AcceptableJob}",
                                targetJob, Task.Quest.Id, acceptableJobs[0]);
                    }
                    if (Task.Quest.Info.AlliedSociety.Equals(EAlliedSociety.Namazu))
                    {
                        if (configuration.Advanced.NamazuPreferCraft && !acceptableJobs[0].IsCrafter())
                            acceptableJobs = [.. acceptableJobs.Prepend(targetJob)];
                        else if (!configuration.Advanced.NamazuPreferCraft && !acceptableJobs[0].IsGatherer())
                            acceptableJobs = [.. acceptableJobs.Prepend(targetJob)];
                    }
                    targetJob = acceptableJobs[0];
                    if (classJobUtils.ClassToJobStone(targetJob) is (Job job, ushort item))
                    {
                        _unequipItem = item;
                        logger.LogInformation("Current job {ClassJob} is not valid for {QuestId}, changing to {AcceptableJob} via {MiddleJob}",
                            playerJob, Task.Quest.Id, targetJob, job);
                        targetJob = job;
                    }

                    if (!classJobUtils.SwitchClassJob(targetJob))
                    {
                        throw new Exception($"Quest {Task.Quest.Info.Name} requires a job like {targetJob}, " +
                                           "but you do not have a gearset for this job or have not configured QST job preferences.");
                    }
                    logger.LogInformation($"Switched from {playerJob} to {targetJob}");

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    return ETaskResult.StillRunning;
                }
            }

            bool isTargetable = gameObject.IsTargetable;
            bool hasAnyMarker = HasAnyMarker(gameObject);
            if (!isTargetable || !hasAnyMarker)
            {
                if (EzThrottler.Throttle("skipTarget", miliseconds: 1000))
                    logger.LogDebug($"IsTargetable: {isTargetable} / HasAnyMarker: {hasAnyMarker}");
                return ETaskResult.StillRunning;
            }

            TriggerInteraction(gameObject);
            return ETaskResult.StillRunning;
        }

        public void OnConditionChange(ConditionFlag flag, bool value)
        {
            if (ProgressContext != null && (ProgressContext.WasInterrupted() || ProgressContext.WasSuccessful()))
                return;

            logger.LogDebug("Condition change: {Flag} = {Value}", flag, value);
            if (_interactionState == EInteractionState.InteractionTriggered &&
                flag is ConditionFlag.OccupiedInQuestEvent or ConditionFlag.OccupiedInEvent &&
                value)
            {
                logger.LogInformation("Interaction was most likely triggered");
                _interactionState = EInteractionState.InteractionConfirmed;
            }
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            InteractionType = Task.InteractionType;

            _needsFacing = true;
            IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId);
            if (gameObject == null)
            {
                logger.LogWarning("No game object with dataId {DataId}", Task.DataId);
                return false;
            }

            if (!gameObject.IsTargetable && Task.SkipConditions is { Never: false, NotTargetable: true })
            {
                logger.LogInformation("Not interacting with {DataId} because it is not targetable (but skippable)",
                    Task.DataId);
                return false;
            }

            // this is only relevant for followers on quests
            if (!gameObject.IsTargetable && condition[ConditionFlag.Mounted] &&
                gameObject.ObjectKind != ObjectKind.GatheringPoint)
            {
                logger.LogInformation("Preparing interaction for {DataId} by unmounting", Task.DataId);
                _needsUnmount = true;
                gameFunctions.Unmount();
                _continueAt = DateTime.Now.AddSeconds(1);
                return true;
            }

            return true;
        }

        private void TriggerInteraction(IGameObject gameObject)
        {
            ProgressContext =
                InteractionProgressContext.FromActionUseOrDefault(() =>
                {
                    if (gameFunctions.InteractWith(gameObject))
                        _interactionState = EInteractionState.InteractionTriggered;
                    else
                        _interactionState = EInteractionState.None;
                    return _interactionState != EInteractionState.None;
                });
            _continueAt = DateTime.Now.AddSeconds(0.5);
        }

        private unsafe bool HasAnyMarker(IGameObject gameObject)
        {
            if (Task.SkipMarkerCheck || gameObject.ObjectKind != ObjectKind.EventNpc)
                return true;

            GameObject* gameObjectStruct = (GameObject*)gameObject.Address;
            return gameObjectStruct->NamePlateIconId != 0;
        }

        private enum EInteractionState
        {
            None,
            InteractionTriggered,
            InteractionConfirmed
        }
    }
}
