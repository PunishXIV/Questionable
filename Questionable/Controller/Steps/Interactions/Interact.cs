using System.Collections.ObjectModel;
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

internal static class Interact
{
    internal static Job SelectQuestJob(IReadOnlyList<Job> acceptableJobs, Configuration configuration,
        EAlliedSociety alliedSociety, bool isCrafter, bool isGatherer)
    {
        Job fallback = acceptableJobs[0];
        if (acceptableJobs.Count == 1)
            return fallback;

        Job preferred;
        if (isCrafter || (alliedSociety == EAlliedSociety.Namazu && configuration.Advanced.NamazuPreferCraft))
            preferred = configuration.General.CraftingJob;
        else if (isGatherer || alliedSociety == EAlliedSociety.Namazu)
            preferred = configuration.General.GatheringJob;
        else
            preferred = configuration.General.CombatJob;

        return acceptableJobs.Contains(preferred) ? preferred : fallback;
    }

    internal sealed class Factory(AutomatonIpc automatonIpc, BossModIpc bossModIpc, Configuration configuration, RedoUtil redoUtil,
        GilShopData gilShopData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
                or EInteractionType.SinglePlayerDuty)
            {
                if (step.InteractionType == EInteractionType.SinglePlayerDuty &&
                    bossModIpc.CanRunQuestBattle(quest.Id, step.SinglePlayerDutyOptions))
                    yield return new QuestBattle.LeaveParty();

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
                if (step.DataId == null || Purchase.IsHandledByKnightshopper(step, gilShopData))
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
                var skip = false;
                unsafe
                {
                    skip = PlayerState.Instance()->CurrentLevel < 10 || UIState.Instance()->IsChocoboTaxiStandUnlocked(step.TaxiStandId.Value);
                }
                if (skip)
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
        List<QuestWorkValue?>? CompletionFlags = null) : ITask
    {
        public List<QuestWorkValue?> CompletionFlags { get; } = CompletionFlags ?? [];

        public bool HasCompletionFlags { get; } =
            Quest != null &&
            CompletionFlags != null &&
            QuestWorkUtils.HasCompletionFlags(CompletionFlags);

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"Interact{(HasCompletionFlags ? "*" : "")}({DataId})";
    }

