using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.ConfigComponents;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller;

internal sealed class QuestController : MiniTaskController<QuestController>
{
    private readonly IClientState _clientState;
    private readonly GameFunctions _gameFunctions;
    private readonly QuestFunctions _questFunctions;
    private readonly MovementController _movementController;
    private readonly CombatController _combatController;
    private readonly GatheringController _gatheringController;
    private readonly QuestRegistry _questRegistry;
    private readonly IKeyState _keyState;
    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly IToastGui _toastGui;
    private readonly Configuration _configuration;
    private readonly TaskCreator _taskCreator;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent;
    private readonly ILogger<QuestController> _logger;

    private readonly object _progressLock = new();

    private QuestProgress? _startedQuest;
    private QuestProgress? _nextQuest;
    private QuestProgress? _simulatedQuest;
    private QuestProgress? _gatheringQuest;
    private QuestProgress? _pendingQuest;
    private EAutomationType _automationType;

    /// <summary>
    /// Some combat encounters finish relatively early (i.e. they're done as part of progressing the quest, but not
    /// technically necessary to progress the quest if we'd just run away and back). We add some slight delay, as
    /// talking to NPCs, teleporting etc. won't successfully execute.
    /// </summary>
    private DateTime _safeAnimationEnd = DateTime.MinValue;

    /// <summary>
    ///
    /// </summary>
    private DateTime _lastTaskUpdate = DateTime.Now;

    public QuestController(
        IClientState clientState,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        MovementController movementController,
        CombatController combatController,
        GatheringController gatheringController,
        ILogger<QuestController> logger,
        QuestRegistry questRegistry,
        IKeyState keyState,
        IChatGui chatGui,
        ICondition condition,
        IToastGui toastGui,
        Configuration configuration,
        TaskCreator taskCreator,
        IServiceProvider serviceProvider,
        InterruptHandler interruptHandler,
        IDataManager dataManager,
        SinglePlayerDutyConfigComponent singlePlayerDutyConfigComponent)
        : base(chatGui, condition, serviceProvider, interruptHandler, dataManager, logger)
    {
        _clientState = clientState;
        _gameFunctions = gameFunctions;
        _questFunctions = questFunctions;
        _movementController = movementController;
        _combatController = combatController;
        _gatheringController = gatheringController;
        _questRegistry = questRegistry;
        _keyState = keyState;
        _chatGui = chatGui;
        _condition = condition;
        _toastGui = toastGui;
        _configuration = configuration;
        _taskCreator = taskCreator;
        _singlePlayerDutyConfigComponent = singlePlayerDutyConfigComponent;
        _logger = logger;

        _condition.ConditionChange += OnConditionChange;
        _toastGui.Toast += OnNormalToast;
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

    public delegate void AutomationTypeChangedEventHandler(object sender, EAutomationType e);
    public event AutomationTypeChangedEventHandler? AutomationTypeChanged;

    public (QuestProgress Progress, ECurrentQuestType Type)? CurrentQuestDetails
    {
        get
        {
            if (_simulatedQuest != null)
                return (_simulatedQuest, ECurrentQuestType.Simulated);
            else if (_nextQuest != null && _questFunctions.IsReadyToAcceptQuest(_nextQuest.Quest.Id))
                return (_nextQuest, ECurrentQuestType.Next);
            else if (_gatheringQuest != null)
                return (_gatheringQuest, ECurrentQuestType.Gathering);
            else if (_startedQuest != null)
                return (_startedQuest, ECurrentQuestType.Normal);
            else
                return null;
        }
    }

    public QuestProgress? CurrentQuest => CurrentQuestDetails?.Progress;

    public QuestProgress? StartedQuest => _startedQuest;
    public QuestProgress? SimulatedQuest => _simulatedQuest;
    public QuestProgress? NextQuest => _nextQuest;
    public QuestProgress? GatheringQuest => _gatheringQuest;

    /// <summary>
    /// Used when accepting leves, as there's a small delay
    /// </summary>
    public QuestProgress? PendingQuest => _pendingQuest;

    public List<Quest> ManualPriorityQuests { get; } = [];

    public string? DebugState { get; private set; }

    public bool IsQuestWindowOpen => IsQuestWindowOpenFunction?.Invoke() ?? true;
    public Func<bool>? IsQuestWindowOpenFunction { private get; set; } = () => true;

    public void Reload()
    {
        lock (_progressLock)
        {
            _logger.LogInformation("Reload, resetting curent quest progress");

            ResetInternalState();

            _questRegistry.Reload();
            _singlePlayerDutyConfigComponent.Reload();
        }
    }

    private void ResetInternalState()
    {
        _startedQuest = null;
        _nextQuest = null;
        _gatheringQuest = null;
        _pendingQuest = null;
        _simulatedQuest = null;
        _safeAnimationEnd = DateTime.MinValue;

        DebugState = null;
    }

    public void Update()
    {
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
                    _safeAnimationEnd = DateTime.Now.AddSeconds(1 + animationLock);
            }
        }

