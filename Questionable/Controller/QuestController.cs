using System.Diagnostics.CodeAnalysis;
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
internal sealed class QuestController : MiniTaskController<QuestController>, IExternalOperationHost, IQuestRecoveryHost
{
    public delegate void AutomationChangedHandler(object sender, EAutomationType e);

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

    private const int CompletedStep = QuestStepPlanner.CompletedStep;

    // Extra grace after the game's animation lock.
    private const float AnimationLockGraceSeconds = 1f;

    private const uint HighQualityItemIdOffset = 1_000_000;

    private const uint CollectableItemIdOffset = 500_000;

    // The quest-accept addon can fail to appear at sequence 0/step 0.
    private const int AcceptStallSeconds = 15;

    private readonly QuestAcceptanceController _acceptance;
    private readonly QuestSelectionController _selection;
    private IQuestSelectionState? _selectionState;
    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuests;
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly CombatController _combatController;
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly GameFunctions _gameFunctions;
    private readonly IGameGuiAdapter _gameGui;
    private readonly GatheringController _gatheringController;
    private readonly OperationCoordinator _externalOperations;
    private readonly QuestRecoveryController _recovery;
    private readonly QuestBattle.RetryCounter _questBattleRetries;
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
    private readonly QuestBattleConfigComponent _questBattleConfig;
    private readonly QuestStopPolicy _stopPolicy;
    private readonly TaskCreator _taskCreator;
    private readonly IToastGui _toastGui;
    private readonly NotificationMasterIpc _notificationMasterIpc;
    private bool _disposed;
    private EAutomationType _automationType;

    private DateTime _lastTaskUpdate = DateTime.Now;

    // Combat can end before quest actions are safe again.
    private DateTime _safeAnimationEnd = DateTime.MinValue;

    public QuestController(
        IClientState clientState,
        IObjectTable objectTable,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        MovementController movementController,
        CombatController combatController,
        GatheringController gatheringController,
        OperationCoordinator externalOperations,
        QuestRecoveryController recovery,
        QuestAcceptanceController acceptance,
        QuestSelectionController selection,
        QuestBattle.RetryCounter questBattleRetries,
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
        TaskToastClassifier toastClassifier,
        NotificationMasterIpc notificationMasterIpc,
        IGameGuiAdapter gameGui,
        QuestBattleConfigComponent questBattleConfig,
        QuestStopPolicy stopPolicy,
        AlliedSocietyQuestFunctions alliedSocietyQuests)
        : base(chatGui, condition, serviceProvider, interruptHandler, toastClassifier,
            notificationMasterIpc.NotifyOnFailure, logger)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _gameFunctions = gameFunctions;
        _gameGui = gameGui;
        _questFunctions = questFunctions;
        _movementController = movementController;
        _combatController = combatController;
        _gatheringController = gatheringController;
        _externalOperations = externalOperations;
        _recovery = recovery;
        _acceptance = acceptance;
        _selection = selection;
        _questBattleRetries = questBattleRetries;
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
        _questBattleConfig = questBattleConfig;
        _stopPolicy = stopPolicy;
        _alliedSocietyQuests = alliedSocietyQuests;
        _logger = logger;
        _highlightObject = highlightObject;
        _notificationMasterIpc = notificationMasterIpc;

