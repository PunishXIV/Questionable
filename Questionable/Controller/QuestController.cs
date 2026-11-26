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
using static Questionable.Controller.Steps.ITaskExecutor;
using static Questionable.External.IPCUtils;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestController : MiniTaskController<QuestController>, IExternalOperationHost, IQuestRecoveryHost
{
    public enum EAutomationType
    {
        Manual,
        Automatic,
        SingleQuestA,
        SingleQuestB
    }

    public enum ECurrentQuestType
    {
        Normal,
        Next,
        Simulated
    }

    private const int CompletedStep = QuestStepPlanner.CompletedStep;

    private const float AnimationLockGraceSeconds = 1f;

    // The quest-accept addon can fail to appear at sequence 0/step 0.
    private const int AcceptStallSeconds = 15;
    private (ElementId? Quest, int Count) _acceptStalls;

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
    private readonly ClassJobUtils _classJobUtils;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestPriorityManager _priorityManager;
    private readonly QuestProgressTracker _tracker;
    private readonly QuestBattleConfigComponent _questBattleConfig;
    private readonly QuestStopPolicy _stopPolicy;
    private readonly QuestReporter _reports;
    private readonly StuckWatch _stuckWatch;
    private readonly StoryCatchUp _storyCatchUp;
    private readonly PriorityPresets _presets;
    private readonly TaskCreator _taskCreator;
    private readonly IToastGui _toastGui;
    private bool _disposed;
    private EAutomationType _automationType;
    private bool _manualTasks;
    private IDailiesRun? _dailies;
    private HashSet<ElementId>? _dailyAccepted;
    private bool _retryDaily;
    private DateTime? _dailyMissingSince;

    public bool IsDailiesRun => _dailies != null;

    internal bool BeginDailies(IDailiesRun run)
    {
        if (_dailies != null || !_clientState.IsLoggedIn || IsRunning || HasExternalOperation ||
            SimulatedQuest != null || !CanPrepareQuestTasks())
            return false;

        StartedQuest = NextQuest = PendingQuest = null;
        _tracker.DisplacedQuest = null;
        _dailies = run;
        _dailyAccepted = [];
        _dailyMissingSince = null;
        _retryDaily = false;
        _priorityManager.IsDailiesRun = true;
        return true;
    }

    private void NoteDailyAccepted(ElementId id)
    {
        if (_dailies != null && (_dailyAccepted ??= []).Add(id))
            _dailies.Accepted(id);
    }

    private void NoteDailyCompleted(ElementId id)
    {
        _dailyMissingSince = null;
        if (_dailies != null && _dailyAccepted?.Remove(id) == true)
            _dailies.Completed(id);
    }

    private bool ObserveDaily()
    {
        if (_dailies == null || StartedQuest is not { } current)
            return false;
        ElementId id = current.Quest.Id;
        bool wasAccepted = _dailyAccepted?.Contains(id) == true;
        if (_questFunctions.IsQuestAccepted(id))
        {
            _dailyMissingSince = null;
            NoteDailyAccepted(id);
            return !wasAccepted;
        }
        if (!wasAccepted)
            return false;
        // Daily completion uses today's flag, not the permanent repeatable completion bit.
        if (_questFunctions.IsPriorityQuestDone(id))
        {
            NoteDailyCompleted(id);
            return true;
        }

        // Accepted and completed state can arrive in separate game updates. Do not replan in between.
        _dailyMissingSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow < _dailyMissingSince.Value.AddSeconds(10))
            return false;

        Stop("Daily quest is no longer accepted");
        return true;
    }

    private void UpdateDailiesQuest()
    {
        if (QuestSelectionController.ShouldHoldStartedQuest(StartedQuest, SelectionState.BetweenAreas))
            return;
        bool changed = ObserveDaily();
        if (_dailies == null || _dailyMissingSince != null || TryStopAtStopPoint())
            return;
        if (StartedQuest is { } current && SelectionState.GetSequence(current.Quest.Id) != current.Sequence)
            changed = true;
        if (changed || _taskQueue.AllTasksComplete)
            ExecuteNextStep();
    }

    private bool ChooseDaily()
    {
        if (_dailies == null)
            return false;
        ObserveDaily();
        if (_dailies == null || _dailyMissingSince != null)
            return false;

        bool hold = StartedQuest is { Step: > 0 } current &&
                    SelectionState.GetSequence(current.Quest.Id) == current.Sequence &&
                    (_questFunctions.IsQuestAccepted(current.Quest.Id) || current.Sequence == 0);
        Quest? quest = _retryDaily || hold ? StartedQuest?.Quest : _dailies.Choose(StartedQuest);
        _retryDaily = false;
        if (quest == null)
        {
            if (_dailies.StopReason is { } reason)
                Stop(reason);
            else
            {
                FinishDailies(true);
                StopFinished("Dailies finished");
            }
            return false;
        }

        byte sequence = SelectionState.GetSequence(quest.Id);
        if (StartedQuest?.Quest.Id != quest.Id)
            SelectStartedQuest(quest, sequence);
        else if (StartedQuest.Sequence != sequence)
            StartedQuest.SetSequence(sequence);
        if (_questFunctions.IsQuestAccepted(quest.Id))
            NoteDailyAccepted(quest.Id);
        return true;
    }

    private bool TryDailyFailure(string reason)
    {
        if (_dailies == null || AutomationType != EAutomationType.Automatic ||
            !_clientState.IsLoggedIn || CurrentQuest is not { } current ||
            HasExternalOperation || !CanPrepareQuestTasks() || !ClearTasksInternal())
            return false;

        EDailiesFailure action = _dailies.Fail(current.Quest.Id, reason);
        _stuckWatch.Reset();
        if (action == EDailiesFailure.Stop)
            return false;

        if (action == EDailiesFailure.Retry)
        {
            current.SetStep(current.Step);
            _retryDaily = true;
        }
        else
        {
            StartedQuest = NextQuest = null;
            _tracker.DisplacedQuest = null;
        }
        return true;
    }

    private IDailiesRun? DetachDailies()
    {
        IDailiesRun? run = _dailies;
        _dailies = null;
        _dailyAccepted?.Clear();
        _dailyMissingSince = null;
        _retryDaily = false;
        if (run != null)
            _priorityManager.IsDailiesRun = false;
        return run;
    }

    private void FinishDailies(bool completed) => DetachDailies()?.Finish(completed);

    private DateTime _lastTaskUpdate = DateTime.Now;

    // Combat can end before quest actions are safe again.
    private DateTime _safeAnimationEnd = DateTime.MinValue;
    private bool _wigglyQuest;
    private DateTime? _journalSince;
    private DateTime? _emptySince;
    private (ITask? Task, DateTime Since) _acceptWait;

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
        StuckWatch stuckWatch,
        StoryCatchUp storyCatchUp,
        PriorityPresets presets,
        AlliedSocietyQuestFunctions alliedSocietyQuests,
        QuestReporter reports,
        ClassJobUtils classJobUtils)
        : base(chatGui, condition, serviceProvider, interruptHandler, toastClassifier,
            notificationMasterIpc.NotifyOnFailure, logger)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _gameFunctions = gameFunctions;
        _gameGui = gameGui;
        _questFunctions = questFunctions;
        _classJobUtils = classJobUtils;
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
        _stuckWatch = stuckWatch;
        _storyCatchUp = storyCatchUp;
        _presets = presets;
        _alliedSocietyQuests = alliedSocietyQuests;
        _reports = reports;
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

    internal const string FinishedReason = "Finished";

    internal Func<Quest?>? NextInChain { get; set; }

    public string StopReason { get; private set; } = "";

    public string StopKind { get; private set; } = "";

    public event EventHandler<StopEventArgs>? Stopped;
    public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;

    // A long walk to the giver used up the 15 s before the accept was even tried (182).
    internal static DateTime AcceptStallStart(DateTime stepStart, DateTime? waitStart) =>
        waitStart is { } wait && wait > stepStart ? wait : stepStart;

    internal static string StopKindName(EProblemKind? kind, bool finished) =>
        kind?.ToString() ?? (finished ? "Finished" : "");

    public bool IsWaitingOnPlayer =>
        CurrentQuest?.FindStep()?.InteractionType is EInteractionType.Instruction or
            EInteractionType.WaitForManualProgress or EInteractionType.Snipe &&
        _taskQueue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.NextStepOrSequence;

    public string CurrentTaskName => _taskQueue.CurrentTaskExecutor?.CurrentTask?.ToString() ?? "";

    public EAutomationType AutomationType
    {
        get => _automationType;
        set
        {
            if (value == _automationType)
                return;

            if (_automationType == EAutomationType.Automatic && value != EAutomationType.Automatic)
                FinishDailies(false);

            _logger.LogInformation("Setting automation type to {NewAutomationType} (previous: {OldAutomationType})",
                value, _automationType);
            if (_automationType == EAutomationType.Manual)
            {
                StopReason = "";
                StopKind = "";
                StartedAnyTask = false;
            }
            _automationType = value;
            _priorityManager.IsManualRun = value == EAutomationType.Manual;
            AutomationTypeChanged?.Invoke(this, new AutomationEventArgs(value));
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

    public Configuration.EEndAction? EndActionOnce { get; set; }

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

    public event EventHandler<AutomationEventArgs>? AutomationTypeChanged;

    public void Reload()
    {
        lock (_progressLock)
        {
            if (HasExternalOperation)
                Stop("Reload during external operation");
            else if (IsDailiesRun)
                Stop("Reload during dailies");

            _logger.LogInformation("Reload, resetting curent quest progress");

            ResetState();

            _questRegistry.Reload();
            _questBattleConfig.Reload();
            _alliedSocietyQuests.Reload();
        }
    }

    private void ResetState()
    {
        FinishDailies(false);
        _acceptance.ClearSelection();
        _tracker.Reset();
        _safeAnimationEnd = DateTime.MinValue;

        DebugState = null;
    }

    // WigglyQuest drives the same character and quests, so nothing of ours may run next to it.
    internal bool BlockedByWigglyQuest()
    {
        if (EzThrottler.Throttle("WigglyQuest", 1000))
            _wigglyQuest = IPCSubscriber.IsInstalled("WigglyQuest");
        return _wigglyQuest;
    }

    internal static void PrintWigglyQuestBlock() =>
        Svc.Chat.PrintError(_L("WigglyQuest is loaded. It controls your character and quests too, so Questionable won't do anything while it's loaded. Disable WigglyQuest to use Questionable."),
            CommandHandler.MessageTag, CommandHandler.TagColor);

    public void Update()
    {
        if (BlockedByWigglyQuest())
        {
            if (IsRunning || AutomationType != EAutomationType.Manual)
            {
                Stop("WigglyQuest is loaded");
                PrintWigglyQuestBlock();
            }

            return;
        }

        if (_dailies != null && !_clientState.IsLoggedIn)
            Stop("Logged out during dailies");
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

        if (_dailyMissingSince != null)
            return;

        if (_configuration.Stop.Enabled && _configuration.Stop.LevelToStopAfter)
        {
            unsafe
            {
                short currentLevel = PlayerState.Instance()->CurrentLevel;
                if (QuestSelectionController.IsLevelStopDue(currentLevel, _configuration.Stop.TargetLevel,
                        IsRunning && AutomationType != EAutomationType.Manual,
                        HasExternalOperation || SelectionState.InDuty))
                {
                    _stopPolicy.ReportLevelReached(currentLevel);
                    StopFinished(string.Create(CultureInfo.InvariantCulture, $"Level stop condition reached [{currentLevel}]"));
                    return;
                }
            }
        }

        ITask? acceptTask = _taskQueue.CurrentTaskExecutor?.CurrentTask as WaitAtEnd.QuestAccepted;
        if (acceptTask != null && !ReferenceEquals(_acceptWait.Task, acceptTask))
            _acceptWait = (acceptTask, DateTime.Now);
        if (!HasExternalOperation && AutomationType == EAutomationType.Automatic &&
            (_taskQueue.AllTasksComplete || acceptTask != null)
            && CurrentQuest is { Sequence: 0, Step: 0 } or { Sequence: 0, Step: CompletedStep }
            && DateTime.Now >= AcceptStallStart(CurrentQuest.StepProgress.StartedAt,
                acceptTask != null ? _acceptWait.Since : null).AddSeconds(AcceptStallSeconds))
        {
            if (_dailies != null)
            {
                if (!TryDailyFailure("Quest acceptance did not finish"))
                    Stop("Daily acceptance retry blocked");
                return;
            }
            lock (_progressLock)
            {
                _logger.LogWarning("Quest accept apparently didn't work out, resetting progress");
                CurrentQuest.SetStep(0);
            }

            int stalls = Equals(_acceptStalls.Quest, CurrentQuest.Quest.Id) ? _acceptStalls.Count + 1 : 1;
            int maxStalls = Math.Clamp(_configuration.General.AutoStepRefreshMaxTries, 1, 10);
            _acceptStalls = (CurrentQuest.Quest.Id, stalls);
            if (stalls > maxStalls)
            {
                _acceptStalls = default;
                _stopPolicy.ReportStuckLimit(CurrentQuest.Quest, maxStalls);
                _reports.Problem(EProblemKind.Stuck);
                Stop("Stuck after retries");
                return;
            }

            ExecuteNextStep();
            return;
        }

        if (UpdateStuckWatch())
            return;

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

    private bool UpdateStuckWatch()
    {
        Configuration.GeneralConfiguration general = _configuration.General;
        ITask? task = _taskQueue.CurrentTaskExecutor?.CurrentTask;
        // After the last step the queue is empty, so IsRunning alone would never redo a sequence the game didn't count.
        bool redoSequence = CurrentQuest is { Step: CompletedStep } finished && task == null &&
                            StuckWatch.CanRedoSequence(finished.FindSequence());
        if (!general.AutoStepRefreshEnabled || AutomationType != EAutomationType.Automatic ||
            !(IsRunning || redoSequence) || CurrentQuest is not { } current || _objectTable[0] is not { } player)
        {
            _stuckWatch.Reset();
            return false;
        }

        int delay = Math.Clamp(general.AutoStepRefreshDelaySeconds, 30, 180);
        int maxTries = _dailies != null ? 1 : Math.Clamp(general.AutoStepRefreshMaxTries, 1, 10);
        bool paused = IsStuckPaused() || !(redoSequence || StuckWatch.CanRetry(current.FindStep()?.InteractionType, task));
        StuckWatch.EResult result = _stuckWatch.Update((current.Quest.Id, current.Sequence, current.Step),
            task, player.Position, paused, TimeSpan.FromSeconds(delay), maxTries);
        switch (result)
        {
            case StuckWatch.EResult.Retry:
                if (_dailies != null)
                {
                    if (!TryDailyFailure("No progress on the current step"))
                        Stop("Daily retry blocked");
                    return true;
                }
                _stopPolicy.ReportStuckRetry(current, delay, _stuckWatch.Tries, maxTries);
                if (redoSequence)
                    current.SetStep(0);
                ExecuteNextStep();
                return true;
            case StuckWatch.EResult.Stop:
                _stopPolicy.ReportStuckLimit(current.Quest, maxTries);
                _reports.Problem(EProblemKind.Stuck);
                _stuckWatch.Reset();
                if (TryDailyFailure("Stuck after retries"))
                    return true;
                Stop("Stuck after retries");
                return true;
            default:
                return false;
        }
    }

    private bool IsStuckPaused() =>
        HasExternalOperation || _recovery.IsRecovering || DateTime.Now < _safeAnimationEnd ||
        _condition[ConditionFlag.InCombat] || _condition[ConditionFlag.Unconscious] ||
        _condition[ConditionFlag.BoundByDuty] || _condition[ConditionFlag.BoundByDuty56] ||
        _condition[ConditionFlag.BoundByDuty95] || _condition[ConditionFlag.InDeepDungeon] ||
        _condition[ConditionFlag.WatchingCutscene] || _condition[ConditionFlag.WatchingCutscene78] ||
        _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
        _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] ||
        _condition[ConditionFlag.Crafting] || _condition[ConditionFlag.Gathering] || _condition[ConditionFlag.Fishing] ||
        _gameFunctions.IsOccupied();

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

            if (_dailies != null)
            {
                UpdateDailiesQuest();
                return;
            }

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
            if (AutomationType == EAutomationType.Automatic && _configuration.Stop is { Enabled: true, PriorityListEmpty: true } &&
                QuestSelectionController.IsPastPriorityList(choice, _priorityManager.Quests,
                    x => _priorityManager.IsAcceptOnly(x) && !_questFunctions.IsQuestAccepted(x)))
            {
                _stopPolicy.ReportPriorityListDone();
                StopFinished("Priority list done");
                return;
            }

            if (!TryGetQuestToRun(choice, out QuestProgress? questToRun))
                return;

            if (questToRun == null)
            {
                _manualTasks &= IsRunning;
                if (_manualTasks && AutomationType == EAutomationType.Manual)
                {
                    DebugState = "Manual tasks";
                    return;
                }

                StopWithNoQuest();
                return;
            }

            if (IsWaitingToRun())
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
        EStopPoint? stopPoint = _selection.GetStopPoint(StartedQuest, StopAfterCurrentQuest, SelectionState);
        if (StartedQuest != null && stopPoint == EStopPoint.Configured && TryLoadPreset(StartedQuest.Quest))
        {
            if (!StopAfterCurrentQuest)
            {
                // Left as started, the finished quest can keep the preset's quests from being picked.
                StartedQuest = null;
                return false;
            }

            stopPoint = EStopPoint.AfterCurrentQuest;
        }

        if (StartedQuest != null && stopPoint != null)
        {
            if (stopPoint == EStopPoint.Configured)
            {
                ElementId questId = StartedQuest.Quest.Id;
                _stopPolicy.ReportStopPoint(StartedQuest.Quest);
                StartedQuest = null;
                StopFinished($"Stopping point [{questId}] reached");
                if (_configuration.Stop.RemoveWhenCompleteConditionMet && !_stopPolicy.IsListRun)
                    _stopPolicy.RemoveStopQuest(questId);
                return true;
            }

            if (stopPoint == EStopPoint.AfterCurrentQuest)
            {
                ElementId questId = StartedQuest.Quest.Id;
                _stopPolicy.ReportStopAfterQuest(StartedQuest.Quest);
                StartedQuest = null;
                StopFinished($"Stop after quest [{questId}]");
                return true;
            }
        }

        return false;
    }

    private bool TryLoadPreset(Quest quest)
    {
        Configuration.StopConfiguration stop = _configuration.Stop;
        if (stop.GetPreset(quest.Id) is not { } presetId)
            return false;

        if (!_presets.TryGetById(presetId, out string? name, out List<ElementId>? quests))
        {
            _stopPolicy.ReportPresetMissing(quest);
            return false;
        }

        _priorityManager.Import(quests);
        _stopPolicy.ReportPresetLoaded(quest, name, StopAfterCurrentQuest);
        if (stop.RemoveWhenCompleteConditionMet && !_stopPolicy.IsListRun)
            _stopPolicy.RemoveStopQuest(quest.Id);

        return true;
    }

    private bool TryGetQuestToRun(QuestChoice choice, out QuestProgress? questToRun)
    {
        // A wait left from an earlier journal frame would let the next one switch at once (5290).
        if (choice.Source != EQuestSource.Journal)
            _journalSince = null;

        questToRun = choice.Progress;
        if (questToRun != null)
        {
            _emptySince = null;
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

                // A quest done on accept (guild join 183) empties the journal for a frame before the next appears.
                _emptySince ??= DateTime.Now;
                if (QuestSelectionController.IsSettling(_emptySince.Value, DateTime.Now))
                    return false;

                _emptySince = null;
                _logger.LogInformation("No current quest, resetting data [CQI: {CurrrentQuestData}], [CQ: {QuestData}], [MSQ: {MsqData}]", _questFunctions.GetCurrentQuestInternal(allowNewMsq: true), _questFunctions.GetCurrentQuest(), _questFunctions.GetMainScenarioQuest());
                StartedQuest = null;
                Stop("Resetting current quest");
            }

            return true;
        }

        _emptySince = null;
        if (StartedQuest == null || StartedQuest.Quest.Id != currentQuestId)
        {
            if (ShouldSettle(choice))
                return false;

            SwitchToQuest(currentQuestId, choice.Sequence);
            return false;
        }

        _journalSince = null;
        questToRun = StartedQuest;
        return true;
    }

    // The next priority quest unlocks a frame after a turn-in, so the journal's quest would win that frame (2012 level stop).
    private bool ShouldSettle(QuestChoice choice)
    {
        // The journal can show another quest for under a second and QST switched job for it.
        if (choice.Source != EQuestSource.Journal ||
            (StartedQuest == null && !_priorityManager.Quests.Any(x => !_questFunctions.IsQuestComplete(x.Id))))
        {
            _journalSince = null;
            return false;
        }

        // The second can run out during the turn-in cutscene, before the next quest can unlock.
        if (_gameFunctions.IsOccupied())
        {
            _journalSince = null;
            DebugState = "Waiting for the next priority quest";
            return true;
        }

        _journalSince ??= DateTime.Now;
        if (QuestSelectionController.IsSettling(_journalSince.Value, DateTime.Now))
        {
            DebugState = "Waiting for the next priority quest";
            return true;
        }

        _journalSince = null;
        return false;
    }

    private void SwitchToQuest(ElementId questId, byte sequence)
    {
        if (_questRegistry.TryGetQuest(questId, out Quest? quest))
        {
            SelectStartedQuest(quest, sequence);
            if (_configuration.Advanced.Debug && _configuration.Advanced.OpenEditor &&
                _reports.StatusFor(quest).NeedsAttention)
            {
                (bool success, string msg) = QuestRegistry.OpenEditor(StartedQuest.Quest.Info);
                _logger.LogDebug("OpenEditor {Success}: {Msg}", success, msg);
            }

            if (AutomationType == EAutomationType.Manual)
                return;

            unsafe
            {
                int playerLevel = SwitchClassJob.AcceptedJob(quest.Id, quest.Info.ClassJobs, quest.Info.Level) is { } acceptedJob
                    ? _questFunctions.GetClassJobLevel(acceptedJob)
                    : _questFunctions.IsQuestAccepted(quest.Id)
                        ? PlayerState.Instance()->CurrentLevel
                        : QuestSelectionController.BestLevel(PlayerState.Instance()->CurrentLevel, quest.Info.ClassJobs,
                            _classJobUtils.GetJobGearSets(combatOnly: false));
                if (QuestSelectionController.IsLevelTooHigh(playerLevel, quest))
                {
                    _stopPolicy.ReportLevelTooLow(quest.Info, playerLevel);
                    StopFinished("Quest level too high");
                }
                else
                {
                    bool finished = false;
                    if (AutomationType == EAutomationType.SingleQuestB)
                    {
                        if (NextInChain?.Invoke() is { } next)
                        {
                            _logger.LogInformation("Event quest finished, continuing with {QuestId}", next.Id);
                            SetNextQuest(next);
                            AutomationType = EAutomationType.SingleQuestA;
                        }
                        else
                        {
                            _logger.LogInformation("Single quest is finished");
                            NextInChain = null;
                            AutomationType = EAutomationType.Manual;
                            finished = true;
                        }
                    }

                    CheckNextTasks("Different Quest");
                    if (finished)
                        StopReason = FinishedReason;
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
            if (_storyCatchUp?.Enabled == true)
            {
                if (IsRunning)
                    return;
                if (_storyCatchUp.LevelRun(_LF("'{0}' needs level {1} (you're level {2})", lockedMsq.Name, lockedMsq.Level, playerLevel),
                        playerLevel) is { } run)
                {
                    _taskQueue.Enqueue(run);
                    return;
                }
            }

            _stopPolicy.ReportMainScenarioLocked(lockedMsq, playerLevel);
        }
        else if (AutomationType != EAutomationType.Manual && SetAsideQuests.Prune(_questFunctions.GetQuestSequence) > 0)
            _stopPolicy.ReportOnlySetAsideLeft(SetAsideQuests.All.Count);

        StopFinished("No quest active");
    }

    private bool IsWaitingToRun()
    {
        if (_gameFunctions.IsOccupied())
        {
            DebugState = "Occupied";
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
                {
                    _stopPolicy.ReportNoSequence(questToRun);
                    _reports.Problem(EProblemKind.MissingPath);
                }
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
                {
                    _stopPolicy.ReportNoStep(questToRun);
                    _reports.Problem(EProblemKind.MissingPath);
                }
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

            _logger.LogDebug("Increasing step count from {CurrentValue}", current!.Step);
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
        DetachDailies();
        ClearTasksInternal();
        AutomationType = EAutomationType.Manual;
        StartedQuest = null;
        NextQuest = null;
    }

    public void TryStopOnQueuedAccept(ElementId questId)
    {
        if (_acceptance.TryConsumeQueued(questId))
            TryStopOnQuestAccepted(questId);
    }

    public void TryStopOnQuestAccepted(ElementId questId)
    {
        NoteDailyAccepted(questId);
        if (AutomationType == EAutomationType.Manual)
            return;

        if (!_acceptance.ShouldStop(questId, _questFunctions.IsQuestAccepted(questId),
                AutomationType == EAutomationType.Manual, out bool configStop))
            return;

        if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            _stopPolicy.ReportAcceptStop(quest, configStop);

        StartedQuest = null;
        StopFinished(configStop ? $"Accept stopping point [{questId}] reached" : $"Stop after accept [{questId}]");
    }

    public override void Stop(string label) => Stop(label, clearQuest: true);

    public void StopFromUi(string label) => Stop(label, clearQuest: true, policyReason: QuestStopPolicy.UiStopReason);

    internal void StopFinished(string label) => Stop(label, clearQuest: true, finished: true);

    public void StopRequested(string label) => Stop(label, clearQuest: true, requested: true);

    internal void StopForPass(string label) => Stop(label, clearQuest: true, pass: true);

    private void Stop(string label, bool clearQuest, string? policyReason = null, bool finished = false,
        bool requested = false, bool pass = false)
    {
        FinishDailies(false);
        Configuration.EEndAction? endAction = EndActionOnce;
        if (!pass)
        {
            EndActionOnce = null;
            StopAfterCurrentQuest = false;
            StopAfterNextAccept = false;
            StopBeforeTeleport = false;
            StopAfterAttune = false;
        }
        AttuneStopDue = false;
        _recovery.Reset();
        _highlightObject.SetHighlight([]);
        using IDisposable? scope = _logger.BeginScope($"Stop/{label}");
        bool wasRunning = IsRunning || AutomationType != EAutomationType.Manual;
        bool wasAutomatic = AutomationType == EAutomationType.Automatic;
        QuestProgress? stoppedAt = wasRunning && IsQuestRun() && SimulatedQuest == null ? StartedQuest : null;
        ClearTasksInternal();
        EProblemKind? problem = _reports.Stopped(stoppedAt, policyReason ?? label, RecentTasks);
        if (!wasRunning)
            return;

        _logger.LogInformation("Stopping automatic questing: {Label}", label);
        AutomationType = EAutomationType.Manual;
        StopReason = policyReason ?? label;
        StopKind = StopKindName(problem, finished);
        if (clearQuest)
        {
            StartedQuest = null;
            NextQuest = null;
        }
        _lastTaskUpdate = DateTime.Now;

        if (!pass)
            _stopPolicy.Apply(policyReason ?? label, wasAutomatic, finished, endAction, idle: finished && !StartedAnyTask,
                requested: requested);
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

        Stopped?.Invoke(this, new StopEventArgs(StopKind, StopReason));
    }

    public void StopAll(string label) => StopAll(label, requested: false);

    public void StopAll(string label, bool requested)
    {
        bool hadExternalOperation = HasExternalOperation;
        Stop(label, clearQuest: true, requested: requested);
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
    bool IQuestRecoveryHost.PlayerMoved => _configuration.General.StopOnPlayerMove && GameFunctions.IsMovementInputHeld();
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
    void IQuestRecoveryHost.ReportDeathLimit(int deaths)
    {
        _stopPolicy.ReportDeathLimit(deaths);
        _reports.Problem(EProblemKind.Deaths);
    }

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

    // Only on a player start: right after the last step the game is still moving the quest on.
    internal void RedoFinishedSequence()
    {
        lock (_progressLock)
        {
            if (CurrentQuest is { Step: CompletedStep } current && StuckWatch.CanRedoSequence(current.FindSequence()))
            {
                _logger.LogInformation("Sequence {Sequence} of {QuestId} finished without the game moving on (game: {Progress}), redoing it",
                    current.Sequence, current.Quest.Id, _questFunctions?.GetQuestProgressInfo(current.Quest.Id)?.ToString() ?? "not accepted");
                current.SetStep(0);
            }
        }
    }

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
            // The job gear shop and the mailbox are open (and the player occupied) while they're used.
            _taskQueue.CurrentTaskExecutor is not JobGear.TakeExecutor &&
            _taskQueue.CurrentTaskExecutor is not Mail.TakeExecutor &&
            _taskQueue.CurrentTaskExecutor is not Repair.TakeExecutor)
            return;

        if (AttuneStopDue)
        {
            _movementController.Stop();
            StopFinished("Stop after attune");
            return;
        }

        if (StopBeforeTeleport)
        {
            _taskQueue.TryPeek(out ITask? nextTask);
            if (TeleportTaskDetector.GetUpcoming(_taskQueue.CurrentTaskExecutor, nextTask, _clientState.TerritoryType) is { } teleport)
            {
                _stopPolicy.ReportStopBeforeTeleport(teleport);
                _movementController.Stop();
                StopFinished("Stop before teleport");
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

    protected override bool TryHandleTaskFailure(Exception exception, ITask task) =>
        CanRetryDaily(exception) && TryDailyFailure(exception.Message);

    internal static bool CanRetryDaily(Exception exception) =>
        exception is not NeedsPlayerException &&
        exception is TaskException or MovementController.PathfindingException;

    internal void RecordNavigationFailure(MovementController.PathfindingException failure, string? task = null) =>
        _movementController.RecordFailure(failure, CurrentQuest?.ToString(),
            task ?? _taskQueue.CurrentTaskExecutor?.CurrentTask.ToString());

    protected override void OnTaskComplete(ITask task)
    {
        if (task is WaitAtEnd.QuestCompleted completed)
        {
            if (_dailies != null && !_questFunctions.IsPriorityQuestDone(completed.ElementId))
                return;
            NoteDailyCompleted(completed.ElementId);
            QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(completed.ElementId));
            if (SimulatedQuest == null && IsQuestRun() &&
                _questRegistry.TryGetQuest(completed.ElementId, out Quest? quest))
                _reports.Completed(quest);
            SimulatedQuest = StartedQuest = null;
        }

        // Only a finished attune counts: already-attuned aetherytes get no task, and shards are a different task.
        if (StopAfterAttune && task is Aetheryte.Attune attune)
        {
            _stopPolicy.ReportStopAfterAttune(attune.AetheryteLocation);
            AttuneStopDue = true;
        }
    }

    private bool IsQuestRun() =>
        AutomationType is EAutomationType.Automatic or EAutomationType.SingleQuestA or EAutomationType.SingleQuestB;

    protected override void OnNextStep(ILastTask task) => IncreaseStepCount(task.ElementId, task.Sequence, shouldContinue: true);

    protected override void OnRetryStep()
    {
        if (CurrentQuest == null)
        {
            _logger.LogWarning("OnRetryStep: no current quest, cannot retry");
            return;
        }

        _logger.LogInformation("Retrying current step [{QuestId}, {Sequence}, {Step}]",
                    CurrentQuest.Quest.Id, CurrentQuest.Sequence, CurrentQuest.Step);
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

        if (_dailies != null && !ChooseDaily())
            return;

        if (_dailies == null && AutomationType == EAutomationType.Automatic && TryQueueAcceptOnly())
            _logger.LogInformation("Accepting queued accept-only quests before other work");

        if (_dailies == null && TryPickPriorityQuest())
            _logger.LogInformation("Using priority quest over current quest");

        (QuestSequence? seq, QuestStep? step, bool createTasks) = GetNextStep();
        if (CurrentQuest == null || seq == null)
        {
            if (EzThrottler.Throttle("NoNextStep", 10_000))
                _logger.LogWarning(
                    "Could not retrieve next quest step, not doing anything [{QuestId}, {Sequence}, {Step}]",
                    CurrentQuest?.Quest.Id, CurrentQuest?.Sequence, CurrentQuest?.Step);

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
            _reports.Problem(EProblemKind.Other);
            if (CanRetryDaily(e) && TryDailyFailure(e.Message))
                return;
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

    // The last step of a sequence moves on with the game; skipping it would only leave QST waiting.
    internal static bool CanSkipStep(QuestProgress? current) =>
        current != null && current.FindStep() != current.FindSequence()?.Steps.LastOrDefault();

    public bool CanSkipStep() => CanSkipStep(CurrentQuest);

    public bool SkipStep()
    {
        if (CurrentQuest is not { } current || !CanSkipStep(current))
            return false;

        if (!HasExternalOperation)
            _movementController.Stop();
        Skip(current.Quest.Id, current.Sequence);
        return true;
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
                    SkipToNextStep(elementId, sequence, "Skip");
            }
            else
                SkipToNextStep(elementId, sequence, "SkipNx");
        }
    }

    internal static bool ContinuesAfterSkip(EAutomationType automation, int? stepBefore, int? stepAfter) =>
        automation != EAutomationType.Manual && stepAfter != null && stepAfter != stepBefore;

    private void SkipToNextStep(ElementId elementId, byte sequence, string label)
    {
        QuestProgress? current = CurrentQuest;
        int? before = current?.Step;
        bool running = AutomationType != EAutomationType.Manual;
        if (running)
            IncreaseStepCount(elementId, sequence);

        if (!ContinuesAfterSkip(AutomationType, before, current?.Step))
        {
            Stop(label, clearQuest: false);
            if (!running)
                IncreaseStepCount(elementId, sequence);
            return;
        }

        ExecuteNextStep();
    }

    public void SkipSimulatedTask()
    {
        if (HasExternalOperation)
            Stop("Skip simulated task during external operation");
        else
            CancelCurrentTask();
    }

    private QuestSlots Slots => new(StartedQuest, NextQuest, SimulatedQuest);
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
        public QuestReference GetCurrentQuest(bool allowNewMsq, bool skipPriority = false) =>
            controller._questFunctions.GetCurrentQuest(allowNewMsq, skipPriority);
    }

    private bool TryQueueAcceptOnly()
    {
        if (_dailies != null)
            return false;
        if (!_priorityManager.HasPendingAcceptOnly)
            return false;

        foreach (Quest quest in _priorityManager.AcceptOnlyQuests.Where(x => !_questFunctions.CanQueueSociety(x.Id)).ToList())
        {
            _logger.LogInformation("Accept-all: {QuestId} has a disabled path or a step that needs the player, dropping it", quest.Id);
            _priorityManager.Remove(quest);
        }

        Quest? next = QuestSelectionController.PickAcceptOnly(_priorityManager.AcceptOnlyQuests, SelectionState,
            _clientState.TerritoryType);
        if (next == null)
        {
            foreach (Quest quest in _priorityManager.AcceptOnlyQuests.Where(x => _questFunctions.IsOutOfSocietyAllowances(x.Id)).ToList())
            {
                _logger.LogInformation("Accept-all: no society allowances left, dropping {QuestId}", quest.Id);
                _priorityManager.Remove(quest);
            }
            return false;
        }

        // SetNextQuest resets Step to 0, which would restart the accept every step boundary forever.
        if (NextQuest?.Quest.Id == next.Id)
            return true;

        if (!_priorityManager.CanInterrupt && QuestSelectionController.IsOtherQuestRunning(StartedQuest, next.Id,
                SelectionState, () => SelectionState.GetCurrentQuest(allowNewMsq: true, skipPriority: true).CurrentQuest))
            return false;

        _logger.LogInformation("Accept-all: queueing {QuestId} to be accepted before other work", next.Id);
        SetNextQuest(next);
        return true;
    }

    private bool TryPickPriorityQuest()
    {
        if (_dailies != null)
            return false;
        if (!_priorityManager.CanInterrupt || !QuestSelectionController.IsInterruptible(CurrentQuestDetails, AutomationType, HasExternalOperation,
                _priorityManager.Contains))
            return false;

        QuestStep? teleportStep = CurrentQuestDetails?.Progress.FindStep();
        ElementId? questId = QuestSelectionController.PickPriorityQuest(Slots,
            StopAfterCurrentQuest || StopAfterNextAccept || StopBeforeTeleport || StopAfterAttune,
            () =>
            {
                List<ElementId> classQuests = _questFunctions.GetPriorityQuests(onlyClassAndRoleQuests: true);
                return _questFunctions.NextPriorityQuests.Where(x =>
                    !SetAsideQuests.Contains(x.QuestId, _questFunctions.GetQuestSequence(x.QuestId)) &&
                    (!classQuests.Contains(x.QuestId) || _questFunctions.IsFreeClassQuestDetour(x.QuestId, teleportStep)));
            });
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

    internal void UnsafeEnqueueManualTask(ITask task)
    {
        _manualTasks = true;
        _taskQueue.Enqueue(task);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;
        DetachDailies();
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
