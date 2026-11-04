using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestController : MiniTaskController<QuestController>
{
    public delegate void AutomationTypeChangedEventHandler(object sender, EAutomationType e);

    public enum EAutomationType
    {
        Manual,
        Automatic,
        GatheringOnly,
        SingleQuestA,
        SingleQuestB
    }

    public enum ECurrentQuestType
    {
        Normal,
        Next,
        Gathering,
        Simulated
    }


    /// <summary>Sentinel value for <c>CurrentQuest.Step</c> meaning "all steps of the current sequence are completed".</summary>
    private const int CompletedStepValue = 255;

    /// <summary>Extra grace period (seconds) added on top of the game's animation lock before we consider the player idle.</summary>
    private const float AnimationLockGraceSeconds = 1f;

    private const uint HighQualityItemIdOffset = 1_000_000;

    private const uint CollectableItemIdOffset = 500_000;

    /// <summary>How long to wait at sequence 0/step 0 before assuming the quest-accept addon never fired and resetting.</summary>
    private const int QuestAcceptStalledTimeoutSeconds = 15;

    /// <summary>Consecutive deaths on the same quest step before death handling gives up and stops.</summary>
    private const int MaxConsecutiveDeaths = 5;

    /// <summary>Seconds to wait after returning from a death before retrying the current step.</summary>
    private const int DeathRecoveryGraceSeconds = 3;

    private HashSet<ElementId>? _questsAwaitingAcceptance;
    private QuestProgress? _nextQuestAwaitingAcceptance;

    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuestFunctions;
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly CombatController _combatController;
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly GameFunctions _gameFunctions;
    private readonly IGameGuiAdapter _gameGui;
    private readonly GatheringController _gatheringController;
    private readonly AutoDutyIpc _autoDutyIpc;
    private readonly BossModIpc _bossModIpc;
    private readonly SinglePlayerDuty.RetryCounter _soloDutyRetries;
    private readonly Stopwatch _autoDutyStatusPoll = new();
    private readonly Stopwatch _combatStatusPoll = new();
    private readonly IArtisanIpc _artisanIpc;
    private readonly CraftingOperationState _craftingOperation;
    private readonly Stopwatch _craftingStatusPoll = new();
    private readonly IKnightshopperIpc _knightshopperIpc;
    private readonly PurchaseOperationState _purchaseOperation;
    private readonly Stopwatch _purchaseStatusPoll = new();
    private readonly IGatherChillIpc _gatherChillIpc;
    private readonly GatheringOperationState _gatheringOperation;
    private readonly Stopwatch _gatheringStatusPoll = new();
    private readonly IFeeshIpc _feeshIpc;
    private readonly FishingOperationState _fishingOperation;
    private readonly Stopwatch _fishingStatusPoll = new();
    private readonly HighlightObject _highlightObject;
    private readonly IKeyState _keyState;
    private readonly ILogger<QuestController> _logger;
    private readonly MovementController _movementController;
    private readonly IObjectTable _objectTable;

    private readonly Lock _progressLock = new();
    private readonly QuestData _questData;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestPriorityManager _priorityManager;
    private readonly QuestProgressTracker _tracker;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent;
    private readonly StopConditionComponent _stopConditionComponent;
    private readonly TaskCreator _taskCreator;
    private readonly IToastGui _toastGui;
    private readonly ICommandManager _commandManager;
    private readonly NotificationMasterIpc _notificationMasterIpc;
    private EAutomationType _automationType;

    /// <summary>True while recovering from the player's death (waiting for the return prompt / respawn).</summary>
    private bool _handlingDeath;

    /// <summary>When the player regained consciousness after a death; used to let the zone settle.</summary>
    private DateTime _respawnedAt = DateTime.MinValue;

    /// <summary>Throttle for re-confirming the return-to-respawn prompt while the player is dead.</summary>
    private DateTime _lastReturnConfirmAt = DateTime.MinValue;

    /// <summary>Quest step a death streak is counted against; the streak resets when the step changes.</summary>
    private (ElementId QuestId, byte Sequence, int Step)? _deathStreakKey;
    private int _deathStreakCount;

    private DateTime _lastTaskUpdate = DateTime.Now;

    /// <summary>
    ///     Some combat encounters finish relatively early (i.e. they're done as part of progressing the quest, but not
    ///     technically necessary to progress the quest if we'd just run away and back). We add some slight delay, as
    ///     talking to NPCs, teleporting etc. won't successfully execute.
    /// </summary>
    private DateTime _safeAnimationEnd = DateTime.MinValue;


    public QuestController(
        IClientState clientState,
        IObjectTable objectTable,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        MovementController movementController,
        CombatController combatController,
        GatheringController gatheringController,
        AutoDutyIpc autoDutyIpc,
        BossModIpc bossModIpc,
        SinglePlayerDuty.RetryCounter soloDutyRetries,
        IArtisanIpc artisanIpc,
        CraftingOperationState craftingOperation,
        IKnightshopperIpc knightshopperIpc,
        PurchaseOperationState purchaseOperation,
        IGatherChillIpc gatherChillIpc,
        GatheringOperationState gatheringOperation,
        IFeeshIpc feeshIpc,
        FishingOperationState fishingOperation,
        ILogger<QuestController> logger,
        HighlightObject highlightObject,
        QuestRegistry questRegistry,
        QuestPriorityManager priorityManager,
        QuestProgressTracker tracker,
        QuestData questData,
        IKeyState keyState,
        IChatGui chatGui,
        ICondition condition,
        IToastGui toastGui,
        Configuration configuration,
        TaskCreator taskCreator,
        IServiceProvider serviceProvider,
        InterruptHandler interruptHandler,
        IDataManager dataManager,
        IGameGuiAdapter gameGui,
        ICommandManager commandManager,
        SinglePlayerDutyConfigComponent singlePlayerDutyConfigComponent,
        StopConditionComponent stopConditionComponent,
        AlliedSocietyQuestFunctions alliedSocietyQuestFunctions)
        : base(chatGui, condition, serviceProvider, interruptHandler, dataManager, logger)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _gameFunctions = gameFunctions;
        _gameGui = gameGui;
        _questFunctions = questFunctions;
        _movementController = movementController;
        _combatController = combatController;
        _gatheringController = gatheringController;
        _autoDutyIpc = autoDutyIpc;
        _bossModIpc = bossModIpc;
        _soloDutyRetries = soloDutyRetries;
        _artisanIpc = artisanIpc;
        _craftingOperation = craftingOperation;
        _knightshopperIpc = knightshopperIpc;
        _purchaseOperation = purchaseOperation;
        _gatherChillIpc = gatherChillIpc;
        _gatheringOperation = gatheringOperation;
        _feeshIpc = feeshIpc;
        _fishingOperation = fishingOperation;
        _questRegistry = questRegistry;
        _priorityManager = priorityManager;
        _tracker = tracker;
        _questData = questData;
        _keyState = keyState;
        _chatGui = chatGui;
        _condition = condition;
        _toastGui = toastGui;
        _configuration = configuration;
        _taskCreator = taskCreator;
        _singlePlayerDutyConfigComponent = singlePlayerDutyConfigComponent;
        _stopConditionComponent = stopConditionComponent;
        _alliedSocietyQuestFunctions = alliedSocietyQuestFunctions;
        _logger = logger;
        _highlightObject = highlightObject;
        _commandManager = commandManager;
        _notificationMasterIpc = serviceProvider.GetRequiredService<NotificationMasterIpc>();

        _condition.ConditionChange += OnConditionChange;
        _toastGui.ErrorToast += OnErrorToast;
    }

    public EAutomationType AutomationType
    {
        get => _automationType;
        set
        {
            if (value == _automationType)
                return;

            _logger.LogInformation("Setting automation type to {NewAutomationType} (previous: {OldAutomationType})",
                value, _automationType);
            _automationType = value;
            AutomationTypeChanged?.Invoke(this, value);
        }
    }

    public (QuestProgress Progress, ECurrentQuestType Type)? CurrentQuestDetails => _tracker.CurrentQuestDetails;

    public QuestProgress? CurrentQuest => _tracker.CurrentQuest;

    public QuestProgress? StartedQuest
    {
        get => _tracker.StartedQuest;
        private set => _tracker.StartedQuest = value;
    }

    public QuestProgress? SimulatedQuest
    {
        get => _tracker.SimulatedQuest;
        private set => _tracker.SimulatedQuest = value;
    }

    public QuestProgress? NextQuest
    {
        get => _tracker.NextQuest;
        private set
        {
            _tracker.NextQuest = value;
            if (value == null)
                _nextQuestAwaitingAcceptance = null;
        }
    }

    public QuestProgress? GatheringQuest
    {
        get => _tracker.GatheringQuest;
        private set => _tracker.GatheringQuest = value;
    }

    /// <summary>
    ///     Used when accepting leves, as there's a small delay
    /// </summary>
    public QuestProgress? PendingQuest
    {
        get => _tracker.PendingQuest;
        private set => _tracker.PendingQuest = value;
    }

    public QuestPriorityManager PriorityManager => _priorityManager;

    public bool StopAfterCurrentQuest { get; set; }
    public bool StopAfterAcceptingNextQuest { get; set; }
    public bool StopBeforeTeleport { get; set; }

    public string? DebugState { get; private set; }

    public bool IsQuestWindowOpen => IsQuestWindowOpenFunction?.Invoke() ?? true;
    public Func<bool>? IsQuestWindowOpenFunction { private get; set; } = () => true;

    public bool IsRunning => !_taskQueue.AllTasksComplete;
    public bool HasExternalOperation => _craftingOperation.HasOperation || _purchaseOperation.HasOperation ||
        _gatheringOperation.HasOperation || _fishingOperation.HasOperation || _autoDutyIpc.HasOperation ||
        _combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup;
    private string? ExternalOperationBlockReason => _craftingOperation.BlockReason ?? _purchaseOperation.BlockReason ??
        _gatheringOperation.BlockReason ?? _fishingOperation.BlockReason ?? _autoDutyIpc.BlockReason ??
        _combatController.BlockReason ?? (_bossModIpc.HasPendingCleanup ? _bossModIpc.CleanupReason : null);
    public TaskQueue TaskQueue => _taskQueue;

    public string? CurrentTaskState
    {
        get
        {
            if (_taskQueue.CurrentTaskExecutor is IDebugStateProvider debugStateProvider)
                return debugStateProvider.GetDebugState();

            return null;
        }
    }

    public bool IsQuestingStopped
    {
        get
        {
            return AutomationType == EAutomationType.Manual && !IsRunning && !IsQuestWindowOpen;
        }
    }

    public event AutomationTypeChangedEventHandler? AutomationTypeChanged;

    public void Reload()
    {
        lock (_progressLock)
        {
            if (HasExternalOperation)
                Stop("Reload during external operation");

            _logger.LogInformation("Reload, resetting curent quest progress");

            ResetInternalState();

            _questRegistry.Reload();
            _singlePlayerDutyConfigComponent.Reload();
            _alliedSocietyQuestFunctions.Reload();
        }
    }

    private void ResetInternalState()
    {
        _nextQuestAwaitingAcceptance = null;
        _tracker.Reset();
        _safeAnimationEnd = DateTime.MinValue;

        DebugState = null;
    }

    public void Update()
    {
        // Success can replace the outcome task before its next update; observe it independently.
        _soloDutyRetries.Observe(_clientState.IsLoggedIn ? ProfileController.CurrentContentId : null,
            static questId => QuestFunctions.GetQuestProgressInfo(questId)?.Sequence);

        // Reconcile retained work even while stopped, occupied or logged out.
        bool craftingBlocked = UpdateCraftingOperation();
        bool purchaseBlocked = UpdateOwnedOperation(_purchaseOperation, _taskQueue.CurrentTaskExecutor is Purchase.DoBuy,
            _purchaseStatusPoll, _knightshopperIpc.PollOperation, _knightshopperIpc.CancelOwned);
        bool gatheringBlocked = UpdateOwnedOperation(_gatheringOperation, _taskQueue.CurrentTaskExecutor is Gather.StartGathering,
            _gatheringStatusPoll, _gatherChillIpc.PollOperation, _gatherChillIpc.CancelOwned);
        bool fishingBlocked = UpdateOwnedOperation(_fishingOperation, _taskQueue.CurrentTaskExecutor is Fish.DoFish,
            _fishingStatusPoll, _feeshIpc.PollOperation, _feeshIpc.CancelOwned);
        bool dutyBlocked = UpdateOwnedOperation(_autoDutyIpc.Operation, _taskQueue.CurrentTaskExecutor is Duty.StartAutoDutyExecutor,
            _autoDutyStatusPoll, _autoDutyIpc.PollOperation, _autoDutyIpc.CancelOwned);
        bool combatBlocked = UpdateCombatRecovery();
        if (craftingBlocked || purchaseBlocked || gatheringBlocked || fishingBlocked || dutyBlocked || combatBlocked)
            return;

        unsafe
        {
            ActionManager* actionManager = ActionManager.Instance();
            if (actionManager != null)
            {
                float animationLock = Math.Max(actionManager->AnimationLock,
                    actionManager->CastTimeElapsed > 0
                        ? actionManager->CastTimeTotal - actionManager->CastTimeElapsed
                        : 0);
                if (animationLock > 0)
                    _safeAnimationEnd = DateTime.Now.AddSeconds(AnimationLockGraceSeconds + animationLock);
            }
        }

        if (IsQuestingStopped)
            return;

        UpdateCurrentQuest();

        if (!_clientState.IsLoggedIn)
        {
            StopAllDueToConditionFailed("Logged out");
            return;
        }
        if (_condition[ConditionFlag.Unconscious])
        {
            if (_autoDutyIpc.HasOperation && _taskQueue.CurrentTaskExecutor is Duty.StartAutoDutyExecutor)
            {
                // ignoring death in a dungeon if it is being run by AD
            }
            else if (_bossModIpc.HasOwnedSoloDuty)
            {
                // ignoring death in a solo duty so it can be retried
            }
            else if (!_taskQueue.AllTasksComplete || _handlingDeath)
            {
                BeginDeathHandling();
                return;
            }
        }
        else if (_handlingDeath)
        {
            FinishDeathHandling();
            return;
        }
        else if (_configuration.General.UseEscToCancelQuesting && _keyState[VirtualKey.ESCAPE])
        {
            if (!_taskQueue.AllTasksComplete)
                StopAllDueToConditionFailed("ESC pressed");
        }

        // check level stop condition
        // stops immediately instead of quest stop after completion of quest
        if (_configuration.Stop.Enabled && _configuration.Stop.LevelToStopAfter)
        {
            unsafe
            {
                short currentLevel = PlayerState.Instance()->CurrentLevel;
                if (currentLevel >= _configuration.Stop.TargetLevel && IsRunning)
                {
                    _logger.LogInformation("Reached level stop condition (level: {CurrentLevel}, target: {TargetLevel})", currentLevel, _configuration.Stop.TargetLevel);
                    var msg = _LF("Reached or exceeded target level {0}.", _configuration.Stop.TargetLevel);
                    _chatGui.Print(msg, CommandHandler.MessageTag, CommandHandler.TagColor);
                    if (_configuration.Notifications.NotifyOnStopCondition)
                        _notificationMasterIpc.Notify(msg);
                    Stop($"Level stop condition reached [{currentLevel}]");
                    return;
                }
            }
        }

        if (!HasExternalOperation && AutomationType == EAutomationType.Automatic &&
            (_taskQueue.AllTasksComplete || _taskQueue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.WaitQuestAccepted)
            && CurrentQuest is { Sequence: 0, Step: 0 } or { Sequence: 0, Step: CompletedStepValue }
            && DateTime.Now >= CurrentQuest.StepProgress.StartedAt.AddSeconds(QuestAcceptStalledTimeoutSeconds))
        {
            lock (_progressLock)
            {
                _logger.LogWarning("Quest accept apparently didn't work out, resetting progress");
                CurrentQuest.SetStep(0);
            }

            ExecuteNextStep();
            return;
        }

        try
        {
            UpdateCurrentTask();
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle(ex.Message))
                throw;
        }
    }

    private void UpdateCurrentQuest()
    {
        lock (_progressLock)
        {
            // Let the owning executor verify its objective before quest or sequence changes discard it.
            if (HasExternalOperation)
                return;

            DebugState = null;

            if (!_clientState.IsLoggedIn)
            {
                ResetInternalState();
                DebugState = "Not logged in";
                return;
            }

            // Observe expected acceptances before quest selection or a sequence change replaces the queue.
            bool wasRunning = AutomationType != EAutomationType.Manual;
            ObserveQuestAcceptances(_questFunctions.IsQuestAccepted, TryStopOnQuestAccepted);
            if (wasRunning && AutomationType == EAutomationType.Manual)
                return;

            if (PendingQuest != null)
            {
                if (!_questFunctions.IsQuestAccepted(PendingQuest.Quest.Id))
                {
                    DebugState = $"Waiting for Leve {PendingQuest.Quest.Id}";
                    return;
                }

                StartedQuest = PendingQuest;
                PendingQuest = null;
                TryStopOnQuestAccepted(StartedQuest.Quest.Id);
                if (AutomationType == EAutomationType.Manual)
                    return;
                CheckNextTasks("Pending quest accepted");
            }

            if (SimulatedQuest == null && NextQuest != null)
            {
                bool accepted = _questFunctions.IsQuestAccepted(NextQuest.Quest.Id);
                bool completed = !NextQuest.Quest.Info.IsRepeatable && _questFunctions.IsQuestComplete(NextQuest.Quest.Id);
                if (ResolveNextQuest(accepted, completed, TryStopOnQuestAccepted) && AutomationType == EAutomationType.Manual)
                    return;
            }

            // Queue currently available accept-only priority quests before completing any.
            if (SimulatedQuest == null && NextQuest == null && GatheringQuest == null &&
                (AutomationType == EAutomationType.Automatic || StartedQuest == null))
            {
                TryQueueNextAcceptOnlyQuest();
            }

            // Honor the stop-after toggle before starting queued or priority quests; completed repeatables may still be active.
            if (StartedQuest != null && _questFunctions.IsQuestComplete(StartedQuest.Quest.Id) && !_questFunctions.IsQuestAccepted(StartedQuest.Quest.Id))
            {
                if (_configuration.Stop.Enabled &&
                    _configuration.Stop.QuestsToStopAfter.Contains(StartedQuest.Quest.Id))
                {
                    ElementId questId = StartedQuest.Quest.Id;
                    _logger.LogInformation("Reached stopping point (quest: {QuestId})", questId);
                    var msg = _LF("Completed quest '{0}', which is configured as a stopping point.", StartedQuest.Quest.Info.Name);
                    _chatGui.Print(msg, CommandHandler.MessageTag, CommandHandler.TagColor);
                    _notificationMasterIpc.Notify(msg);
                    StartedQuest = null;
                    Stop($"Stopping point [{questId}] reached");
                    if (_configuration.Stop.RemoveWhenCompleteConditionMet)
                        _configuration.Stop.QuestsToStopAfter.Remove(questId);
                    return;
                }

                if (StopAfterCurrentQuest)
                {
                    ElementId questId = StartedQuest.Quest.Id;
                    _logger.LogInformation("Stopping after current quest as requested (quest: {QuestId})", questId);
                    var msg = _LF("Completed quest '{0}', stopping as requested.", StartedQuest.Quest.Info.Name);
                    _chatGui.Print(msg, CommandHandler.MessageTag, CommandHandler.TagColor);
                    if (_configuration.Notifications.NotifyOnStopCondition)
                        _notificationMasterIpc.Notify(msg);
                    StartedQuest = null;
                    Stop($"Stop after quest [{questId}]");
                    return;
                }
            }

            QuestProgress? questToRun;
            byte currentSequence;
            if (SimulatedQuest != null)
            {
                currentSequence = SimulatedQuest.Sequence;
                questToRun = SimulatedQuest;
            }
            else if (NextQuest != null && _questFunctions.IsReadyToAcceptQuest(NextQuest.Quest.Id))
            {
                questToRun = NextQuest;
                currentSequence = NextQuest.Sequence; // by definition, this should always be 0
                if (NextQuest.Step == 0 &&
                    _taskQueue.AllTasksComplete &&
                    AutomationType == EAutomationType.Automatic)
                    ExecuteNextStep();
            }
            else if (GatheringQuest != null)
            {
                questToRun = GatheringQuest;
                currentSequence = GatheringQuest.Sequence;
                if (GatheringQuest.Step == 0 &&
                    _taskQueue.AllTasksComplete &&
                    AutomationType == EAutomationType.Automatic)
                    ExecuteNextStep();
            }
            else
            {
                (ElementId? currentQuestId, currentSequence, MainScenarioQuestState msqState) =
                    _questFunctions.GetCurrentQuest(allowNewMsq: AutomationType != EAutomationType.SingleQuestB);
                (ElementId, byte)? priorityQuestOption =
                    _priorityManager.Quests
                        .Where(x => !_priorityManager.IsAcceptOnly(x.Id))
                        .Where(x => _questFunctions.IsReadyToAcceptQuest(x.Id, ignoreLevel: true) ||
                                    _questFunctions.IsQuestAccepted(x.Id))
                        .Select(x => (x.Id, QuestFunctions.GetQuestProgressInfo(x.Id)?.Sequence ?? 0))
                        .FirstOrDefault();
                if (priorityQuestOption is { Item1: not null } priorityQuest)
                {
                    currentQuestId = priorityQuest.Item1;
                    currentSequence = priorityQuest.Item2;
                }

                if (currentQuestId == null || currentQuestId.Value == 0)
                {
                    if (StartedQuest != null)
                    {
                        if (msqState == MainScenarioQuestState.Unavailable)
                        {
                            return;
                        }

                        if (msqState == MainScenarioQuestState.LoadingScreen)
                        {
                            return;
                        }

                        _logger.LogInformation("No current quest, resetting data [CQI: {CurrrentQuestData}], [CQ: {QuestData}], [MSQ: {MsqData}]", _questFunctions.GetCurrentQuestInternal(allowNewMsq: true), _questFunctions.GetCurrentQuest(), _questFunctions.GetMainScenarioQuest());
                        StartedQuest = null;
                        Stop("Resetting current quest");
                    }

                    questToRun = null;
                }
                else if (StartedQuest == null || StartedQuest.Quest.Id != currentQuestId)
                {
                    if (_questRegistry.TryGetQuest(currentQuestId, out Quest? quest))
                    {
                        SelectStartedQuest(quest, currentSequence);
                        if (_configuration.Advanced.Debug && _configuration.Advanced.OpenEditor &&
                            (quest.Root.LastChecked.Date == null || (quest.Root.LastChecked.Since(DateTime.Now) is { } since && since.TotalDays > 30)))
                        {
                            (bool success, string msg) = QuestRegistry.OpenEditor(StartedQuest.Quest.Info);
                            _logger.LogDebug("OpenEditor {Success}: {Msg}", success, msg);
                        }

                        if (AutomationType == EAutomationType.Manual)
                            return;

                        unsafe
                        {
                            if (PlayerState.Instance()->CurrentLevel < quest.Info.Level)
                            {
                                _logger.LogInformation(
                                    "Stopping automation, player level ({PlayerLevel}) < quest level ({QuestLevel}",
                                    PlayerState.Instance()->CurrentLevel, quest.Info.Level);
                                Stop("Quest level too high");
                            }
                            else
                            {
                                if (AutomationType == EAutomationType.SingleQuestB)
                                {
                                    _logger.LogInformation("Single quest is finished");
                                    AutomationType = EAutomationType.Manual;
                                }

                                CheckNextTasks("Different Quest");
                            }
                        }
                    }
                    else if (StartedQuest != null)
                    {
                        _logger.LogInformation("No active quest anymore? Not sure what happened...");
                        StartedQuest = null;
                        Stop("No active Quest");
                    }

                    return;
                }
                else
                    questToRun = StartedQuest;
            }

            if (questToRun == null)
            {
                DebugState = "No quest active";
                Stop("No quest active");
                return;
            }

            if (_gameFunctions.IsOccupied() && !_gameFunctions.IsOccupiedWithCustomDeliveryNpc(questToRun.Quest))
            {
                DebugState = "Occupied";
                return;
            }

            if (_movementController.IsPathfinding)
            {
                DebugState = "Pathfinding is running";
                return;
            }

            if (_movementController.IsPathRunning)
            {
                DebugState = "Path is running";
                return;
            }

            if (DateTime.Now < _safeAnimationEnd)
            {
                DebugState = "Waiting for Animation";
                return;
            }

            if (questToRun.Sequence != currentSequence)
            {
                _highlightObject.SetHighlight([]);
                questToRun.SetSequence(currentSequence);
                CheckNextTasks(
                    $"New sequence {questToRun == StartedQuest}/{_questFunctions.GetCurrentQuestInternal(allowNewMsq: true)}");
            }

            Quest q = questToRun.Quest;
            QuestSequence? sequence = q.FindSequence(questToRun.Sequence);
            if (sequence == null)
            {
                DebugState = $"Sequence {questToRun.Sequence} not found";
                Stop("Unknown sequence");
                return;
            }

            if (questToRun.Step == CompletedStepValue)
            {
                DebugState = "Step completed";
                if (!_taskQueue.AllTasksComplete)
                    CheckNextTasks("Step complete");
                return;
            }

            if (sequence.Steps.Count > 0 && questToRun.Step >= sequence.Steps.Count)
            {
                DebugState = "Step not found";
                Stop("Unknown step");
                return;
            }

            DebugState = null;
        }
    }

    [MemberNotNull(nameof(StartedQuest))]
    internal void SelectStartedQuest(Quest quest, byte sequence)
    {
        _highlightObject.SetHighlight([]);
        _logger.LogInformation("New quest: {QuestName}", quest.Info.Name);
        StartedQuest = new(quest, sequence);
    }

    internal void EnqueueQuestTask(ITask task, Func<ElementId, bool> isAccepted)
    {
        if (task is WaitAtEnd.WaitQuestAccepted wait && !isAccepted(wait.ElementId))
            (_questsAwaitingAcceptance ??= []).Add(wait.ElementId);
        _taskQueue.Enqueue(task);
    }

    internal void ObserveQuestAcceptances(Func<ElementId, bool> isAccepted, Action<ElementId> onAccepted)
    {
        if (_questsAwaitingAcceptance == null)
            return;

        foreach (ElementId questId in _questsAwaitingAcceptance.ToArray())
        {
            bool stillQueued = _taskQueue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.WaitQuestAccepted current &&
                               current.ElementId == questId ||
                               _taskQueue.RemainingTasks.OfType<WaitAtEnd.WaitQuestAccepted>().Any(x => x.ElementId == questId);
            if (!stillQueued)
            {
                _questsAwaitingAcceptance.Remove(questId);
                continue;
            }
            if (!isAccepted(questId))
                continue;
            _questsAwaitingAcceptance.Remove(questId);
            if (_nextQuestAwaitingAcceptance?.Quest.Id == questId)
                _nextQuestAwaitingAcceptance = null;
            onAccepted(questId);
            if (AutomationType == EAutomationType.Manual)
                return;
        }
    }

    public (QuestSequence? Sequence, QuestStep? Step, bool createTasks) GetNextStep() => _tracker.GetNextStep();

    public void IncreaseStepCount(ElementId? questId, int? sequence, bool shouldContinue = false)
    {
        lock (_progressLock)
        {
            if (HasExternalOperation)
            {
                Stop("Step change during external operation");
                return;
            }

            (QuestSequence? seq, QuestStep? step, bool _) = GetNextStep();
            if (CurrentQuest == null || seq == null || step == null)
            {
                _logger.LogWarning("Unable to retrieve next quest step, not increasing step count");
                return;
            }

            if (questId != null && CurrentQuest.Quest.Id != questId)
            {
                _logger.LogWarning(
                    "Ignoring 'increase step count' for different quest (expected {ExpectedQuestId}, but we are at {CurrentQuestId}",
                    questId, CurrentQuest.Quest.Id);
                return;
            }

            if (sequence != null && seq.Sequence != sequence.Value)
            {
                _logger.LogWarning(
                    "Ignoring 'increase step count' for different sequence (expected {ExpectedSequence}, but we are at {CurrentSequence}",
                    sequence, seq.Sequence);
                return;
            }

            _logger.LogInformation("Increasing step count from {CurrentValue}", CurrentQuest.Step);
            if (CurrentQuest.Step + 1 < seq.Steps.Count)
                CurrentQuest.SetStep(CurrentQuest.Step + 1);
            else
                CurrentQuest.SetStep(CompletedStepValue);
        }

        using IDisposable? scope = _logger.BeginScope("IncStepCt");
        if (shouldContinue && AutomationType != EAutomationType.Manual)
            ExecuteNextStep();
    }

    private bool ClearTasksInternal()
    {
        _questsAwaitingAcceptance?.Clear();
        // Provider work is cleaned up even when its executor has already detached.
        _craftingOperation.AbandonCurrent();
        bool success = _taskQueue.Reset();
        success &= TryCleanup(_knightshopperIpc.CancelOwned, "Knightshopper purchase");
        success &= TryCleanup(_feeshIpc.CancelOwned, "Feesh fishing");
        if (_movementController.Destination != null || _purchaseOperation.HasOperation ||
            _gatheringOperation.HasOperation || _fishingOperation.HasOperation || _autoDutyIpc.HasOperation)
            success &= TryCleanup(_movementController.Stop, "movement");
        success &= TryCleanup(() => _combatController.Stop("ClearTasksInternal"), "combat");
        success &= TryCleanup(() => _gatheringController.Stop("ClearTasksInternal"), "gathering");
        success &= TryCleanup(_autoDutyIpc.Stop, "AutoDuty");
        success &= TryCleanup(_bossModIpc.DisableSoloDutyPreset, "solo duty");
        // A provider can still be cleaning up without having failed; don't queue replacements yet.
        return success && !HasExternalOperation;
    }

    private bool TryCleanup(Action cleanup, string operation)
    {
        try
        {
            cleanup();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to stop {Operation}", operation);
            return false;
        }
    }

    // Plugin unload: no stop commands or notifications, as unloading isn't a quest stopping point.
    internal void Shutdown()
    {
        ClearTasksInternal();
        AutomationType = EAutomationType.Manual;
        StartedQuest = null;
        NextQuest = null;
        GatheringQuest = null;
    }

    public void TryStopOnExpectedQuestAccepted(ElementId questId)
    {
        if (_questsAwaitingAcceptance?.Remove(questId) == true)
            TryStopOnQuestAccepted(questId);
    }

    /// <summary>
    ///     Stops automation when a quest is accepted, if configured or requested via
    ///     <see cref="StopAfterAcceptingNextQuest"/>.
    /// </summary>
    public void TryStopOnQuestAccepted(ElementId questId)
    {
        if (AutomationType == EAutomationType.Manual)
            return;

        if (!ShouldStopOnQuestAccepted(questId, _questFunctions.IsQuestAccepted(questId), out bool configStop))
            return;

        if (_questRegistry.TryGetQuest(questId, out Quest? quest))
        {
            _logger.LogInformation("Reached accept stopping point (quest: {QuestId})", questId);
            if (configStop)
            {
                _chatGui.Print(
                    $"Accepted quest '{quest.Info.Name}', which is configured as a stopping point.",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
            else
            {
                _chatGui.Print(
                    $"Accepted quest '{quest.Info.Name}', stopping as requested.",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
        }

        StartedQuest = null;
        Stop(configStop ? $"Accept stopping point [{questId}] reached" : $"Stop after accept [{questId}]");
    }

    internal bool ShouldStopOnQuestAccepted(ElementId questId, bool isAccepted, out bool configStop)
    {
        configStop = false;
        if (AutomationType == EAutomationType.Manual || !isAccepted)
            return false;

        configStop = _configuration.Stop.Enabled &&
                     _configuration.Stop.QuestsToStopWhenAccepted.Any(x => x == questId);
        return configStop || StopAfterAcceptingNextQuest;
    }

    public override void Stop(string label) => Stop(label, clearQuest: true);

    private void Stop(string label, bool clearQuest)
    {
        StopAfterCurrentQuest = false;
        StopAfterAcceptingNextQuest = false;
        StopBeforeTeleport = false;
        _handlingDeath = false;
        _deathStreakKey = null;
        _deathStreakCount = 0;
        _highlightObject.SetHighlight([]);
        using IDisposable? scope = _logger.BeginScope($"Stop/{label}");
        bool wasRunning = IsRunning || AutomationType != EAutomationType.Manual;
        bool wasAutomatic = AutomationType == EAutomationType.Automatic;
        // Owned operations still need cleanup even if the executor already detached.
        ClearTasksInternal();
        if (!wasRunning)
            return;

        _logger.LogInformation("Stopping automatic questing");
        AutomationType = EAutomationType.Manual;
        if (clearQuest)
        {
            StartedQuest = null;
            NextQuest = null;
            GatheringQuest = null;
        }
        _lastTaskUpdate = DateTime.Now;

        if (wasAutomatic && !_stopConditionComponent.commandExceptions.Any(e => label.Equals(e, StringComparison.OrdinalIgnoreCase)))
        {
            TryCleanup(() => _notificationMasterIpc.NotifyOnFailure(_L("Automatic questing has stopped.")), "stop notification");
            if (_configuration.Stop is { RunCommandAfterStop: true } stop && stop.CommandAfterStop.StartsWith('/'))
                TryCleanup(() => _commandManager.ProcessCommand(stop.CommandAfterStop), "stop command");
        }
        unsafe
        {
            if (_objectTable[0] is IPlayerCharacter player)
            {
                StatusManager* playerStatusManager = player.BattleChara()->GetStatusManager();
                if (playerStatusManager->HasStatus(416)) // Transparent
                {
                    StatusManager.ExecuteStatusOff(416);
                }
            }
        }
    }

    public void StopAllDueToConditionFailed(string label)
    {
        bool hadExternalOperation = HasExternalOperation;
        Stop(label);
        // Our own movement is already stopped; a retained external request doesn't allow stopping a manual route.
        if (hadExternalOperation || HasExternalOperation)
            return;

        _movementController.Stop();
        _combatController.Stop(label);
        _gatheringController.Stop(label);
    }

    private void CheckNextTasks(string label)
    {
        if (!CanPrepareQuestTasks())
        {
            // The old queue must not continue while another provider blocks its replacement.
            Stop("Quest replan blocked");
            return;
        }

        if (AutomationType is not (EAutomationType.Automatic or EAutomationType.SingleQuestA or EAutomationType.SingleQuestB))
        {
            Stop(label);
            return;
        }

        using IDisposable? scope = _logger.BeginScope(label);

        if (!ClearTasksInternal())
        {
            Stop("Task cleanup failed");
            return;
        }

        if (CurrentQuest?.Step is >= 0 and < CompletedStepValue)
            ExecuteNextStep();
        else
            _logger.LogInformation("Couldn't execute next step during Stop() call");

        _lastTaskUpdate = DateTime.Now;
    }

    /// <summary>
    ///     Recovers from the player dying during questing: on the first frame it stops the current
    ///     tasks, then every frame it confirms the return-to-respawn prompt until the player respawns.
    /// </summary>
    private void BeginDeathHandling()
    {
        if (HasExternalOperation)
        {
            Stop("Died during external operation");
            return;
        }

        if (!_handlingDeath)
        {
            _handlingDeath = true;
            _respawnedAt = DateTime.MinValue;
            _lastReturnConfirmAt = DateTime.MinValue;
            _logger.LogWarning("Player died while questing — waiting to return and retry the current step");
            if (!ClearTasksInternal())
            {
                Stop("Task cleanup failed");
                return;
            }
            _movementController.Stop();
        }

        ConfirmReturnPrompt();
    }

    /// <summary>Confirms the "Return?" SelectYesno that appears after death, throttled while it is open.</summary>
    private unsafe void ConfirmReturnPrompt()
    {
        if (DateTime.Now - _lastReturnConfirmAt < TimeSpan.FromMilliseconds(500))
            return;

        if (!_gameGui.TryGetAddonByName("SelectYesno", out AtkUnitBase* addon) || !addon->IsVisible)
            return;

        _lastReturnConfirmAt = DateTime.Now;
        _logger.LogInformation("Confirming return-to-respawn prompt");
        new AddonMaster.SelectYesno((nint)addon).Yes();
    }

    /// <summary>
    ///     Continues recovering after the player regained consciousness: waits for the zone to settle,
    ///     then retries the current step — or stops with an error after too many deaths on it.
    /// </summary>
    private void FinishDeathHandling()
    {
        if (!_clientState.IsLoggedIn ||
            _condition[ConditionFlag.BetweenAreas] ||
            _condition[ConditionFlag.BetweenAreas51] ||
            _objectTable[0] == null)
        {
            _respawnedAt = DateTime.MinValue;
            return;
        }

        if (_respawnedAt == DateTime.MinValue)
        {
            _respawnedAt = DateTime.Now;
            return;
        }

        if (DateTime.Now - _respawnedAt < TimeSpan.FromSeconds(DeathRecoveryGraceSeconds))
            return;

        _handlingDeath = false;

        int deaths = RecordDeath();
        if (deaths >= MaxConsecutiveDeaths)
        {
            _logger.LogError("Player died {Deaths} times on the same step — stopping", deaths);
            _chatGui.PrintError($"You died {MaxConsecutiveDeaths} times, manual intervention needed.",
                CommandHandler.MessageTag, CommandHandler.TagColor);
            StopAllDueToConditionFailed("Died too many times");
            return;
        }

        _logger.LogInformation("Retrying current step after death ({Deaths}/{Max})", deaths, MaxConsecutiveDeaths);
        ExecuteNextStep();
    }

    /// <summary>
    ///     Increments the death counter for the current quest step, resetting it when the step changes.
    /// </summary>
    private int RecordDeath()
    {
        (ElementId QuestId, byte Sequence, int Step)? key = CurrentQuest is { } current
            ? (current.Quest.Id, current.Sequence, current.Step)
            : null;
        if (_deathStreakKey != key)
        {
            _deathStreakKey = key;
            _deathStreakCount = 0;
        }

        return ++_deathStreakCount;
    }

    public void SimulateQuest(IQuestInfo? questInfo, byte sequence, int step)
    {
        if (CanStartNextTask())
            _tracker.SimulateQuest(questInfo, sequence, step);
    }

    public void SimulateQuest(Quest? quest, byte sequence, int step)
    {
        if (CanStartNextTask())
            _tracker.SimulateQuest(quest, sequence, step);
    }

    public void StopSimulate()
    {
        if (HasExternalOperation)
            Stop("Stop simulation during external operation");
        if (HasExternalOperation)
            return;
        _tracker.StopSimulate();
    }

    public void SetNextQuest(Quest? quest)
    {
        if (CanStartNextTask())
            SelectNextQuest(quest, quest != null && _questFunctions.IsQuestAccepted(quest.Id));
    }

    internal void SelectNextQuest(Quest? quest, bool isAccepted)
    {
        _tracker.SetNextQuest(quest);
        // Keep the selection snapshot across task replans, but tie it to this exact selection.
        _nextQuestAwaitingAcceptance = isAccepted ? null : NextQuest;
    }

    internal bool ResolveNextQuest(bool isAccepted, bool isComplete, Action<ElementId> onAccepted)
    {
        if (NextQuest is not { } next || !isAccepted && (next.Quest.Info.IsRepeatable || !isComplete))
            return false;

        bool newlyAccepted = isAccepted && ReferenceEquals(_nextQuestAwaitingAcceptance, next);
        _nextQuestAwaitingAcceptance = null;
        _logger.LogInformation("Next quest {QuestId} accepted or completed", next.Quest.Id);
        if (AutomationType == EAutomationType.SingleQuestA)
        {
            StartedQuest = next;
            AutomationType = EAutomationType.SingleQuestB;
        }

        _logger.LogDebug("Started: {StartedQuest}", StartedQuest?.Quest.Id);
        NextQuest = null;
        if (newlyAccepted)
            onAccepted(next.Quest.Id);
        return true;
    }

    public void SetGatheringQuest(Quest? quest)
    {
        if (CanStartNextTask())
            _tracker.SetGatheringQuest(quest);
    }

    public void SetPendingQuest(QuestProgress? quest)
    {
        if (CanStartNextTask())
            _tracker.SetPendingQuest(quest);
    }

    protected override bool CanStartNextTask() => !HasExternalOperation;

    private bool CanPrepareQuestTasks()
    {
        if (!CanStartNextTask())
            return false;

        if (_autoDutyIpc.IsAvailable() && _autoDutyIpc.GetStopped() != true)
        {
            DebugState = _L("AutoDuty is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        // Manual Knightshopper work has no owner here, so block on busy or unreadable status.
        if (_knightshopperIpc.IsReady() && _knightshopperIpc.GetBusy() != false)
        {
            DebugState = _L("Knightshopper is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        if (_gatherChillIpc.IsAvailable() && _gatherChillIpc.GetBusy() != false)
        {
            DebugState = _L("Gather n Chill is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        // WahTools drives the character while busy.
        if (_feeshIpc.IsAvailable() && _feeshIpc.GetBusy() != false)
        {
            DebugState = _L("WahTools is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        return true;
    }

    public bool CanStartQuest(string label)
    {
        if (CanPrepareQuestTasks())
            return true;

        string reason = ExternalOperationBlockReason ?? DebugState ??
            _L("Check the external operation before restarting.");
        _logger.LogWarning("Quest start {Label} blocked: {Reason}", label, reason);
        _chatGui.PrintError(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
        return false;
    }

    private bool UpdateCraftingOperation()
    {
        if (!_craftingOperation.HasOperation)
        {
            _craftingStatusPoll.Reset();
            return false;
        }

        if (!_craftingStatusPoll.IsRunning || _craftingStatusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
            ObserveCraftingStatus();

        if (_taskQueue.CurrentTaskExecutor is not Craft.DoCraft)
            _craftingOperation.AbandonCurrent();

        if (!_craftingOperation.RequiresReconciliation)
            return false;

        if (IsRunning || AutomationType != EAutomationType.Manual)
            Stop("Artisan requires reconciliation");

        DebugState = _craftingOperation.BlockReason;
        return true;
    }

    private void ObserveCraftingStatus()
    {
        _craftingStatusPoll.Restart();
        try
        {
            _craftingOperation.ObserveStatus(_artisanIpc.GetStatus());
        }
        catch (Exception e)
        {
            _craftingOperation.ObserveStatus(ArtisanStatus.Unavailable);
            _logger.LogWarning(e, "Unable to observe retained Artisan operation");
        }
    }

    public bool TryConfirmArtisanIdle()
    {
        // Confirming only releases the barrier; it never resumes questing.
        if (!_craftingOperation.RequiresReconciliation || IsRunning || AutomationType != EAutomationType.Manual)
            return false;

        ObserveCraftingStatus();
        if (!_craftingOperation.TryConfirmIdle())
            return false;

        DebugState = ExternalOperationBlockReason;
        return true;
    }

    private bool UpdateOwnedOperation(OwnedOperationState operation, bool hasMatchingExecutor,
        Stopwatch statusPoll, Action poll, Action cancel)
    {
        if (!operation.HasOperation)
        {
            statusPoll.Reset();
            return false;
        }

        // Stop scheduling before polling can release an abandoned operation at terminal status.
        if ((operation.IsAbandoned || operation.RequiresReconciliation || !hasMatchingExecutor) &&
            (IsRunning || AutomationType != EAutomationType.Manual))
            Stop("External operation no longer active");

        if (!statusPoll.IsRunning || statusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            statusPoll.Restart();
            if (!hasMatchingExecutor)
                TryCleanup(cancel, "detached external operation");
            if (!TryCleanup(poll, "external operation status"))
            {
                Stop("External operation status unavailable");
                DebugState = operation.BlockReason ?? _L("Unable to observe the external operation.");
                return true;
            }
        }

        if (!operation.HasOperation)
        {
            DebugState = ExternalOperationBlockReason;
            return false;
        }

        if (!operation.RequiresReconciliation && !operation.IsAbandoned && hasMatchingExecutor)
            return false;

        if (IsRunning || AutomationType != EAutomationType.Manual)
            Stop("External operation requires reconciliation");

        DebugState = operation.BlockReason;
        return true;
    }

    public bool TryConfirmKnightshopperIdle() =>
        TryConfirmOwnedOperationIdle(_purchaseOperation, _knightshopperIpc.PollOperation, _knightshopperIpc.ObserveIdle);

    public bool TryConfirmGatherChillIdle() =>
        TryConfirmOwnedOperationIdle(_gatheringOperation, _gatherChillIpc.PollOperation, _gatherChillIpc.ObserveIdle);

    public bool TryConfirmFeeshIdle() =>
        TryConfirmOwnedOperationIdle(_fishingOperation, _feeshIpc.PollOperation, _feeshIpc.ObserveIdle);

    public bool TryConfirmAutoDutyIdle() =>
        TryConfirmOwnedOperationIdle(_autoDutyIpc.Operation, _autoDutyIpc.PollOperation, _autoDutyIpc.ObserveIdle);

    public bool TryConfirmBossModStopped()
    {
        if (IsRunning || AutomationType != EAutomationType.Manual || !_bossModIpc.TryConfirmStopped())
            return false;
        _combatController.TryRetryCleanup();
        DebugState = ExternalOperationBlockReason;
        return true;
    }

    private bool UpdateCombatRecovery()
    {
        if (_combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup)
        {
            // Clear scheduling before a cleanup retry can release the retained owner.
            if (IsRunning || AutomationType != EAutomationType.Manual)
                Stop("Combat cleanup is unresolved");
        }

        if (!_combatStatusPoll.IsRunning || _combatStatusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            _combatStatusPoll.Restart();
            _combatController.TryRetryCleanup();
            try
            {
                // Solo duties keep local movement/interaction tasks between BossMod activations.
                _bossModIpc.PollSoloDuty();
            }
            catch (Exception e)
            {
                if (IsRunning || AutomationType != EAutomationType.Manual)
                {
                    _logger.LogWarning(e, "Solo-duty control was lost");
                    Stop("Solo-duty control was lost");
                }
            }
        }

        if (!_combatController.HasPendingCleanup && !_bossModIpc.HasPendingCleanup)
            return false;
        DebugState = ExternalOperationBlockReason;
        return true;
    }

    private bool TryConfirmOwnedOperationIdle(OwnedOperationState operation, Action poll, Action observeIdle)
    {
        if (!operation.RequiresReconciliation || IsRunning || AutomationType != EAutomationType.Manual)
            return false;

        // Refresh both, so a newly running operation isn't confirmed from a stale status.
        if (!TryCleanup(poll, "external operation confirmation status") ||
            !TryCleanup(observeIdle, "external operation idle confirmation"))
            return false;

        // Polling may already have released an abandoned operation on a terminal result.
        if (operation.HasOperation && !operation.TryConfirmIdle())
            return false;

        DebugState = ExternalOperationBlockReason;
        return true;
    }

    protected override void UpdateCurrentTask()
    {
        if (_craftingOperation.HasOperation &&
            (_craftingOperation.RequiresReconciliation || _taskQueue.CurrentTaskExecutor is not Craft.DoCraft))
            return;

        if (_purchaseOperation.HasOperation &&
            (_purchaseOperation.RequiresReconciliation || _purchaseOperation.IsAbandoned ||
             _taskQueue.CurrentTaskExecutor is not Purchase.DoBuy))
            return;

        if (_gatheringOperation.HasOperation &&
            (_gatheringOperation.RequiresReconciliation || _gatheringOperation.IsAbandoned ||
             _taskQueue.CurrentTaskExecutor is not Gather.StartGathering))
            return;

        if (_fishingOperation.HasOperation &&
            (_fishingOperation.RequiresReconciliation || _fishingOperation.IsAbandoned ||
             _taskQueue.CurrentTaskExecutor is not Fish.DoFish))
            return;

        if (_autoDutyIpc.HasOperation &&
            (_autoDutyIpc.RequiresReconciliation || _autoDutyIpc.Operation.IsAbandoned ||
             _taskQueue.CurrentTaskExecutor is not Duty.StartAutoDutyExecutor))
            return;

        if (_combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup)
            return;

        // The owning executor must still verify external work that occupies the player.
        bool updatingCraft = _taskQueue.CurrentTaskExecutor is Craft.DoCraft && _craftingOperation.HasOperation;
        bool updatingPurchase = _taskQueue.CurrentTaskExecutor is Purchase.DoBuy && _purchaseOperation.HasOperation;
        bool updatingGather = _taskQueue.CurrentTaskExecutor is Gather.StartGathering && _gatheringOperation.HasOperation;
        bool updatingFish = _taskQueue.CurrentTaskExecutor is Fish.DoFish && _fishingOperation.HasOperation;
        bool updatingDuty = _taskQueue.CurrentTaskExecutor is Duty.StartAutoDutyExecutor && _autoDutyIpc.HasOperation;
        if ((updatingCraft || updatingPurchase || updatingGather || updatingFish || updatingDuty) &&
            (!_clientState.IsLoggedIn || _objectTable[0] == null ||
             _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] ||
             _gameFunctions.IsLoadingScreenVisible()))
            return;

        if (_gameFunctions.IsOccupied() && !updatingPurchase && !updatingGather && !updatingFish && !updatingDuty &&
            !(updatingCraft && _condition[ConditionFlag.Crafting]) &&
            // Outcome polling resets its grace timer before returning while occupied.
            _taskQueue.CurrentTaskExecutor is not SinglePlayerDuty.WaitForSinglePlayerDutyOutcomeExecutor &&
            !_gameFunctions.IsOccupiedWithCustomDeliveryNpc(CurrentQuest?.Quest))
            return;

        if (StopBeforeTeleport &&
            _taskQueue.CurrentTaskExecutor == null &&
            _taskQueue.TryPeek(out ITask? nextTask) &&
            TeleportTaskDetector.IsUpcomingTeleport(nextTask, _clientState.TerritoryType))
        {
            string stopping = _L("Stopping before teleport as requested.");
            _logger.LogInformation("{Stopping} (upcoming task: {Task})", stopping, nextTask);
            _chatGui.Print(stopping, CommandHandler.MessageTag, CommandHandler.TagColor);
            if (_configuration.Notifications.NotifyOnStopCondition)
                _notificationMasterIpc.Notify(stopping);
            _movementController.Stop();
            Stop("Stop before teleport");
            return;
        }

        base.UpdateCurrentTask();
    }

    protected override void OnTaskFailure(Exception exception, ITask task)
    {
        if (exception is MovementController.PathfindingFailedException navigationFailure)
            RecordNavigationFailure(navigationFailure, task.ToString());
    }

    internal void RecordNavigationFailure(MovementController.PathfindingFailedException failure, string? task = null) =>
        _movementController.RecordFailure(failure, CurrentQuest?.ToString(),
            task ?? _taskQueue.CurrentTaskExecutor?.CurrentTask.ToString());

    protected override void OnTaskComplete(ITask task)
    {
        if (task is WaitAtEnd.WaitQuestCompleted)
            SimulatedQuest = StartedQuest = null;

    }

    protected override void OnNextStep(ILastTask task) => IncreaseStepCount(task.ElementId, task.Sequence, shouldContinue: true);

    protected override void OnRetryStep()
    {
        if (CurrentQuest == null)
        {
            _logger.LogWarning("OnRetryStep: no current quest, cannot retry");
            return;
        }

        _logger.LogInformation("Retrying current step [{QuestId}, {Sequence}, {Step}]",
                    CurrentQuest?.Quest.Id, CurrentQuest?.Sequence, CurrentQuest?.Step);
        CheckNextTasks("RetryStep");
    }

    public void Start(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = _logger.BeginScope($"Q/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.Automatic;
        ExecuteNextStep();
    }

    public void StartGatheringQuest(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = _logger.BeginScope($"GQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.GatheringOnly;
        ExecuteNextStep();
    }

    public void StartSingleQuest(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = _logger.BeginScope($"SQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.SingleQuestA;
        ExecuteNextStep();
    }

    public void StartSingleStep(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = _logger.BeginScope($"SS/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.Manual;
        ExecuteNextStep();
    }

    private void ExecuteNextStep()
    {
        // Factories and movement cleanup can have side effects before an executor starts.
        if (!CanPrepareQuestTasks())
        {
            Stop("Quest replan blocked");
            return;
        }

        if (!ClearTasksInternal())
        {
            Stop("Task cleanup failed");
            return;
        }

        if (AutomationType == EAutomationType.Automatic && TryQueueNextAcceptOnlyQuest())
            _logger.LogInformation("Accepting queued accept-only quests before other work");

        if (TryPickPriorityQuest())
            _logger.LogInformation("Using priority quest over current quest");

        (QuestSequence? seq, QuestStep? step, bool createTasks) = GetNextStep();
        if (CurrentQuest == null || seq == null)
        {
            if (CurrentQuestDetails?.Progress.Quest.Id is SatisfactionSupplyNpcId &&
                CurrentQuestDetails?.Progress.Sequence == 1 &&
                CurrentQuestDetails?.Progress.Step == CompletedStepValue &&
                CurrentQuestDetails?.Type == ECurrentQuestType.Gathering)
            {
                _logger.LogInformation("Completed delivery quest");
                SetGatheringQuest(quest: null);
                Stop("Gathering quest complete");
            }
            else
            {
                _logger.LogWarning(
                    "Could not retrieve next quest step, not doing anything [{QuestId}, {Sequence}, {Step}]",
                    CurrentQuest?.Quest.Id, CurrentQuest?.Sequence, CurrentQuest?.Step);
            }

            if (CurrentQuest == null || !createTasks)
                return;
        }

        _movementController.Stop();
        _combatController.Stop("Execute next step");
        _gatheringController.Stop("Execute next step");

        try
        {
            foreach (ITask task in _taskCreator.CreateTasks(CurrentQuest.Quest, CurrentQuest.Sequence, seq, step))
            {
                if (SimulatedQuest != null)
                {
                    string repr = task.ToString() ?? "";
                    string[] SimSkip = ["Interact", "Action", "Emote", "Craft", "Unmount"];
                    if (repr.Contains('(') && SimSkip.Contains(repr[..repr.IndexOf('(')]) && step != null && step.TargetTerritoryId.Equals(step.TerritoryId))
                    {
                        _logger.LogInformation("Skipping {Repr} due to simulation", repr);
                        continue;
                    }
                }

                EnqueueQuestTask(task, _questFunctions.IsQuestAccepted);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create tasks [{QuestId}, {Sequence}, {Step}]",
                    CurrentQuest?.Quest.Id, CurrentQuest?.Sequence, CurrentQuest?.Step);
            _chatGui.PrintError("Failed to start next task sequence, please check /xllog for details.", CommandHandler.MessageTag, CommandHandler.TagColor);
            _notificationMasterIpc.NotifyOnFailure(_L("Failed to start next task sequence, stopping.") + " " + _L("Please try Reload Data, fixing manually, or reporting an issue with the 'Stuck?' button."));
            Stop("Tasks failed to create");
        }
    }

    public string ToStatString()
    {
        return _taskQueue.CurrentTaskExecutor?.CurrentTask is { } currentTask
            ? $"{currentTask} (+{_taskQueue.RemainingTasks.Count()})"
            : $"- (+{_taskQueue.RemainingTasks.Count()})";
    }

    public bool HasCurrentTaskExecutorMatching<T>([NotNullWhen(true)] out T? task)
    where T : class, ITaskExecutor
    {
        if (_taskQueue.CurrentTaskExecutor is T t)
        {
            task = t;
            return true;
        }

        task = null;
        return false;
    }

    public bool HasCurrentTaskMatching<T>([NotNullWhen(true)] out T? task)
    where T : class, ITask
    {
        if (_taskQueue.CurrentTaskExecutor?.CurrentTask is T t)
        {
            task = t;
            return true;
        }

        task = null;
        return false;
    }

    public void Skip(ElementId elementId, byte currentQuestSequence)
    {
        lock (_progressLock)
        {
            if (HasExternalOperation)
            {
                Stop("Skip during external operation");
                return;
            }

            if (_taskQueue.CurrentTaskExecutor?.CurrentTask is ISkippableTask)
                CancelCurrentTask();
            else if (_taskQueue.CurrentTaskExecutor != null)
            {
                if (!CancelCurrentTask())
                    return;
                while (_taskQueue.TryPeek(out ITask? task))
                {
                    _taskQueue.TryDequeue(out ITask? _);
                    if (task is ISkippableTask)
                        return;
                }

                if (_taskQueue.AllTasksComplete)
                {
                    Stop("Skip", clearQuest: false);
                    IncreaseStepCount(elementId, currentQuestSequence);
                }
            }
            else
            {
                Stop("SkipNx", clearQuest: false);
                IncreaseStepCount(elementId, currentQuestSequence);
            }
        }
    }

    public void SkipSimulatedTask()
    {
        if (HasExternalOperation)
            Stop("Skip simulated task during external operation");
        else
            CancelCurrentTask();
    }

    public bool IsInterruptible()
    {
        if (HasExternalOperation)
            return false;

        if (AutomationType is EAutomationType.SingleQuestA or EAutomationType.SingleQuestB)
            return false;

        (QuestProgress Progress, ECurrentQuestType Type)? details = CurrentQuestDetails;
        if (details == null)
            return false;

        (QuestProgress currentQuest, ECurrentQuestType type) = details.Value;
        if (type != ECurrentQuestType.Normal || !currentQuest.Quest.Root.Interruptible || currentQuest.Sequence == 0)
            return false;

        if (_priorityManager.Contains(currentQuest.Quest))
            return false;

        // "ifrit bleeds, we can kill it" isn't listed as priority quest, as we accept it during the MSQ 'Moving On'
        // the rest are priority quests, but that's fine here
        if (QuestData.HardModePrimals.Contains(currentQuest.Quest.Id))
            return false;

        if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            return false;

        if (currentQuest.Step > 0)
            return false;

        QuestStep? currentStep = currentQuest.FindStep();
        return currentStep?.AetheryteShortcut != null &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsCompleted.Count ?? 0) == 0 &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsAccepted.Count ?? 0) == 0;
    }

    /// <summary>
    ///     While any priority quest is flagged accept-only and is ready to be accepted, it is queued as <see cref="NextQuest"/> 
    ///     so only its accept step (sequence 0) runs; the quest stays in the priority list to be completed later. 
    ///     The completion phase is therefore deferred until every flagged quest has been accepted (or can no longer be accepted this session)
    /// </summary>
    private bool TryQueueNextAcceptOnlyQuest()
    {
        if (!_priorityManager.HasPendingAcceptOnly)
            return false;

        Quest? next = _priorityManager.AcceptOnlyQuests
            .FirstOrDefault(quest => _questFunctions.IsReadyToAcceptQuest(quest.Id, ignoreLevel: true) &&
                                     _questFunctions.HasClassJobAtQuestLevel(quest.Id));
        if (next == null)
            return false;

        // We are already trying to accept this quest: don't call SetNextQuest again. It rebuilds NextQuest with Step = 0,
        // which would reset the accept sequence every step boundary and loop it forever before it ever reaches the Interact/accept step.
        if (NextQuest?.Quest.Id == next.Id)
            return true;

        _logger.LogInformation("Accept-all: queueing {QuestId} to be accepted before other work", next.Id);
        SetNextQuest(next);
        return true;
    }

    public IReadOnlyList<Quest> QueueOpenQuests()
    {
        List<Quest> added = [];
        foreach (ElementId questId in _questFunctions.OpenQuests)
        {
            if (_priorityManager.Contains(questId))
                continue;

            if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                continue;

            _priorityManager.Add(quest);
            added.Add(quest);
        }

        if (added.Count > 0)
        {
            _logger.LogInformation("Clean up: queued {Count} open quest(s): {QuestIds}",
                added.Count, string.Join(", ", added.Select(x => x.Id)));
        }

        return added;
    }

    public bool TryPickPriorityQuest()
    {
        if (!IsInterruptible() ||
            NextQuest != null ||
            GatheringQuest != null ||
            SimulatedQuest != null ||
            StopAfterCurrentQuest ||
            StopAfterAcceptingNextQuest ||
            StopBeforeTeleport)
            return false;

        ElementId? priorityQuestId = _questFunctions.NextPriorityQuestsThatCanBeAccepted
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (priorityQuestId == null)
            return false;

        // don't start a second priority quest until the first one is resolved
        if (StartedQuest != null && priorityQuestId == StartedQuest.Quest.Id)
            return false;

        if (_questRegistry.TryGetQuest(priorityQuestId, out Quest? quest))
        {
            SetNextQuest(quest);
            return true;
        }

        return false;
    }

    public bool WasLastTaskUpdateWithin(TimeSpan timeSpan)
    {
        if (EzThrottler.Throttle("WasLastTaskUpdateWithin"))
            _logger.LogTrace("Last update: {Update}", _lastTaskUpdate);
        return IsRunning || DateTime.Now <= _lastTaskUpdate.Add(timeSpan);
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (_taskQueue.CurrentTaskExecutor is IConditionChangeAware conditionChangeAware)
            conditionChangeAware.OnConditionChange(flag, value);
    }

    protected override void HandleInterruption(object? sender, EventArgs e)
    {
        if (!IsRunning)
            return;

        if (AutomationType == EAutomationType.Manual)
            return;

        base.HandleInterruption(sender, e);
    }

    public bool StartGathering(uint npcId, uint itemId, Job classJob, int quantity = 1, ushort collectability = 0)
    {
        if (!CanStartQuest("SatisfactionSupply prepare gathering"))
            return false;

        if (itemId > HighQualityItemIdOffset)
            itemId -= HighQualityItemIdOffset;

        if (itemId >= CollectableItemIdOffset)
            itemId -= CollectableItemIdOffset;

        SatisfactionSupplyInfo info = (SatisfactionSupplyInfo)_questData.GetAllByIssuerDataId(npcId)
            .Single(x => x is SatisfactionSupplyInfo);
        if (_questRegistry.TryGetQuest(info.QuestId, out Quest? quest))
        {
            QuestSequence sequence = quest.FindSequence(0)!;

            QuestStep switchClassStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.SwitchClass);
            switchClassStep.TargetClass = classJob switch
            {
                Job.MIN => EExtendedClassJob.Miner,
                Job.BTN => EExtendedClassJob.Botanist,
                var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, message: null)
            };

            QuestStep gatherStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.Gather);
            gatherStep.ItemsToGather =
            [
                new()
                {
                    ItemId = itemId,
                    ItemCount = quantity,
                    Collectability = collectability
                }
            ];
            SetGatheringQuest(quest);
            StartGatheringQuest("SatisfactionSupply prepare gathering");
            return true;
        }

        _chatGui.PrintError($"No associated quest ({info.QuestId}).", "Questionable");
        return false;
    }

    /// <summary>
    /// Caller should ensure _taskQueue.AllTasksComplete before enqueueing.
    /// See: CommandHandler's impl of /qst redeem
    /// </summary>
    /// <param name="task">An instance of a task produced by a factory or other generator</param>
    internal void UnsafeEnqueueManualTask(ITask task) =>
        _taskQueue.Enqueue(task);

    public override void Dispose()
    {
        _toastGui.ErrorToast -= OnErrorToast;
        _condition.ConditionChange -= OnConditionChange;
        base.Dispose();
    }

    public sealed class QuestProgress
    {
        public QuestProgress(Quest quest, byte sequence = 0, int step = 0)
        {
            Quest = quest;
            SetSequence(sequence, step);
        }
        public override string ToString() => $"{Quest.Id}_{Quest.Info.SimplifiedName} / {Sequence} / {Step}";
        public Quest Quest { get; }
        public byte Sequence { get; private set; }
        public int Step { get; private set; }
        public StepProgress StepProgress { get; private set; } = new(DateTime.Now);

        public QuestSequence? FindSequence() => Quest.FindSequence(Sequence);
        public QuestStep? FindStep() => FindSequence()?.FindStep(Step);

        public void SetSequence(byte sequence, int step = 0)
        {
            Sequence = sequence;
            SetStep(step);
        }

        public void SetStep(int step)
        {
            Step = step;
            StepProgress = new(DateTime.Now);
        }

        public void IncreasePointMenuCounter()
        {
            StepProgress = StepProgress with
            {
                PointMenuCounter = StepProgress.PointMenuCounter + 1
            };
        }
    }

    public sealed record StepProgress
    (
        DateTime StartedAt,
        int PointMenuCounter = 0);
}
