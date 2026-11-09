using System.Diagnostics.CodeAnalysis;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
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

    // The quest-accept addon can fail to appear at sequence 0/step 0.
    private const int AcceptStallSeconds = 15;

    private readonly QuestAcceptanceController _acceptance;
    private readonly QuestSelectionController _selection;
    private IQuestSelectionState? _selectionState;
    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuests;
    private readonly IClientState _clientState;
    private readonly CombatController _combatController;
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly GameFunctions _gameFunctions;
    private readonly GameGuiAdapter _gameGui;
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
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestPriorityManager _priorityManager;
    private readonly QuestProgressTracker _tracker;
    private readonly QuestBattleConfigComponent _questBattleConfig;
    private readonly QuestStopPolicy _stopPolicy;
    private readonly TaskCreator _taskCreator;
    private readonly IToastGui _toastGui;
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
        GameGuiAdapter gameGui,
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
        _keyState = keyState;
        _condition = condition;
        _toastGui = toastGui;
        _configuration = configuration;
        _taskCreator = taskCreator;
        _questBattleConfig = questBattleConfig;
        _stopPolicy = stopPolicy;
        _alliedSocietyQuests = alliedSocietyQuests;
        _logger = logger;
        _highlightObject = highlightObject;

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
    public bool StopAfterAttune { get; set; }

    // Set when an attune finishes; the stop waits for the next update so the step isn't cut mid-task.
    internal bool AttuneStopDue { get; private set; }

    public string? DebugState { get; private set; }

    public bool IsQuestWindowOpen => IsQuestWindowOpenFunction?.Invoke() ?? true;
    public Func<bool>? IsQuestWindowOpenFunction { private get; set; } = () => true;

    public bool IsRunning => !_taskQueue.AllTasksComplete;
    public bool HasExternalOperation => _externalOperations.HasOperation;
    internal string? ExternalBlockReason => _externalOperations.BlockReason;
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
                    _stopPolicy.ReportLevelReached(currentLevel);
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

            if (UpdateAcceptedQuests())
                return;

            // Queue currently available accept-only priority quests before completing any.
            if (QuestSelectionController.ShouldQueueAcceptOnly(Slots, AutomationType))
                TryQueueAcceptOnly();

            if (TryStopAtStopPoint())
                return;

            if (QuestSelectionController.ShouldHoldStartedQuest(StartedQuest, SelectionState.BetweenAreas))
            {
                DebugState = "Loading";
                return;
            }

            QuestChoice choice = QuestSelectionController.Choose(new QuestSelectionContext(Slots, _priorityManager.Quests,
                _priorityManager.IsAcceptOnly, AutomationType, SelectionState, _priorityManager.CanInterrupt));
            if (!TryGetQuestToRun(choice, out QuestProgress? questToRun))
                return;

            if (questToRun == null)
            {
                StopWithNoQuest();
                return;
            }

            if (IsWaitingToRun(questToRun))
                return;

            if (questToRun.Sequence != choice.Sequence)
            {
                _highlightObject.SetHighlight([]);
                questToRun.SetSequence(choice.Sequence);
                CheckNextTasks(
                    $"New sequence {questToRun == StartedQuest}/{_questFunctions.GetCurrentQuestInternal(allowNewMsq: true)}");
            }

            CheckQuestStep(questToRun);
        }
    }

    // True ends this update.
    private bool UpdateAcceptedQuests()
    {
        // Observe expected acceptances before quest selection or a sequence change replaces the queue.
        bool wasRunning = AutomationType != EAutomationType.Manual;
        _acceptance.ObserveQueued(_taskQueue, _questFunctions.IsQuestAccepted, TryStopOnQuestAccepted);
        if (wasRunning && AutomationType == EAutomationType.Manual)
            return true;

        if (PendingQuest != null)
        {
            if (!_questFunctions.IsQuestAccepted(PendingQuest.Quest.Id))
            {
                DebugState = $"Waiting for Leve {PendingQuest.Quest.Id}";
                return true;
            }

            StartedQuest = PendingQuest;
            PendingQuest = null;
            TryStopOnQuestAccepted(StartedQuest.Quest.Id);
            if (AutomationType == EAutomationType.Manual)
                return true;
            CheckNextTasks("Pending quest accepted");
        }

        if (SimulatedQuest == null && NextQuest != null)
        {
            bool accepted = _questFunctions.IsQuestAccepted(NextQuest.Quest.Id);
            bool completed = !NextQuest.Quest.Info.IsRepeatable && _questFunctions.IsQuestComplete(NextQuest.Quest.Id);
            if (ResolveNextQuest(accepted, completed, TryStopOnQuestAccepted) && AutomationType == EAutomationType.Manual)
                return true;
        }

        return false;
    }

    private bool TryStopAtStopPoint()
    {
        // Honor the stop-after toggle before starting queued or priority quests.
        EStopPoint? stopPoint = _selection.GetStopPoint(StartedQuest, StopAfterCurrentQuest, SelectionState);
        if (StartedQuest != null && stopPoint != null)
        {
            if (stopPoint == EStopPoint.Configured)
            {
                ElementId questId = StartedQuest.Quest.Id;
                _stopPolicy.ReportStopPoint(StartedQuest.Quest);
                StartedQuest = null;
                Stop($"Stopping point [{questId}] reached");
                if (_configuration.Stop.RemoveWhenCompleteConditionMet)
                    _configuration.Stop.QuestsToStopAfter.Remove(questId);
                return true;
            }

            if (stopPoint == EStopPoint.AfterCurrentQuest)
            {
                ElementId questId = StartedQuest.Quest.Id;
                _stopPolicy.ReportStopAfterQuest(StartedQuest.Quest);
                StartedQuest = null;
                Stop($"Stop after quest [{questId}]");
                return true;
            }
        }

        return false;
    }

    // False ends this update. A null quest with true means there is nothing left to run.
    private bool TryGetQuestToRun(QuestChoice choice, out QuestProgress? questToRun)
    {
        questToRun = choice.Progress;
        if (questToRun != null)
        {
            if (choice.Source != EQuestSource.Simulated &&
                questToRun.Step == 0 &&
                _taskQueue.AllTasksComplete &&
                AutomationType == EAutomationType.Automatic)
                ExecuteNextStep();
            return true;
        }

        ElementId? currentQuestId = choice.QuestId;
        MainScenarioQuestState msqState = choice.MsqState;
        if (currentQuestId == null || currentQuestId.Value == 0)
        {
            if (StartedQuest != null)
            {
                if (msqState == MainScenarioQuestState.Unavailable)
                {
                    return false;
                }

                if (msqState == MainScenarioQuestState.LoadingScreen)
                {
                    return false;
                }

                _logger.LogInformation("No current quest, resetting data [CQI: {CurrrentQuestData}], [CQ: {QuestData}], [MSQ: {MsqData}]", _questFunctions.GetCurrentQuestInternal(allowNewMsq: true), _questFunctions.GetCurrentQuest(), _questFunctions.GetMainScenarioQuest());
                StartedQuest = null;
                Stop("Resetting current quest");
            }

            return true;
        }

        if (StartedQuest == null || StartedQuest.Quest.Id != currentQuestId)
        {
            SwitchToQuest(currentQuestId, choice.Sequence);
            return false;
        }

        questToRun = StartedQuest;
        return true;
    }

    private void SwitchToQuest(ElementId questId, byte sequence)
    {
        if (_questRegistry.TryGetQuest(questId, out Quest? quest))
        {
            SelectStartedQuest(quest, sequence);
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
                    _stopPolicy.ReportLevelTooLow(quest.Info, playerLevel);
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
    }

    private void StopWithNoQuest()
    {
        DebugState = "No quest active";
        if (AutomationType != EAutomationType.Manual &&
            _questFunctions.GetLevelLockedMainScenarioQuest() is var (lockedMsq, playerLevel))
        {
            _stopPolicy.ReportMainScenarioLocked(lockedMsq, playerLevel);
        }

        Stop("No quest active");
    }

    private bool IsWaitingToRun(QuestProgress questToRun)
    {
        if (_gameFunctions.IsOccupied() && !_gameFunctions.IsInCustomDelivery(questToRun.Quest))
        {
            DebugState = "Occupied";
            return true;
        }

        if (_movementController.IsPathfinding)
        {
            DebugState = "Pathfinding is running";
            return true;
        }

        if (_movementController.IsPathRunning)
        {
            DebugState = "Path is running";
            return true;
        }

        if (DateTime.Now < _safeAnimationEnd)
        {
            DebugState = "Waiting for Animation";
            return true;
        }

        return false;
    }

    private void CheckQuestStep(QuestProgress questToRun)
    {
        switch (QuestStepPlanner.CheckStep(questToRun))
        {
            case EStepState.UnknownSequence:
                DebugState = $"Sequence {questToRun.Sequence} not found";
                if (AutomationType != EAutomationType.Manual)
                    _stopPolicy.ReportNoSequence(questToRun);
                Stop("Unknown sequence");
                return;

            case EStepState.Completed:
                DebugState = "Step completed";
                if (!_taskQueue.AllTasksComplete)
                    CheckNextTasks("Step complete");
                else if (AutomationType != EAutomationType.Manual && questToRun.Quest.Id is AetherCurrentId zoneId &&
                         _questFunctions.GetMissingAetherCurrents(zoneId) is { Count: > 0 } missing)
                {
                    _stopPolicy.ReportMissingCurrents(questToRun.Quest, missing);
                    Stop("Aether currents missing");
                }
                return;

            case EStepState.UnknownStep:
                DebugState = "Step not found";
                if (AutomationType != EAutomationType.Manual)
                    _stopPolicy.ReportNoStep(questToRun);
                Stop("Unknown step");
                return;
        }

        DebugState = null;
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
            _stopPolicy.ReportAcceptStop(quest, configStop);

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
        StopAfterAttune = false;
        AttuneStopDue = false;
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
    void IQuestRecoveryHost.ReportDeathLimit(int deaths) => _stopPolicy.ReportDeathLimit(deaths);

    unsafe bool IQuestRecoveryHost.TryConfirmReturnPrompt()
    {
        if (!_gameGui.TryGetAddonByName("SelectYesno", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        _logger.LogInformation("Confirming return-to-respawn prompt");
        new AddonMaster.SelectYesno((nint)addon).Yes();
        return true;
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

    internal bool CanPrepareQuestTasks()
    {
        bool ready = _externalOperations.CanPrepareQuestTasks(out string? reason);
        if (reason != null)
            DebugState = reason;
        return ready;
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

        if (AttuneStopDue)
        {
            _movementController.Stop();
            Stop("Stop after attune");
            return;
        }

        if (StopBeforeTeleport)
        {
            _taskQueue.TryPeek(out ITask? nextTask);
            if (TeleportTaskDetector.GetUpcoming(_taskQueue.CurrentTaskExecutor, nextTask) is { } teleport)
            {
                _stopPolicy.ReportStopBeforeTeleport(teleport);
                _movementController.Stop();
                Stop("Stop before teleport");
                return;
            }
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

        // Only a finished attune counts: already-attuned aetherytes get no task, and shards are a different task.
        if (StopAfterAttune && task is Aetheryte.Attune attune)
        {
            _stopPolicy.ReportStopAfterAttune(attune.AetheryteLocation);
            AttuneStopDue = true;
        }
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

    internal void ExecuteNextStep()
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
            _stopPolicy.ReportTasksFailed(e, CurrentQuest);
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

        if (!_priorityManager.CanInterrupt && QuestSelectionController.IsOtherQuestRunning(StartedQuest, next.Id,
                SelectionState, () => SelectionState.GetCurrentQuest(allowNewMsq: true).CurrentQuest))
            return false;

        _logger.LogInformation("Accept-all: queueing {QuestId} to be accepted before other work", next.Id);
        SetNextQuest(next);
        return true;
    }

    private bool TryPickPriorityQuest()
    {
        if (!QuestSelectionController.IsInterruptible(CurrentQuestDetails, AutomationType, HasExternalOperation,
                _priorityManager.Contains))
            return false;

        ElementId? questId = QuestSelectionController.PickPriorityQuest(Slots,
            StopAfterCurrentQuest || StopAfterNextAccept || StopBeforeTeleport || StopAfterAttune,
            () => _questFunctions.NextPriorityQuests);
        if (questId != null && _questRegistry.TryGetQuest(questId, out Quest? quest))
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