    internal sealed class Executor
    (
        GameFunctions gameFunctions,
        CameraFunctions cameraFunctions,
        Configuration configuration,
        IChatGui chatGui,
        ICondition condition,
        IObjectTable objectTable,
        ClassJobUtils classJobUtils,
        ILogger<Executor> logger,
        IQuestGameState gameState)
        : TaskExecutor<Task>, IConditionChangeAware
    {
        private DateTime _continueAt = DateTime.MinValue;
        private EInteractionState _interactionState = EInteractionState.None;
        private bool _needsFacing;
        private bool _needsUnmount;
        private bool _reportedNoObject;
        private string? _loggedAcceptableJobs;
        private bool _reportedWrongJob;
        private ushort _unequipItem;

        // Process a possible cancellation from combat before the next step.
        private bool _delayedFinalCheck;

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
                    if (EquipmentSlots.TryFindFirstEmptySlot(armoryContainer, out ushort targetSlot))
                    {
                        logger.LogDebug($"Moving {_unequipItem} to {targetSlot} in soul crystal inv");
                        _ = inventoryManager->MoveItemSlot(InventoryType.EquippedItems, srcSlot, armoryType, targetSlot, a6: true);
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
            else if (Quest != null && Task.HasCompletionFlags)
            {
                QuestProgressInfo? questWork = gameState.GetQuestProgressInfo(Quest.Id);

                if (questWork != null && QuestWorkUtils.Matches(Task.CompletionFlags, questWork))
                    return ETaskResult.TaskComplete;
            }
            else if (ProgressContext != null)
            {
                if (ProgressContext.WasInterrupted())
                    return ETaskResult.StillRunning;

                if (ProgressContext.WasSuccessful() || _interactionState == EInteractionState.Confirmed)
                {
                    if (_delayedFinalCheck)
                        return ETaskResult.TaskComplete;

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    _delayedFinalCheck = true;
                    return ETaskResult.StillRunning;
                }
            }

            IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId);
            if (gameObject == null)
            {
                if (!_reportedNoObject)
                {
                    logger.LogDebug("gameObject is null");
                    _reportedNoObject = true;
                }

                return ETaskResult.StillRunning;
            }

            _reportedNoObject = false;

            if (_needsFacing)
            {
                logger.LogInformation("Facing target");
                cameraFunctions.Face(gameObject.Position);
                _continueAt = DateTime.Now.AddSeconds(0.2);
                _needsFacing = false;
                return ETaskResult.StillRunning;
            }

            if (!_reportedWrongJob &&
                objectTable[0] is IPlayerCharacter player &&
                Task.Quest != null &&
                InteractionType == EInteractionType.AcceptQuest)
            {
                var jobGearSets = classJobUtils.GetJobGearSets(combatOnly: false);
                List<Job> acceptableJobs = [.. Task.Quest.Info.ClassJobs.Where(x => jobGearSets.Count == 0 || jobGearSets.Any(v => v.ClassJob.Equals(x)))];
                // Runs every frame while waiting for the target.
                string acceptableJobsText = string.Join(',', acceptableJobs);
                if (acceptableJobsText != _loggedAcceptableJobs)
                {
                    _loggedAcceptableJobs = acceptableJobsText;
                    logger.LogInformation("{QuestId} acceptableJobs: {AcceptableJobs}", Task.Quest.Id, acceptableJobsText);
                }
                if (acceptableJobs.Count == 0)
                    throw new Exception(_LF("_JobGearsetError", Task.Quest.Info.ClassJobs[0].ToFriendlyString(), Task.Quest.Info.Name));

                // the game won't offer the quest on a class below its level, so prefer classes that are high enough
                List<Job> levelledJobs = [.. acceptableJobs.Where(x => gameState.GetClassJobLevel(x) >= Task.Quest.Info.Level)];
                if (levelledJobs.Count > 0)
                    acceptableJobs = levelledJobs;

                Job playerJob = (Job)player.ClassJob.Value.RowId;
                if (!acceptableJobs.Contains(playerJob))
                {
                    Job firstJob = acceptableJobs[0];
                    Job candidate = SelectQuestJob(acceptableJobs, configuration, Task.Quest.Info.AlliedSociety,
                        firstJob.IsCrafter(), firstJob.IsGatherer());
                    if (ClassJobUtils.ClassToJobStone(candidate) is (Job job, ushort item))
                    {
                        _unequipItem = item;
                        logger.LogInformation("Current job {ClassJob} is not valid for {QuestId}, changing to {AcceptableJob} via {MiddleJob}",
                            playerJob, Task.Quest.Id, candidate, job);
                        candidate = job;
                    }

                    if (!classJobUtils.SwitchClassJob(candidate) && !_reportedWrongJob)
                        throw new Exception(_LF("_JobGearsetError", candidate.ToFriendlyString(), Task.Quest.Info.Name));
                    logger.LogInformation($"Switched from {playerJob} to {candidate}");

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    return ETaskResult.StillRunning;
                }
            }

            // Accepted class/job quests must be progressed on their original class; switch back before interacting.
            if (!_reportedWrongJob && Task.Quest != null &&
                InteractionType is EInteractionType.CompleteQuest or EInteractionType.Interact &&
                objectTable[0] is IPlayerCharacter completionPlayer &&
                configuration.General.SameJobThroughoutQuest)
            {
                Job requiredJob = classJobUtils.LookupQuestStartJob(Task.Quest.Id);
                if (requiredJob != Job.ADV && (Job)completionPlayer.ClassJob.Value.RowId != requiredJob)
                {
                    logger.LogInformation("Quest {QuestId} should be continued as {RequiredJob}, switching from {CurrentJob}",
                        Task.Quest.Id, requiredJob, (Job)completionPlayer.ClassJob.Value.RowId);
                    if (!classJobUtils.SwitchClassJob(requiredJob))
                    {
                        chatGui.PrintError(_LF(
                            "Quest {0} should be continued as {1}, but you do not have a gearset for that job.",
                            Task.Quest.Info.Name, requiredJob));
                        _reportedWrongJob = true;
                    }

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
            if (_interactionState == EInteractionState.Triggered &&
                flag is ConditionFlag.OccupiedInQuestEvent or ConditionFlag.OccupiedInEvent &&
                value)
            {
                logger.LogInformation("Interaction was most likely triggered");
                _interactionState = EInteractionState.Confirmed;
            }
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            InteractionType = Task.InteractionType;
            _loggedAcceptableJobs = null;

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
                        _interactionState = EInteractionState.Triggered;
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
            Triggered,
            Confirmed
        }
    }
}