        if (AutomationType == EAutomationType.Manual && !IsRunning && !IsQuestWindowOpen)
            return;

        UpdateCurrentQuest();

        if (!_clientState.IsLoggedIn)
        {
            StopAllDueToConditionFailed("Logged out");
        }
        if (_condition[ConditionFlag.Unconscious])
        {
            if (_condition[ConditionFlag.Unconscious] &&
                _condition[ConditionFlag.SufferingStatusAffliction63] &&
                _clientState.TerritoryType == SinglePlayerDuty.SpecialTerritories.Lahabrea)
            {
                // ignore, we're in the lahabrea fight
            }
            else if (_taskQueue.CurrentTaskExecutor is Duty.WaitAutoDutyExecutor)
            {
                // ignoring death in a dungeon if it is being run by AD
            }
            else if (!_taskQueue.AllTasksComplete)
            {
                StopAllDueToConditionFailed("HP = 0");
            }
        }
        else if (_configuration.General.UseEscToCancelQuesting && _keyState[VirtualKey.ESCAPE])
        {
            if (!_taskQueue.AllTasksComplete)
            {
                StopAllDueToConditionFailed("ESC pressed");
            }
        }

        if (AutomationType == EAutomationType.Automatic &&
            (_taskQueue.AllTasksComplete || _taskQueue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.WaitQuestAccepted)
            && CurrentQuest is { Sequence: 0, Step: 0 } or { Sequence: 0, Step: 255 }
            && DateTime.Now >= CurrentQuest.StepProgress.StartedAt.AddSeconds(15))
        {
            lock (_progressLock)
            {
                _logger.LogWarning("Quest accept apparently didn't work out, resetting progress");
                CurrentQuest.SetStep(0);
            }

            ExecuteNextStep();
            return;
        }

