using System.Collections.ObjectModel;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Shared;
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

        bool namazu = alliedSociety == EAlliedSociety.Namazu;
        bool craft = namazu ? configuration.Advanced.NamazuPreferCraft : isCrafter;
        bool gather = namazu ? !craft : isGatherer;
        Job preferred = craft ? configuration.General.CraftingJob
            : gather ? configuration.General.GatheringJob
            : configuration.General.CombatJob;

        if (acceptableJobs.Contains(preferred))
            return preferred;

        if (preferred == Job.ADV && acceptableJobs.FirstOrDefault(x => craft ? x is >= Job.CRP and <= Job.CUL : !gather || x is Job.MIN or Job.BTN or Job.FSH) is var best &&
            best != Job.ADV)
            return best;

        return fallback;
    }

    // The current combat job can usually take a battle society quest too, so the society job has to be forced.
    internal static Job? SocietyJobSwitch(IReadOnlyList<Job> acceptableJobs, Job current, EAlliedSociety society,
        Job societyJob, bool lowest, Func<Job> combatJob)
    {
        if (society != EAlliedSociety.None)
            return societyJob != Job.ADV && current != societyJob && acceptableJobs.Contains(societyJob) ? societyJob : null;

        // With 'Lowest level' the society job keeps changing, so every other quest goes back to the preferred job.
        if (!acceptableJobs.Contains(current) || !lowest && (societyJob == Job.ADV || current != societyJob))
            return null;

        Job back = combatJob();
        return back != Job.ADV && back != current && acceptableJobs.Contains(back) ? back : null;
    }

    // A crafter or gatherer can take most story quests, but the quest's fights then fail (Operation Archon on FSH).
    internal static bool NeedsCombatJob(Job current, IReadOnlyList<Job> questJobs, EAlliedSociety society) =>
        society == EAlliedSociety.None && current is >= Job.CRP and <= Job.FSH &&
        questJobs.Any(x => x is not (>= Job.CRP and <= Job.FSH or Job.ADV));

    internal static Job LowestCombatJob(IEnumerable<Job> acceptableJobs, Func<Job, short> level) =>
        acceptableJobs.Where(x => x is not (>= Job.CRP and <= Job.FSH)).OrderBy(level).FirstOrDefault();

    internal const int MaxUnmountTries = 3;

    // A mount the game won't let you leave still gets the interaction once the tries run out.
    internal static bool ShouldUnmount(bool mounted, bool isGatheringPoint, int tries) =>
        mounted && !isGatheringPoint && tries < MaxUnmountTries;

    internal sealed class Factory(AutomatonIpc automatonIpc, BossModIpc bossModIpc, Configuration configuration, RedoUtil redoUtil,
        GilShopData gilShopData,
        TomestoneShopData tomestoneShopData,
        SealShopData sealShopData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
                or EInteractionType.SinglePlayerDuty)
            {
                if (step.InteractionType == EInteractionType.SinglePlayerDuty &&
                    bossModIpc.CanRunQuestBattle(quest.Id, step.SinglePlayerDutyOptions))
                    yield return new QuestBattle.LeaveParty();

                if (step.InteractionType is EInteractionType.AcceptQuest && sequence.Sequence > 0 && redoUtil.IsActive())
                {
                    // Can't accept other quests during NG+
                    yield break;
                }
                if (step.InteractionType is EInteractionType.CompleteQuest ||
                    (step.InteractionType is EInteractionType.AcceptQuest && quest.GetQuestInfo().CompletesInstantly)) // instant quest
                {
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
                if (step.DataId == null || Purchase.IsHandledByKnightshopper(step, gilShopData, tomestoneShopData, sealShopData))
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
                throw new ArgumentException($"{nameof(step.DataId)} is missing", nameof(step));

            // if we're fast enough, it is possible to get the smalltalk prompt
            if (sequence.Sequence == 0 && sequence.Steps.IndexOf(step) == 0)
                yield return new WaitAtEnd.WaitDelay();

            yield return new Task(
                step.DataId.Value,
                quest,
                step.InteractionType,
                step.TargetTerritoryId != null ||
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
        IQuestGameState gameState,
        TaskToastClassifier toastClassifier)
        : TaskExecutor<Task>, IConditionChangeAware, IToastAware
    {
        private static readonly TimeSpan RefusalWindow = TimeSpan.FromSeconds(2);

        private DateTime _continueAt = DateTime.MinValue;
        private DateTime? _triggeredAt;
        private bool _tooFar;
        private EInteractionState _interactionState = EInteractionState.None;
        private bool _needsFacing;
        private bool _needsUnmount;
        private int _unmountTries;
        private int _landingTries;
        private bool _reportedStillMounted;
        private bool _reportedNoObject;
        private string? _loggedAcceptableJobs;
        private bool _reportedWrongJob;
        private ushort _unequipItem;

        private bool _delayedFinalCheck;

        public Quest? Quest => Task.Quest;
        public EInteractionType InteractionType { get; set; }

        public override ETaskResult Update()
        {
            if (_tooFar)
                return ETaskResult.RetryStep;

            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;

            UnequipJobStone();
            if (GetEarlyResult() is { } earlyResult)
                return earlyResult;

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
                logger.LogDebug("Facing target");
                cameraFunctions.Face(gameObject.Position);
                _continueAt = DateTime.Now.AddSeconds(0.2);
                _needsFacing = false;
                return ETaskResult.StillRunning;
            }

            if (SwitchToAcceptJob() is { } acceptResult)
                return acceptResult;

            if (SwitchToQuestJob() is { } questJobResult)
                return questJobResult;

            bool isTargetable = gameObject.IsTargetable;
            bool hasAnyMarker = HasAnyMarker(gameObject);
            if (!isTargetable || !hasAnyMarker)
            {
                if (EzThrottler.Throttle("skipTarget", miliseconds: 1000))
                    logger.LogDebug("IsTargetable: {IsTargetable} / HasAnyMarker: {HasAnyMarker}", isTargetable, hasAnyMarker);
                return ETaskResult.StillRunning;
            }

            // The game's own dismount on interact can pass for the finished interaction, so get off the mount first.
            bool mounted = condition[ConditionFlag.Mounted];
            bool isGatheringPoint = gameObject.ObjectKind == ObjectKind.GatheringPoint;
            // While flying, dismount only makes the mount land.
            if (mounted && !isGatheringPoint && condition[ConditionFlag.InFlight])
            {
                // The game refuses an interaction in the air, and QST would only notice through the stuck handling.
                if (_landingTries++ >= 20)
                {
                    logger.LogWarning("Still flying near {DataId} after trying to land, travelling to it again", Task.DataId);
                    return ETaskResult.RetryStep;
                }

                gameFunctions.Unmount();
                _continueAt = DateTime.Now.AddSeconds(0.5);
                return ETaskResult.StillRunning;
            }

            if (ShouldUnmount(mounted, isGatheringPoint, _unmountTries))
            {
                if (_unmountTries == 0)
                    logger.LogInformation("Unmounting before interacting with {DataId}", Task.DataId);

                _unmountTries++;
                gameFunctions.Unmount();
                _continueAt = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            if (mounted && !isGatheringPoint && !_reportedStillMounted)
            {
                logger.LogWarning("Still mounted after {Tries} tries, interacting with {DataId} anyway",
                    _unmountTries, Task.DataId);
                _reportedStillMounted = true;
            }

            // In the air the game rejects the interaction but still reports success.
            if (condition[ConditionFlag.Jumping] || condition[ConditionFlag.Jumping61] || IsFalling())
            {
                _continueAt = DateTime.Now.AddSeconds(0.25);
                return ETaskResult.StillRunning;
            }

            TriggerInteraction(gameObject);
            return ETaskResult.StillRunning;
        }

        // The fall after a mid-air dismount sets neither jumping condition.
        private static unsafe bool IsFalling()
        {
            BattleChara* player = Control.GetLocalPlayer();
            return player != null && player->IsJumping();
        }

        private void UnequipJobStone()
        {
            if (_unequipItem != 0)
            {
                logger.LogDebug("unequipping item {ItemId}", _unequipItem);
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    var armoryType = InventoryType.ArmorySoulCrystal;
                    ushort srcSlot = 13;
                    InventoryContainer* armoryContainer = inventoryManager->GetInventoryContainer(armoryType);
                    if (EquipmentSlots.TryFindFirstEmptySlot(armoryContainer, out ushort targetSlot))
                    {
                        logger.LogDebug("Moving {ItemId} to {TargetSlot} in soul crystal inv", _unequipItem, targetSlot);
                        _ = inventoryManager->MoveItemSlot(InventoryType.EquippedItems, srcSlot, armoryType, targetSlot, a6: true);
                    }
                    else
                    {
                        logger.LogWarning("Armory container {ArmoryType} is full, cannot unequip item {ItemId}",
                            armoryType, _unequipItem);
                        throw new InvalidOperationException("Unable to unequip gear - armory chest is full.");
                    }
                }
                _unequipItem = 0;
            }
        }

        private ETaskResult? GetEarlyResult()
        {
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
                    {
                        logger.LogInformation("Interacted with {DataId}: {How}", Task.DataId,
                            _interactionState == EInteractionState.Confirmed ? "the game started the event" : "no event seen, assuming it worked");
                        return ETaskResult.TaskComplete;
                    }

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    _delayedFinalCheck = true;
                    return ETaskResult.StillRunning;
                }
            }

            return null;
        }

        private ETaskResult? SwitchToAcceptJob()
        {
            if (!_reportedWrongJob &&
                objectTable[0] is IPlayerCharacter player &&
                Task.Quest != null &&
                InteractionType == EInteractionType.AcceptQuest)
            {
                var jobGearSets = classJobUtils.GetJobGearSets(combatOnly: false);
                Job currentJob = (Job)player.ClassJob.Value.RowId;
                // The current job needs no gear set to switch to (152 and 639 on FSH without one).
                List<Job> acceptableJobs = [.. Task.Quest.Info.ClassJobs
                    .Where(x => jobGearSets.Count == 0 || x == currentJob || jobGearSets.Any(v => v.ClassJob.Equals(x)))
                    .OrderByDescending(x => jobGearSets.Where(v => v.ClassJob.Equals(x)).Select(v => (int)v.Level).DefaultIfEmpty().Max())
                    .ThenByDescending(x => jobGearSets.Where(v => v.ClassJob.Equals(x)).Select(v => (int)v.ItemLevel).DefaultIfEmpty().Max())];
                string acceptableJobsText = string.Join(',', acceptableJobs);
                if (acceptableJobsText != _loggedAcceptableJobs)
                {
                    _loggedAcceptableJobs = acceptableJobsText;
                    logger.LogInformation("{QuestId} acceptableJobs: {AcceptableJobs}", Task.Quest.Id, acceptableJobsText);
                }
                if (acceptableJobs.Count == 0)
                    throw new InvalidOperationException(_LF("_JobGearsetError", Task.Quest.Info.ClassJobs[0].ToFriendlyString(), Task.Quest.Info.Name));

                // The game won't offer the quest on a class below its level.
                List<Job> levelledJobs = [.. acceptableJobs.Where(x => gameState.GetClassJobLevel(x) >= Task.Quest.Info.Level)];
                if (levelledJobs.Count > 0)
                    acceptableJobs = levelledJobs;

                Job playerJob = (Job)player.ClassJob.Value.RowId;
                bool lowest = configuration.General.SocietyLowestJob;
                Job? societySwitch = SocietyJobSwitch(acceptableJobs, playerJob, Task.Quest.Info.AlliedSociety,
                    lowest ? LowestCombatJob(acceptableJobs, gameState.GetClassJobLevel) : configuration.General.SocietyCombatJob,
                    lowest, () => classJobUtils.LookupConfiguredJob(EExtendedClassJob.ConfiguredCombatJob));
                bool needsCombat = jobGearSets.Count > 0 &&
                    NeedsCombatJob(playerJob, acceptableJobs, Task.Quest.Info.AlliedSociety);
                if (!acceptableJobs.Contains(playerJob) || societySwitch != null || needsCombat)
                {
                    if (needsCombat)
                        acceptableJobs = [.. acceptableJobs.Where(x => x is not (>= Job.CRP and <= Job.FSH))];
                    Job firstJob = acceptableJobs[0];
                    Job candidate = societySwitch ?? SelectQuestJob(acceptableJobs, configuration,
                        Task.Quest.Info.AlliedSociety, firstJob.IsCrafter(), firstJob.IsGatherer());
                    if (ClassJobUtils.ClassToJobStone(candidate) is (Job job, ushort item))
                    {
                        _unequipItem = item;
                        logger.LogInformation("Current job {ClassJob} is not valid for {QuestId}, changing to {AcceptableJob} via {MiddleJob}",
                            playerJob, Task.Quest.Id, candidate, job);
                        candidate = job;
                    }

                    if (!classJobUtils.SwitchJob(candidate) && !_reportedWrongJob)
                        throw new InvalidOperationException(_LF("_JobGearsetError", candidate.ToFriendlyString(), Task.Quest.Info.Name));
                    logger.LogInformation("Switched from {OldJob} to {NewJob}", playerJob, candidate);

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    return ETaskResult.StillRunning;
                }
            }

            return null;
        }

        private ETaskResult? SwitchToQuestJob()
        {
            // Accepted class/job quests must be progressed on their original class; switch back before interacting.
            if (!_reportedWrongJob && Task.Quest != null &&
                InteractionType is EInteractionType.CompleteQuest or EInteractionType.Interact &&
                objectTable[0] is IPlayerCharacter completionPlayer &&
                configuration.General.SameJobThroughoutQuest)
            {
                Job requiredJob = classJobUtils.LookupQuestStartJob(Task.Quest.Id);
                if (requiredJob != Job.ADV && (Job)completionPlayer.ClassJob.Value.RowId != requiredJob &&
                    !NeedsCombatJob(requiredJob, Task.Quest.Info.ClassJobs, Task.Quest.Info.AlliedSociety))
                {
                    logger.LogInformation("Quest {QuestId} should be continued as {RequiredJob}, switching from {CurrentJob}",
                        Task.Quest.Id, requiredJob, (Job)completionPlayer.ClassJob.Value.RowId);
                    if (!classJobUtils.SwitchJob(requiredJob))
                    {
                        chatGui.Print(_LF(
                            "Quest {0} should be continued as {1}, but you don't have a gear set for that job.",
                            Task.Quest.Info.Name, requiredJob), CommandHandler.MessageTag, CommandHandler.TagColor);
                        _reportedWrongJob = true;
                    }

                    _continueAt = DateTime.Now.AddSeconds(0.2);
                    return ETaskResult.StillRunning;
                }
            }

            return null;
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

        // The game answers an out-of-range interact with "Too far away." while the call still reports success.
        public bool OnErrorToast(SeString message)
        {
            if (!IsRefusal(_interactionState == EInteractionState.Triggered, _triggeredAt, DateTime.Now) ||
                !toastClassifier.IsTooFarAway(message.TextValue))
                return false;

            logger.LogInformation("Too far away to interact with {DataId}, travelling to it again", Task.DataId);
            ProgressContext = null;
            _interactionState = EInteractionState.None;
            _tooFar = true;
            return false;
        }

        internal static bool IsRefusal(bool triggered, DateTime? triggeredAt, DateTime now) =>
            triggered && triggeredAt is { } at && now - at <= RefusalWindow;

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            InteractionType = Task.InteractionType;
            _loggedAcceptableJobs = null;
            _unmountTries = 0;
            _landingTries = 0;
            _reportedStillMounted = false;

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
                    {
                        _interactionState = EInteractionState.Triggered;
                        _triggeredAt = DateTime.Now;
                    }
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