        try
        {
            _condition.ConditionChange += OnConditionChange;
            _toastGui.ErrorToast += OnErrorToast;
        }
        catch
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(cleanupError, "Unable to roll back quest controller subscriptions");
            }
            throw;
        }
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
                _acceptance.ClearSelection();
        }
    }

    public QuestProgress? GatheringQuest
    {
        get => _tracker.GatheringQuest;
        private set => _tracker.GatheringQuest = value;
    }

    // Leve acceptance is delayed.
    public QuestProgress? PendingQuest
    {
        get => _tracker.PendingQuest;
        private set => _tracker.PendingQuest = value;
    }

    public QuestPriorityManager PriorityManager => _priorityManager;

    public bool StopAfterCurrentQuest { get; set; }
    public bool StopAfterNextAccept
    {
        get => _acceptance.StopAfterNextAccept;
        set => _acceptance.StopAfterNextAccept = value;
    }

    public bool StopBeforeTeleport { get; set; }

    public string? DebugState { get; private set; }

    public bool IsQuestWindowOpen => IsQuestWindowOpenFunction?.Invoke() ?? true;
    public Func<bool>? IsQuestWindowOpenFunction { private get; set; } = () => true;

    public bool IsRunning => !_taskQueue.AllTasksComplete;
    public bool HasExternalOperation => _externalOperations.HasOperation;
    private string? ExternalBlockReason => _externalOperations.BlockReason;
    ITaskExecutor? IExternalOperationHost.CurrentExecutor => _taskQueue.CurrentTaskExecutor;
    bool IExternalOperationHost.IsActive => IsRunning || AutomationType != EAutomationType.Manual;
    string? IExternalOperationHost.Status { set => DebugState = value; }
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

    public bool IsQuestingStopped => _recovery.IsQuestingStopped(this);

    public event AutomationChangedHandler? AutomationTypeChanged;

    public void Reload()
    {
        lock (_progressLock)
        {
            if (HasExternalOperation)
                Stop("Reload during external operation");

            _logger.LogInformation("Reload, resetting curent quest progress");

            ResetState();

            _questRegistry.Reload();
            _questBattleConfig.Reload();
            _alliedSocietyQuests.Reload();
        }
    }

    private void ResetState()
    {
        _acceptance.ClearSelection();
        _tracker.Reset();
        _safeAnimationEnd = DateTime.MinValue;

        DebugState = null;
    }

    public void Update()
    {
        // Success can replace the outcome task before its next update; observe it independently.
        _questBattleRetries.Observe(_clientState.IsLoggedIn ? ProfileController.CurrentContentId : null,
            questId => _questFunctions.GetQuestProgressInfo(questId)?.Sequence);

        if (_externalOperations.Update(this))
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

        if (UpdateStopAndDeath())
            return;

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
            (_taskQueue.AllTasksComplete || _taskQueue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.QuestAccepted)
            && CurrentQuest is { Sequence: 0, Step: 0 } or { Sequence: 0, Step: CompletedStep }
            && DateTime.Now >= CurrentQuest.StepProgress.StartedAt.AddSeconds(AcceptStallSeconds))
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

    internal bool UpdateStopAndDeath() => _recovery.Update(this);

    private void UpdateCurrentQuest()
    {
        lock (_progressLock)
        {
            // Let the running executor verify its objective before quest or sequence changes discard it.
            if (HasExternalOperation)
                return;

            DebugState = null;

            if (!_clientState.IsLoggedIn)
            {
                ResetState();
                DebugState = "Not logged in";
                return;
            }

            // Observe expected acceptances before quest selection or a sequence change replaces the queue.
            bool wasRunning = AutomationType != EAutomationType.Manual;
            _acceptance.ObserveQueued(_taskQueue, _questFunctions.IsQuestAccepted, TryStopOnQuestAccepted);
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
            if (QuestSelectionController.ShouldQueueAcceptOnly(Slots, AutomationType))
                TryQueueAcceptOnly();

            // Honor the stop-after toggle before starting queued or priority quests.
            EStopPoint? stopPoint = _selection.GetStopPoint(StartedQuest, StopAfterCurrentQuest, SelectionState);
            if (StartedQuest != null && stopPoint != null)
            {
                if (stopPoint == EStopPoint.Configured)
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

                if (stopPoint == EStopPoint.AfterCurrentQuest)
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

            if (QuestSelectionController.ShouldHoldStartedQuest(StartedQuest, SelectionState.BetweenAreas))
            {
                DebugState = "Loading";
                return;
            }

            QuestChoice choice = QuestSelectionController.Choose(new QuestSelectionContext(Slots, _priorityManager.Quests,
                _priorityManager.IsAcceptOnly, AutomationType, SelectionState));
            QuestProgress? questToRun;
            byte currentSequence = choice.Sequence;
            if (choice.Progress != null)
            {
                questToRun = choice.Progress;
                if (choice.Source != EQuestSource.Simulated &&
                    questToRun.Step == 0 &&
                    _taskQueue.AllTasksComplete &&
                    AutomationType == EAutomationType.Automatic)
                    ExecuteNextStep();
            }
            else
            {
                ElementId? currentQuestId = choice.QuestId;
                MainScenarioQuestState msqState = choice.MsqState;
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
                            int playerLevel = PlayerState.Instance()->CurrentLevel;
                            if (QuestSelectionController.IsLevelTooHigh(playerLevel, quest))
                            {
                                _logger.LogInformation(
                                    "Stopping automation, player level ({PlayerLevel}) < quest level ({QuestLevel}",
                                    playerLevel, quest.Info.Level);
                                _chatGui.Print(_LF("Stopped: '{0}' needs level {1} (you're level {2}).",
                                    quest.Info.Name, quest.Info.Level, playerLevel), CommandHandler.MessageTag, CommandHandler.TagColor);
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
                if (AutomationType != EAutomationType.Manual &&
                    _questFunctions.GetLevelLockedMainScenarioQuest() is var (lockedMsq, playerLevel))
                {
                    _logger.LogInformation("Stopping automation, next MSQ {QuestId} needs level {QuestLevel} (player level {PlayerLevel})",
                        lockedMsq.QuestId, lockedMsq.Level, playerLevel);
                    _chatGui.Print(_LF("Stopped: '{0}' needs level {1} (you're level {2}).",
                        lockedMsq.Name, lockedMsq.Level, playerLevel), CommandHandler.MessageTag, CommandHandler.TagColor);
                }

                Stop("No quest active");
                return;
            }

            if (_gameFunctions.IsOccupied() && !_gameFunctions.IsInCustomDelivery(questToRun.Quest))
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

            switch (QuestStepPlanner.CheckStep(questToRun))
            {
                case EStepState.UnknownSequence:
                    DebugState = $"Sequence {questToRun.Sequence} not found";
                    if (AutomationType != EAutomationType.Manual)
                        _chatGui.PrintError(_LF("Stopped: no path for '{0}' sequence {1}. Try Reload Data or report it with Stuck?",
                            questToRun.Quest.Info.Name, questToRun.Sequence), CommandHandler.MessageTag, CommandHandler.TagColor);
                    Stop("Unknown sequence");
                    return;

                case EStepState.Completed:
                    DebugState = "Step completed";
                    if (!_taskQueue.AllTasksComplete)
                        CheckNextTasks("Step complete");
                    return;

                case EStepState.UnknownStep:
                    DebugState = "Step not found";
                    if (AutomationType != EAutomationType.Manual)
                        _chatGui.PrintError(_LF("Stopped: no path for '{0}' sequence {1} step {2}. Try Reload Data or report it with Stuck?",
                            questToRun.Quest.Info.Name, questToRun.Sequence, questToRun.Step), CommandHandler.MessageTag, CommandHandler.TagColor);
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
        QuestProgress? previous = StartedQuest;
        StartedQuest = QuestSelectionController.ResumeOrStart(_tracker.DisplacedQuest, quest, sequence);
        _tracker.DisplacedQuest = previous != null && previous.Quest.Id != quest.Id ? previous : null;
        if (StartedQuest.Step > 0)
            _logger.LogInformation("Resuming at sequence {Sequence} step {Step}", StartedQuest.Sequence, StartedQuest.Step);
    }

    internal void EnqueueTask(ITask task, Func<ElementId, bool> isAccepted)
    {
        _acceptance.TrackQueued(task, isAccepted);
        _taskQueue.Enqueue(task);
    }

    public (QuestSequence? Sequence, QuestStep? Step, bool CreateTasks) GetNextStep() => _tracker.GetNextStep();

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
            QuestProgress? current = CurrentQuest;
            StepAdvance advance = QuestStepPlanner.Advance(current, seq, step, questId, sequence);
            switch (advance.Kind)
            {
                case EStepAdvance.NoStep:
                    _logger.LogWarning("Unable to retrieve next quest step, not increasing step count");
                    return;

                case EStepAdvance.OtherQuest:
                    _logger.LogWarning(
                        "Ignoring 'increase step count' for different quest (expected {ExpectedQuestId}, but we are at {CurrentQuestId}",
                        questId, current!.Quest.Id);
                    return;

                case EStepAdvance.OtherSequence:
                    _logger.LogWarning(
                        "Ignoring 'increase step count' for different sequence (expected {ExpectedSequence}, but we are at {CurrentSequence}",
                        sequence, seq!.Sequence);
                    return;
            }

            _logger.LogInformation("Increasing step count from {CurrentValue}", current!.Step);
            current.SetStep(advance.NextStep);
        }

        using IDisposable? scope = _logger.BeginScope("IncStepCt");
        if (shouldContinue && AutomationType != EAutomationType.Manual)
            ExecuteNextStep();
    }

    private bool ClearTasksInternal()
    {
        _acceptance.ClearQueued();
        return _externalOperations.CancelAll(_taskQueue.Reset);
    }

    // Unloading must not stop automation or show notifications.
    internal void Shutdown()
    {
        ClearTasksInternal();
        AutomationType = EAutomationType.Manual;
        StartedQuest = null;
        NextQuest = null;
        GatheringQuest = null;
    }

    public void TryStopOnQueuedAccept(ElementId questId)
    {
        if (_acceptance.TryConsumeQueued(questId))
            TryStopOnQuestAccepted(questId);
    }

    public void TryStopOnQuestAccepted(ElementId questId)
    {
        if (AutomationType == EAutomationType.Manual)
            return;

        if (!_acceptance.ShouldStop(questId, _questFunctions.IsQuestAccepted(questId),
                AutomationType == EAutomationType.Manual, out bool configStop))
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

    public override void Stop(string label) => Stop(label, clearQuest: true);

    public void StopFromUi(string label) => Stop(label, clearQuest: true, policyReason: QuestStopPolicy.UiStopReason);

    private void Stop(string label, bool clearQuest, string? policyReason = null)
    {
        StopAfterCurrentQuest = false;
        StopAfterNextAccept = false;
        StopBeforeTeleport = false;
        _recovery.Reset();
        _highlightObject.SetHighlight([]);
        using IDisposable? scope = _logger.BeginScope($"Stop/{label}");
        bool wasRunning = IsRunning || AutomationType != EAutomationType.Manual;
        bool wasAutomatic = AutomationType == EAutomationType.Automatic;
        // Plugin work still needs cleanup even if its executor is already gone.
        ClearTasksInternal();
        if (!wasRunning)
            return;

        _logger.LogInformation("Stopping automatic questing: {Label}", label);
        AutomationType = EAutomationType.Manual;
        if (clearQuest)
        {
            StartedQuest = null;
            NextQuest = null;
            GatheringQuest = null;
        }
        _lastTaskUpdate = DateTime.Now;

        _stopPolicy.Apply(policyReason ?? label, wasAutomatic);
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

    public void StopAll(string label)
    {
        bool hadExternalOperation = HasExternalOperation;
        Stop(label);
        // Our own movement is already stopped; a plugin request still running doesn't allow stopping a manual route.
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

        if (CurrentQuest?.Step is >= 0 and < CompletedStep)
            ExecuteNextStep();
        else
            _logger.LogInformation("Couldn't execute next step during Stop() call");

        _lastTaskUpdate = DateTime.Now;
    }

    bool IQuestRecoveryHost.IsManual => AutomationType == EAutomationType.Manual;
    bool IQuestRecoveryHost.HasQueuedTasks => IsRunning;
    bool IQuestRecoveryHost.IsLoggedIn => _clientState.IsLoggedIn;
    bool IQuestRecoveryHost.EscapeRequested =>
        _configuration.General.UseEscToCancelQuesting && _keyState[VirtualKey.ESCAPE];
    bool IQuestRecoveryHost.IsUnconscious => _condition[ConditionFlag.Unconscious];
    bool IQuestRecoveryHost.ProviderHandlesDeath => _externalOperations.HandlesPlayerDeath(_taskQueue.CurrentTaskExecutor);
    bool IQuestRecoveryHost.IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    bool IQuestRecoveryHost.HasPlayer => _objectTable[0] != null;
    (ElementId QuestId, byte Sequence, int Step)? IQuestRecoveryHost.CurrentStep => CurrentQuest is { } current
        ? (current.Quest.Id, current.Sequence, current.Step)
        : null;
    bool IQuestRecoveryHost.ClearTasks() => ClearTasksInternal();
    void IQuestRecoveryHost.StopMovement() => _movementController.Stop();
    void IQuestRecoveryHost.RetryCurrentStep() => ExecuteNextStep();
    void IQuestRecoveryHost.ReportDeathLimit(int deaths) =>
        _chatGui.PrintError($"You died {deaths} times, manual intervention needed.", CommandHandler.MessageTag, CommandHandler.TagColor);

    unsafe bool IQuestRecoveryHost.TryConfirmReturnPrompt()
    {
        if (!_gameGui.TryGetAddonByName("SelectYesno", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        _logger.LogInformation("Confirming return-to-respawn prompt");
        new AddonMaster.SelectYesno((nint)addon).Yes();
        return true;
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
        _acceptance.Select(NextQuest, isAccepted);
    }

    internal bool ResolveNextQuest(bool isAccepted, bool isComplete, Action<ElementId> onAccepted)
    {
        if (NextQuest is not { } next || !_acceptance.TryResolve(next, isAccepted, isComplete, out bool newlyAccepted))
            return false;

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
        bool ready = _externalOperations.CanPrepareQuestTasks(out string? reason);
        if (reason != null)
            DebugState = reason;
        return ready;
    }

    public bool CanStartQuest(string label)
    {
        if (CanPrepareQuestTasks())
            return true;

        string reason = ExternalBlockReason ?? DebugState ??
            _L("Check the external operation before restarting.");
        _logger.LogWarning("Quest start {Label} blocked: {Reason}", label, reason);
        _chatGui.PrintError(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
        return false;
    }

    // A manual provider stop clears only its block; it never resumes questing.
    public bool TryConfirmIdle(IOperationProvider provider) => _externalOperations.TryConfirmIdle(provider, this);

    protected override void UpdateCurrentTask()
    {
        if (!_externalOperations.CanUpdateTask(_taskQueue.CurrentTaskExecutor))
            return;

        // The current executor must still check plugin work that occupies the player.
        IOperationProvider? provider = _externalOperations.GetProvider(_taskQueue.CurrentTaskExecutor);
        if (provider != null &&
            (!_clientState.IsLoggedIn || _objectTable[0] == null ||
             _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] ||
             _gameFunctions.IsLoadingScreenVisible()))
            return;

        if (_gameFunctions.IsOccupied() && provider?.CanUpdateWhileOccupied != true &&
            // Outcome polling resets its grace timer before returning while occupied.
            _taskQueue.CurrentTaskExecutor is not QuestBattle.WaitForOutcomeExecutor &&
            !_gameFunctions.IsInCustomDelivery(CurrentQuest?.Quest))
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
        if (exception is MovementController.PathfindingException navigationFailure)
            RecordNavigationFailure(navigationFailure, task.ToString());
    }

    internal void RecordNavigationFailure(MovementController.PathfindingException failure, string? task = null) =>
        _movementController.RecordFailure(failure, CurrentQuest?.ToString(),
            task ?? _taskQueue.CurrentTaskExecutor?.CurrentTask.ToString());

    protected override void OnTaskComplete(ITask task)
    {
        if (task is WaitAtEnd.QuestCompleted)
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

        if (AutomationType == EAutomationType.Automatic && TryQueueAcceptOnly())
            _logger.LogInformation("Accepting queued accept-only quests before other work");

        if (TryPickPriorityQuest())
            _logger.LogInformation("Using priority quest over current quest");

        (QuestSequence? seq, QuestStep? step, bool createTasks) = GetNextStep();
        if (CurrentQuest == null || seq == null)
        {
            if (QuestStepPlanner.IsGatheringDeliveryDone(CurrentQuestDetails))
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
                EnqueueTask(task, _questFunctions.IsQuestAccepted);
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

    public void Skip(ElementId elementId, byte sequence)
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
                    IncreaseStepCount(elementId, sequence);
                }
            }
            else
            {
                Stop("SkipNx", clearQuest: false);
                IncreaseStepCount(elementId, sequence);
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

    private QuestSlots Slots => new(StartedQuest, NextQuest, GatheringQuest, SimulatedQuest);
    private IQuestSelectionState SelectionState => _selectionState ??= new SelectionStateAdapter(this);

    private sealed class SelectionStateAdapter(QuestController controller) : IQuestSelectionState
    {
        public bool InDuty => controller._condition[ConditionFlag.BoundByDuty];
        public bool BetweenAreas => controller._condition[ConditionFlag.BetweenAreas] || controller._condition[ConditionFlag.BetweenAreas51];
        public bool IsAccepted(ElementId questId) => controller._questFunctions.IsQuestAccepted(questId);
        public bool IsComplete(ElementId questId) => controller._questFunctions.IsQuestComplete(questId);
        public bool IsReadyToAccept(ElementId questId, bool ignoreLevel = false) =>
            controller._questFunctions.IsReadyToAcceptQuest(questId, ignoreLevel);
        public bool HasClassJobAtQuestLevel(ElementId questId) => controller._questFunctions.HasClassJobAtQuestLevel(questId);
        public byte GetSequence(ElementId questId) => controller._questFunctions.GetQuestProgressInfo(questId)?.Sequence ?? 0;
        public QuestReference GetCurrentQuest(bool allowNewMsq) => controller._questFunctions.GetCurrentQuest(allowNewMsq);
    }

    // Accept-only priorities run sequence 0, then remain for normal completion.
    private bool TryQueueAcceptOnly()
    {
        if (!_priorityManager.HasPendingAcceptOnly)
            return false;

        Quest? next = QuestSelectionController.PickAcceptOnly(_priorityManager.AcceptOnlyQuests, SelectionState);
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
            StopAfterNextAccept ||
            StopBeforeTeleport)
            return false;

        ElementId? priorityQuestId = _questFunctions.NextPriorityQuests
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

    internal void UnsafeEnqueueManualTask(ITask task) =>
        _taskQueue.Enqueue(task);

    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _toastGui.ErrorToast -= OnErrorToast;
        }
        finally
        {
            try
            {
                _condition.ConditionChange -= OnConditionChange;
            }
            finally
            {
                base.Dispose();
            }
        }
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