        UpdateCurrentTask();
    }

    private void UpdateCurrentQuest()
    {
        lock (_progressLock)
        {
            DebugState = null;

            if (!_clientState.IsLoggedIn)
            {
                ResetInternalState();
                DebugState = "Not logged in";
                return;
            }

            if (_pendingQuest != null)
            {
                if (!_questFunctions.IsQuestAccepted(_pendingQuest.Quest.Id))
                {
                    DebugState = $"Waiting for Leve {_pendingQuest.Quest.Id}";
                    return;
                }
                else
                {
                    _startedQuest = _pendingQuest;
                    _pendingQuest = null;
                    CheckNextTasks("Pending quest accepted");
                }
            }

            if (_simulatedQuest == null && _nextQuest != null)
            {
                // if the quest is accepted, we no longer track it
                bool canUseNextQuest;
                if (_nextQuest.Quest.Info.IsRepeatable)
                    canUseNextQuest = !_questFunctions.IsQuestAccepted(_nextQuest.Quest.Id);
                else
                    canUseNextQuest = !_questFunctions.IsQuestAcceptedOrComplete(_nextQuest.Quest.Id);

                if (!canUseNextQuest)
                {
                    _logger.LogInformation("Next quest {QuestId} accepted or completed",
                        _nextQuest.Quest.Id);

                    if (AutomationType == EAutomationType.SingleQuestA)
                    {
                        _startedQuest = _nextQuest;
                        AutomationType = EAutomationType.SingleQuestB;
                    }

                    _logger.LogDebug("Started: {StartedQuest}", _startedQuest?.Quest.Id);
                    _nextQuest = null;
                }
            }

            QuestProgress? questToRun;
            byte currentSequence;
            if (_simulatedQuest != null)
            {
                currentSequence = _simulatedQuest.Sequence;
                questToRun = _simulatedQuest;
            }
            else if (_nextQuest != null && _questFunctions.IsReadyToAcceptQuest(_nextQuest.Quest.Id))
            {
                questToRun = _nextQuest;
                currentSequence = _nextQuest.Sequence; // by definition, this should always be 0
                if (_nextQuest.Step == 0 &&
                    _taskQueue.AllTasksComplete &&
                    AutomationType == EAutomationType.Automatic)
                    ExecuteNextStep();
            }
            else if (_gatheringQuest != null)
            {
                questToRun = _gatheringQuest;
                currentSequence = _gatheringQuest.Sequence;
                if (_gatheringQuest.Step == 0 &&
                    _taskQueue.AllTasksComplete &&
                    AutomationType == EAutomationType.Automatic)
                    ExecuteNextStep();
            }
            else
            {
                (ElementId? currentQuestId, currentSequence, MainScenarioQuestState msqState) = _questFunctions.GetCurrentQuest(allowNewMsq: AutomationType != EAutomationType.SingleQuestB);
                (ElementId, byte)? priorityQuestOption =
                    ManualPriorityQuests
                        .Where(x => _questFunctions.IsReadyToAcceptQuest(x.Id) || _questFunctions.IsQuestAccepted(x.Id))
                        .Select(x => (x.Id, _questFunctions.GetQuestProgressInfo(x.Id)?.Sequence ?? 0))
                        .FirstOrDefault();
                if (priorityQuestOption is { Item1: not null } priorityQuest)
                {
                    currentQuestId = priorityQuest.Item1;
                    currentSequence = priorityQuest.Item2;
                }

                if (currentQuestId == null || currentQuestId.Value == 0)
                {
                    if (_startedQuest != null)
                    {
                        if (msqState == MainScenarioQuestState.Unavailable)
                        {
                            _logger.LogWarning("MSQ information not available, doing nothing");
                            return;
                        }
                        else if (msqState == MainScenarioQuestState.LoadingScreen)
                        {
                            _logger.LogWarning("On loading screen, no MSQ - doing nothing");
                            return;
                        }

                        _logger.LogInformation("No current quest, resetting data [CQI: {CurrrentQuestData}], [CQ: {QuestData}], [MSQ: {MsqData}]", _questFunctions.GetCurrentQuestInternal(true), _questFunctions.GetCurrentQuest(), _questFunctions.GetMainScenarioQuest());
                        _startedQuest = null;
                        Stop("Resetting current quest");
                    }

                    questToRun = null;
                }
                else if (_startedQuest == null || _startedQuest.Quest.Id != currentQuestId)
                {
                    if (_configuration.Stop.Enabled &&
                        _startedQuest != null &&
                        _configuration.Stop.QuestsToStopAfter.Contains(_startedQuest.Quest.Id) &&
                        _questFunctions.IsQuestComplete(_startedQuest.Quest.Id))
                    {
                        var questId = _startedQuest.Quest.Id;
                        _logger.LogInformation("Reached stopping point (quest: {QuestId})", questId);
                        _chatGui.Print($"Completed quest '{_startedQuest.Quest.Info.Name}', which is configured as a stopping point.", CommandHandler.MessageTag, CommandHandler.TagColor);
                        _startedQuest = null;
                        Stop($"Stopping point [{questId}] reached");
                    }
                    else if (_questRegistry.TryGetQuest(currentQuestId, out var quest))
                    {
                        _logger.LogInformation("New quest: {QuestName}", quest.Info.Name);
                        _startedQuest = new QuestProgress(quest, currentSequence);

                        if (_clientState.LocalPlayer != null &&
                            _clientState.LocalPlayer.Level < quest.Info.Level)
                        {
                            _logger.LogInformation(
                                "Stopping automation, player level ({PlayerLevel}) < quest level ({QuestLevel}",
                                _clientState.LocalPlayer!.Level, quest.Info.Level);
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
                    else if (_startedQuest != null)
                    {
                        _logger.LogInformation("No active quest anymore? Not sure what happened...");
                        _startedQuest = null;
                        Stop("No active Quest");
                    }

                    return;
                }
                else
                    questToRun = _startedQuest;
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
                questToRun.SetSequence(currentSequence);
                CheckNextTasks(
                    $"New sequence {questToRun == _startedQuest}/{_questFunctions.GetCurrentQuestInternal(true)}");
            }

            var q = questToRun.Quest;
            var sequence = q.FindSequence(questToRun.Sequence);
            if (sequence == null)
            {
                DebugState = $"Sequence {questToRun.Sequence} not found";
                Stop("Unknown sequence");
                return;
            }

            if (questToRun.Step == 255)
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

    public (QuestSequence? Sequence, QuestStep? Step, bool createTasks) GetNextStep()
    {
        if (CurrentQuest == null)
            return (null, null, false);

        var q = CurrentQuest.Quest;
        var seq = q.FindSequence(CurrentQuest.Sequence);
        if (seq == null)
            return (null, null, true);

        if (seq.Steps.Count == 0)
            return (seq, null, true);

        if (CurrentQuest.Step >= seq.Steps.Count)
            return (null, null, false);

        return (seq, seq.Steps[CurrentQuest.Step], true);
    }

    public void IncreaseStepCount(ElementId? questId, int? sequence, bool shouldContinue = false)
    {
        lock (_progressLock)
        {
            (QuestSequence? seq, QuestStep? step, _) = GetNextStep();
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
            }

            _logger.LogInformation("Increasing step count from {CurrentValue}", CurrentQuest.Step);
            if (CurrentQuest.Step + 1 < seq.Steps.Count)
                CurrentQuest.SetStep(CurrentQuest.Step + 1);
            else
                CurrentQuest.SetStep(255);
        }

        using var scope = _logger.BeginScope("IncStepCt");
        if (shouldContinue && AutomationType != EAutomationType.Manual)
            ExecuteNextStep();
    }

    private void ClearTasksInternal()
    {
        //_logger.LogDebug("Clearing task (internally)");
        if (_taskQueue.CurrentTaskExecutor is IStoppableTaskExecutor stoppableTaskExecutor)
            stoppableTaskExecutor.StopNow();

        _taskQueue.Reset();

        _combatController.Stop("ClearTasksInternal");
        _gatheringController.Stop("ClearTasksInternal");
    }

    public override void Stop(string label)
    {
        using var scope = _logger.BeginScope($"Stop/{label}");
        if (IsRunning || AutomationType != EAutomationType.Manual)
        {
            ClearTasksInternal();
            _logger.LogInformation("Stopping automatic questing");
            AutomationType = EAutomationType.Manual;
            _nextQuest = null;
            _gatheringQuest = null;
            _lastTaskUpdate = DateTime.Now;
        }
    }

    private void StopAllDueToConditionFailed(string label)
    {
        Stop(label);
        _movementController.Stop();
        _combatController.Stop(label);
        _gatheringController.Stop(label);
    }

    private void CheckNextTasks(string label)
    {
        if (AutomationType is EAutomationType.Automatic or EAutomationType.SingleQuestA or EAutomationType.SingleQuestB)
        {
            using var scope = _logger.BeginScope(label);

            ClearTasksInternal();

            if (CurrentQuest?.Step is >= 0 and < 255)
                ExecuteNextStep();
            else
                _logger.LogInformation("Couldn't execute next step during Stop() call");

            _lastTaskUpdate = DateTime.Now;
        }
        else
            Stop(label);
    }

    public void SimulateQuest(Quest? quest, byte sequence, int step)
    {
        _logger.LogInformation("SimulateQuest: {QuestId}", quest?.Id);
        if (quest != null)
            _simulatedQuest = new QuestProgress(quest, sequence, step);
        else
            _simulatedQuest = null;
    }

    public void SetNextQuest(Quest? quest)
    {
        _logger.LogInformation("NextQuest: {QuestId}", quest?.Id);
        if (quest != null)
            _nextQuest = new QuestProgress(quest);
        else
            _nextQuest = null;
    }

    public void SetGatheringQuest(Quest? quest)
    {
        _logger.LogInformation("GatheringQuest: {QuestId}", quest?.Id);
        if (quest != null)
            _gatheringQuest = new QuestProgress(quest);
        else
            _gatheringQuest = null;
    }

    public void SetPendingQuest(QuestProgress? quest)
    {
        _logger.LogInformation("PendingQuest: {QuestId}", quest?.Quest.Id);
        _pendingQuest = quest;
    }

    protected override void UpdateCurrentTask()
    {
        if (_gameFunctions.IsOccupied() && !_gameFunctions.IsOccupiedWithCustomDeliveryNpc(CurrentQuest?.Quest))
            return;

        base.UpdateCurrentTask();
    }

    protected override void OnTaskComplete(ITask task)
    {
        if (task is WaitAtEnd.WaitQuestCompleted)
            _simulatedQuest = null;
    }

    protected override void OnNextStep(ILastTask task)
    {
        IncreaseStepCount(task.ElementId, task.Sequence, true);
    }

    public void Start(string label)
    {
        using var scope = _logger.BeginScope($"Q/{label}");
        AutomationType = EAutomationType.Automatic;
        ExecuteNextStep();
    }

    public void StartGatheringQuest(string label)
    {
        using var scope = _logger.BeginScope($"GQ/{label}");
        AutomationType = EAutomationType.GatheringOnly;
        ExecuteNextStep();
    }

    public void StartSingleQuest(string label)
    {
        using var scope = _logger.BeginScope($"SQ/{label}");
        AutomationType = EAutomationType.SingleQuestA;
        ExecuteNextStep();
    }

    public void StartSingleStep(string label)
    {
        using var scope = _logger.BeginScope($"SS/{label}");
        AutomationType = EAutomationType.Manual;
        ExecuteNextStep();
    }

    private void ExecuteNextStep()
    {
        ClearTasksInternal();

        if (TryPickPriorityQuest())
            _logger.LogInformation("Using priority quest over current quest");

        (QuestSequence? seq, QuestStep? step, bool createTasks) = GetNextStep();
        if (CurrentQuest == null || seq == null)
        {
            if (CurrentQuestDetails?.Progress.Quest.Id is SatisfactionSupplyNpcId &&
                CurrentQuestDetails?.Progress.Sequence == 1 &&
                CurrentQuestDetails?.Progress.Step == 255 &&
                CurrentQuestDetails?.Type == ECurrentQuestType.Gathering)
            {
                _logger.LogInformation("Completed delivery quest");
                SetGatheringQuest(null);
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
            foreach (var task in _taskCreator.CreateTasks(CurrentQuest.Quest, CurrentQuest.Sequence, seq, step))
                _taskQueue.Enqueue(task);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create tasks");
            _chatGui.PrintError("Failed to start next task sequence, please check /xllog for details.", CommandHandler.MessageTag, CommandHandler.TagColor);
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
        else
        {
            task = null;
            return false;
        }
    }

    public bool HasCurrentTaskMatching<T>([NotNullWhen(true)] out T? task)
        where T : class, ITask
    {
        if (_taskQueue.CurrentTaskExecutor?.CurrentTask is T t)
        {
            task = t;
            return true;
        }
        else
        {
            task = null;
            return false;
        }
    }

    public bool IsRunning => !_taskQueue.AllTasksComplete;
    public TaskQueue TaskQueue => _taskQueue;

    public string? CurrentTaskState
    {
        get
        {
            if (_taskQueue.CurrentTaskExecutor is IDebugStateProvider debugStateProvider)
                return debugStateProvider.GetDebugState();
            else
                return null;
        }
    }

    public sealed class QuestProgress
    {
        public Quest Quest { get; }
        public byte Sequence { get; private set; }
        public int Step { get; private set; }
        public StepProgress StepProgress { get; private set; } = new(DateTime.Now);

        public QuestProgress(Quest quest, byte sequence = 0, int step = 0)
        {
            Quest = quest;
            SetSequence(sequence, step);
        }

        public void SetSequence(byte sequence, int step = 0)
        {
            Sequence = sequence;
            SetStep(step);
        }

        public void SetStep(int step)
        {
            Step = step;
            StepProgress = new StepProgress(DateTime.Now);
        }

        public void IncreasePointMenuCounter()
        {
            StepProgress = StepProgress with
            {
                PointMenuCounter = StepProgress.PointMenuCounter + 1,
            };
        }
    }

    public void Skip(ElementId elementId, byte currentQuestSequence)
    {
        lock (_progressLock)
        {
            if (_taskQueue.CurrentTaskExecutor?.CurrentTask is ISkippableTask)
                _taskQueue.CurrentTaskExecutor = null;
            else if (_taskQueue.CurrentTaskExecutor != null)
            {
                _taskQueue.CurrentTaskExecutor = null;
                while (_taskQueue.TryPeek(out ITask? task))
                {
                    _taskQueue.TryDequeue(out _);
                    if (task is ISkippableTask)
                        return;
                }

                if (_taskQueue.AllTasksComplete)
                {
                    Stop("Skip");
                    IncreaseStepCount(elementId, currentQuestSequence);
                }
            }
            else
            {
                Stop("SkipNx");
                IncreaseStepCount(elementId, currentQuestSequence);
            }
        }
    }

    public void SkipSimulatedTask()
    {
        _taskQueue.CurrentTaskExecutor = null;
    }

    public bool IsInterruptible()
    {
        if (AutomationType is EAutomationType.SingleQuestA or EAutomationType.SingleQuestB)
            return false;

        var details = CurrentQuestDetails;
        if (details == null)
            return false;

        var (currentQuest, type) = details.Value;
        if (type != ECurrentQuestType.Normal || !currentQuest.Quest.Root.Interruptible || currentQuest.Sequence == 0)
            return false;

        if (ManualPriorityQuests.Contains(currentQuest.Quest))
            return false;

        // "ifrit bleeds, we can kill it" isn't listed as priority quest, as we accept it during the MSQ 'Moving On'
        // the rest are priority quests, but that's fine here
        if (QuestData.HardModePrimals.Contains(currentQuest.Quest.Id))
            return false;

        if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            return false;

        QuestSequence? currentSequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
        if (currentQuest.Step > 0)
            return false;

        QuestStep? currentStep = currentSequence?.FindStep(currentQuest.Step);
        return currentStep?.AetheryteShortcut != null &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsCompleted.Count ?? 0) == 0 &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsAccepted.Count ?? 0) == 0;
    }

    public bool TryPickPriorityQuest()
    {
        if (!IsInterruptible() || _nextQuest != null || _gatheringQuest != null || _simulatedQuest != null)
            return false;

        ElementId? priorityQuestId = _questFunctions.GetNextPriorityQuestsThatCanBeAccepted()
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (priorityQuestId == null)
            return false;

        // don't start a second priority quest until the first one is resolved
        if (_startedQuest != null && priorityQuestId == _startedQuest.Quest.Id)
            return false;

        if (_questRegistry.TryGetQuest(priorityQuestId, out var quest))
        {
            SetNextQuest(quest);
            return true;
        }

        return false;
    }

    public void ImportQuestPriority(List<ElementId> questElements)
    {
        foreach (ElementId elementId in questElements)
        {
            if (_questRegistry.TryGetQuest(elementId, out Quest? quest) && !ManualPriorityQuests.Contains(quest))
                ManualPriorityQuests.Add(quest);
        }
    }

    private const char ClipboardSeparator = ';';
    public string ExportQuestPriority()
    {
        return string.Join(ClipboardSeparator, ManualPriorityQuests.Select(x => x.Id.ToString()));
    }

    public void ClearQuestPriority()
    {
        ManualPriorityQuests.Clear();
    }

    public bool AddQuestPriority(ElementId elementId)
    {
        if (_questRegistry.TryGetQuest(elementId, out Quest? quest) && !ManualPriorityQuests.Contains(quest))
            ManualPriorityQuests.Add(quest);
        return true;
    }

    public bool InsertQuestPriority(int index, ElementId elementId)
    {
        try
        {
            if (_questRegistry.TryGetQuest(elementId, out Quest? quest) && !ManualPriorityQuests.Contains(quest))
                ManualPriorityQuests.Insert(index, quest);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to insert quest in priority list");
            _chatGui.PrintError("Failed to insert quest in priority list, please check /xllog for details.", CommandHandler.MessageTag, CommandHandler.TagColor);
            return false;
        }
    }

    public bool WasLastTaskUpdateWithin(TimeSpan timeSpan)
    {
        _logger.LogInformation("Last update: {Update}", _lastTaskUpdate);
        return IsRunning || DateTime.Now <= _lastTaskUpdate.Add(timeSpan);
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (_taskQueue.CurrentTaskExecutor is IConditionChangeAware conditionChangeAware)
            conditionChangeAware.OnConditionChange(flag, value);
    }

    private void OnNormalToast(ref SeString message, ref ToastOptions options, ref bool isHandled)
    {
        _gatheringController.OnNormalToast(message);
    }

    protected override void HandleInterruption(object? sender, EventArgs e)
    {
        if (!IsRunning)
            return;

        if (AutomationType == EAutomationType.Manual)
            return;

        base.HandleInterruption(sender, e);
    }

    public override void Dispose()
    {
        _toastGui.ErrorToast -= OnErrorToast;
        _toastGui.Toast -= OnNormalToast;
        _condition.ConditionChange -= OnConditionChange;
        base.Dispose();
    }

    public sealed record StepProgress(
        DateTime StartedAt,
        int PointMenuCounter = 0);

    public enum ECurrentQuestType
    {
        Normal,
        Next,
        Gathering,
        Simulated,
    }

    public enum EAutomationType
    {
        Manual,
        Automatic,
        GatheringOnly,
        SingleQuestA,
        SingleQuestB,
    }
}
