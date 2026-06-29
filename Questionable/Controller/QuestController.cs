using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.ConfigComponents;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller;

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

    /// <summary>FFXIV item-ID offset for high-quality items.</summary>
    private const uint HighQualityItemIdOffset = 1_000_000;

    /// <summary>FFXIV item-ID offset for collectable items.</summary>
    private const uint CollectableItemIdOffset = 500_000;

    /// <summary>How long to wait at sequence 0/step 0 before assuming the quest-accept addon never fired and resetting.</summary>
    private const int QuestAcceptStalledTimeoutSeconds = 15;

    /// <summary>Minimum cooldown between auto-refresh attempts.</summary>
    private const int AutoRefreshCooldownSeconds = 5;

    /// <summary>Player must move at least this many world-units for the auto-refresh "progress" check to fire.</summary>
    private const float AutoRefreshProgressMoveThreshold = 0.5f;

    /// <summary>Consecutive deaths on the same quest step before death handling gives up and stops.</summary>
    private const int MaxConsecutiveDeaths = 5;

    /// <summary>Seconds to wait after returning from a death before retrying the current step.</summary>
    private const int DeathRecoveryGraceSeconds = 3;

    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuestFunctions;
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly CombatController _combatController;
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly GameFunctions _gameFunctions;
    private readonly IGameGuiAdapter _gameGui;
    private readonly GatheringController _gatheringController;
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
    private readonly TaskCreator _taskCreator;
    private readonly IToastGui _toastGui;
    private readonly ICommandManager _commandManager;
    private EAutomationType _automationType;
    private DateTime _lastAutoRefresh = DateTime.MinValue;

    /// <summary>True while recovering from the player's death (waiting for the return prompt / respawn).</summary>
    private bool _handlingDeath;

    /// <summary>When the player regained consciousness after a death; used to let the zone settle.</summary>
    private DateTime _respawnedAt = DateTime.MinValue;

    /// <summary>Throttle for re-confirming the return-to-respawn prompt while the player is dead.</summary>
    private DateTime _lastReturnConfirmAt = DateTime.MinValue;

    /// <summary>Quest step a death streak is counted against; the streak resets when the step changes.</summary>
    private (ElementId QuestId, byte Sequence, int Step)? _deathStreakKey;
    private int _deathStreakCount;

    /// <summary>
    ///     Auto-refresh fields for tracking player state and progress
    /// </summary>
    private Vector3 _lastPlayerPosition = Vector3.Zero;
    private DateTime _lastProgressUpdate = DateTime.Now;
    private ElementId? _lastQuestId;
    private byte _lastQuestSequence = 255;
    private int _lastQuestStep = -1;

    /// <summary>
    /// </summary>
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
        _alliedSocietyQuestFunctions = alliedSocietyQuestFunctions;
        _logger = logger;
        _highlightObject = highlightObject;
        _commandManager = commandManager;

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
        private set => _tracker.NextQuest = value;
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

    public event AutomationTypeChangedEventHandler? AutomationTypeChanged;

    public void Reload()
    {
        lock (_progressLock)
        {
            _logger.LogInformation("Reload, resetting curent quest progress");

            ResetInternalState();
            ResetAutoRefreshState();

            _questRegistry.Reload();
            _singlePlayerDutyConfigComponent.Reload();
            _alliedSocietyQuestFunctions.Reload();
        }
    }

    private void ResetInternalState()
    {
        _tracker.Reset();
        _safeAnimationEnd = DateTime.MinValue;

        DebugState = null;
    }

    private void ResetAutoRefreshState()
    {
        _lastPlayerPosition = Vector3.Zero;
        _lastQuestStep = -1;
        _lastQuestSequence = 255;
        _lastQuestId = null;
        _lastProgressUpdate = DateTime.Now;
        _lastAutoRefresh = DateTime.Now;
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
                    _safeAnimationEnd = DateTime.Now.AddSeconds(AnimationLockGraceSeconds + animationLock);
            }
        }

        if (AutomationType == EAutomationType.Manual && !IsRunning && !IsQuestWindowOpen)
            return;

        UpdateCurrentQuest();

        if (!_clientState.IsLoggedIn)
            StopAllDueToConditionFailed("Logged out");
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
            else if (_taskQueue.CurrentTaskExecutor is SinglePlayerDuty.WaitSinglePlayerDutyExecutor)
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
                    _chatGui.Print($"Reached or exceeded target level {_configuration.Stop.TargetLevel}.", CommandHandler.MessageTag, CommandHandler.TagColor);
                    Stop($"Level stop condition reached [{currentLevel}]");
                    return;
                }
            }
        }

        if (AutomationType == EAutomationType.Automatic &&
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

        //CheckAutoRefreshCondition();

        UpdateCurrentTask();
    }

    private void CheckAutoRefreshCondition()
    {
        if (!_configuration.General.AutoStepRefreshEnabled ||
            AutomationType != EAutomationType.Automatic ||
            !IsRunning ||
            CurrentQuest == null ||
            !_clientState.IsLoggedIn ||
            _objectTable[0] == null ||
            DateTime.Now < _lastAutoRefresh.AddSeconds(AutoRefreshCooldownSeconds))
        {
            return;
        }

        Vector3? playerPosition = _objectTable[0]?.Position;
        if (playerPosition == null ||
            _condition[ConditionFlag.InCombat] ||
            _condition[ConditionFlag.Unconscious] ||
            _condition[ConditionFlag.BoundByDuty] ||
            _condition[ConditionFlag.InDeepDungeon] ||
            _condition[ConditionFlag.WatchingCutscene] ||
            _condition[ConditionFlag.WatchingCutscene78] ||
            _condition[ConditionFlag.BetweenAreas] ||
            _condition[ConditionFlag.BetweenAreas51] ||
            _gameFunctions.IsOccupied() ||
            _movementController.IsPathfinding ||
            _movementController.IsPathRunning ||
            !_movementController.IsNavmeshReady ||
            (_taskQueue.CurrentTaskExecutor?.CurrentTask.GetType().Namespace == typeof(WaitAtEnd).Namespace) ||
            DateTime.Now < _safeAnimationEnd)
        {
            _lastProgressUpdate = DateTime.Now;
            return;
        }

        Vector3 currentPosition = playerPosition.Value;
        ElementId currentQuestId = CurrentQuest.Quest.Id;
        byte currentSequence = CurrentQuest.Sequence;
        int currentStep = CurrentQuest.Step;

        bool hasProgressBeenMade =
            Vector3.Distance(currentPosition, _lastPlayerPosition) > AutoRefreshProgressMoveThreshold ||
            !currentQuestId.Equals(_lastQuestId) ||
            currentSequence != _lastQuestSequence ||
            currentStep != _lastQuestStep;

        if (hasProgressBeenMade)
        {
            _lastPlayerPosition = currentPosition;
            _lastQuestId = currentQuestId;
            _lastQuestSequence = currentSequence;
            _lastQuestStep = currentStep;
            _lastProgressUpdate = DateTime.Now;
        }
        else
        {
            // we detect no progress, check if we should auto-refresh
            TimeSpan timeSinceProgress = DateTime.Now - _lastProgressUpdate;
            TimeSpan refreshDelay = TimeSpan.FromSeconds(_configuration.General.AutoStepRefreshDelaySeconds);

            if (timeSinceProgress >= refreshDelay)
            {
                _logger.LogInformation("Automatically refreshing quest step as no progress detected for {TimeSinceProgress:F1} seconds (quest: {QuestId}, sequence: {Sequence}, step: {Step})",
                    timeSinceProgress.TotalSeconds, currentQuestId, currentSequence, currentStep);

                _chatGui.Print($"Automatically refreshing quest step as no progress detected for {timeSinceProgress.TotalSeconds:F0} seconds.",
                    CommandHandler.MessageTag, CommandHandler.TagColor);

                ClearTasksInternal();

                Reload();

                _lastAutoRefresh = DateTime.Now;
            }
        }
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

            if (PendingQuest != null)
            {
                if (!_questFunctions.IsQuestAccepted(PendingQuest.Quest.Id))
                {
                    DebugState = $"Waiting for Leve {PendingQuest.Quest.Id}";
                    return;
                }
                else
                {
                    StartedQuest = PendingQuest;
                    PendingQuest = null;
                    TryStopOnQuestAccepted(StartedQuest.Quest.Id);
                    if (AutomationType == EAutomationType.Manual)
                        return;
                    CheckNextTasks("Pending quest accepted");
                }
            }

            if (SimulatedQuest == null && NextQuest != null)
            {
                // if the quest is accepted, we no longer track it
                bool canUseNextQuest;
                if (NextQuest.Quest.Info.IsRepeatable)
                    canUseNextQuest = !_questFunctions.IsQuestAccepted(NextQuest.Quest.Id);
                else
                    canUseNextQuest = !_questFunctions.IsQuestAcceptedOrComplete(NextQuest.Quest.Id);

                if (!canUseNextQuest)
                {
                    ElementId nextQuestId = NextQuest.Quest.Id;
                    _logger.LogInformation("Next quest {QuestId} accepted or completed", nextQuestId);

                    if (AutomationType == EAutomationType.SingleQuestA)
                    {
                        StartedQuest = NextQuest;
                        AutomationType = EAutomationType.SingleQuestB;
                    }

                    _logger.LogDebug("Started: {StartedQuest}", StartedQuest?.Quest.Id);
                    NextQuest = null;
                    TryStopOnQuestAccepted(nextQuestId);
                    if (AutomationType == EAutomationType.Manual)
                        return;
                }
            }

            // Stop checks run before the NextQuest/priority cascade — otherwise a
            // queued NextQuest (e.g. an MSQ chain or priority list pick) is started
            // before the user's "stop after this quest" toggle is ever consulted.
            // This checks for both IsQuestComplete and !IsQuestAccepted because
            // repeatable quests (and new game+ questing) returns true for Complete,
            // but the quest may still be active. This prevents the stop condition
            // being tripped early.
            if (StartedQuest != null && _questFunctions.IsQuestComplete(StartedQuest.Quest.Id) && !_questFunctions.IsQuestAccepted(StartedQuest.Quest.Id))
            {
                if (_configuration.Stop.Enabled &&
                    _configuration.Stop.QuestsToStopAfter.Contains(StartedQuest.Quest.Id))
                {
                    ElementId questId = StartedQuest.Quest.Id;
                    _logger.LogInformation("Reached stopping point (quest: {QuestId})", questId);
                    _chatGui.Print($"Completed quest '{StartedQuest.Quest.Info.Name}', which is configured as a stopping point.", CommandHandler.MessageTag, CommandHandler.TagColor);
                    StartedQuest = null;
                    Stop($"Stopping point [{questId}] reached");
                    return;
                }

                if (StopAfterCurrentQuest)
                {
                    ElementId questId = StartedQuest.Quest.Id;
                    _logger.LogInformation("Stopping after current quest as requested (quest: {QuestId})", questId);
                    _chatGui.Print($"Completed quest '{StartedQuest.Quest.Info.Name}', stopping as requested.", CommandHandler.MessageTag, CommandHandler.TagColor);
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
                (ElementId? currentQuestId, currentSequence, MainScenarioQuestState msqState) = _questFunctions.GetCurrentQuest(allowNewMsq: AutomationType != EAutomationType.SingleQuestB);
                (ElementId, byte)? priorityQuestOption =
                    _priorityManager.Quests
                        .Where(x => _questFunctions.IsReadyToAcceptQuest(x.Id) || _questFunctions.IsQuestAccepted(x.Id))
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
                        else if (msqState == MainScenarioQuestState.LoadingScreen)
                        {
                            _logger.LogWarning("On loading screen, no MSQ - doing nothing");
                            return;
                        }

                        _logger.LogInformation("No current quest, resetting data [CQI: {CurrrentQuestData}], [CQ: {QuestData}], [MSQ: {MsqData}]", _questFunctions.GetCurrentQuestInternal(true), _questFunctions.GetCurrentQuest(), _questFunctions.GetMainScenarioQuest());
                        StartedQuest = null;
                        Stop("Resetting current quest");
                    }

                    questToRun = null;
                }
                else if (StartedQuest == null || StartedQuest.Quest.Id != currentQuestId)
                {
                    if (_questRegistry.TryGetQuest(currentQuestId, out Quest? quest))
                    {
                        _highlightObject.SetHighlight([]);
                        _logger.LogInformation("New quest: {QuestName}", quest.Info.Name);

                        TryStopOnQuestAccepted(quest.Id);

                        StartedQuest = new(quest, currentSequence);
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
                    $"New sequence {questToRun == StartedQuest}/{_questFunctions.GetCurrentQuestInternal(true)}");
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

    public (QuestSequence? Sequence, QuestStep? Step, bool createTasks) GetNextStep() => _tracker.GetNextStep();

    public void IncreaseStepCount(ElementId? questId, int? sequence, bool shouldContinue = false)
    {
        lock (_progressLock)
        {
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
            }

            _logger.LogInformation("Increasing step count from {CurrentValue}", CurrentQuest.Step);
            if (CurrentQuest.Step + 1 < seq.Steps.Count)
                CurrentQuest.SetStep(CurrentQuest.Step + 1);
            else
                CurrentQuest.SetStep(CompletedStepValue);

            ResetAutoRefreshState();
        }

        using IDisposable? scope = _logger.BeginScope("IncStepCt");
        if (shouldContinue && AutomationType != EAutomationType.Manual)
            ExecuteNextStep();
    }

    private void ClearTasksInternal()
    {
        if (_taskQueue.CurrentTaskExecutor is IStoppableTaskExecutor stoppableTaskExecutor)
            stoppableTaskExecutor.StopNow();

        _taskQueue.Reset();

        _combatController.Stop("ClearTasksInternal");
        _gatheringController.Stop("ClearTasksInternal");
    }

    /// <summary>
    ///     Stops automation when a quest is accepted, if configured or requested via
    ///     <see cref="StopAfterAcceptingNextQuest"/>.
    /// </summary>
    public void TryStopOnQuestAccepted(ElementId questId)
    {
        if (AutomationType == EAutomationType.Manual)
            return;

        bool configStop = _configuration.Stop.Enabled &&
                          _configuration.Stop.QuestsToStopWhenAccepted.Any(x => x == questId);
        bool sessionStop = StopAfterAcceptingNextQuest &&
                           (StartedQuest == null || StartedQuest.Quest.Id == questId);

        if (!configStop && !sessionStop)
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

    public override void Stop(string label)
    {
        StopAfterCurrentQuest = false;
        StopAfterAcceptingNextQuest = false;
        StopBeforeTeleport = false;
        _handlingDeath = false;
        _deathStreakKey = null;
        _deathStreakCount = 0;
        _highlightObject.SetHighlight([]);
        using IDisposable? scope = _logger.BeginScope($"Stop/{label}");
        if (IsRunning || AutomationType != EAutomationType.Manual)
        {
            ClearTasksInternal();
            if (_configuration.Stop is { RunCommandAfterStop: true } stop)
            {
                if (stop.CommandAfterStop.StartsWith('/'))
                    _commandManager.ProcessCommand(stop.CommandAfterStop);
            }
            _logger.LogInformation("Stopping automatic questing");
            AutomationType = EAutomationType.Manual;
            NextQuest = null;
            GatheringQuest = null;
            _lastTaskUpdate = DateTime.Now;

            ResetAutoRefreshState();
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
    }

    public void StopAllDueToConditionFailed(string label)
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
            using IDisposable? scope = _logger.BeginScope(label);

            ClearTasksInternal();

            if (CurrentQuest?.Step is >= 0 and < CompletedStepValue)
                ExecuteNextStep();
            else
                _logger.LogInformation("Couldn't execute next step during Stop() call");

            _lastTaskUpdate = DateTime.Now;

            ResetAutoRefreshState();
        }
        else
            Stop(label);
    }

    /// <summary>
    ///     Recovers from the player dying during questing: on the first frame it stops the current
    ///     tasks, then every frame it confirms the return-to-respawn prompt until the player respawns.
    /// </summary>
    private void BeginDeathHandling()
    {
        if (!_handlingDeath)
        {
            _handlingDeath = true;
            _respawnedAt = DateTime.MinValue;
            _lastReturnConfirmAt = DateTime.MinValue;
            _logger.LogWarning("Player died while questing — waiting to return and retry the current step");
            ClearTasksInternal();
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

    public void SimulateQuest(IQuestInfo? questInfo, byte sequence, int step) =>
        _tracker.SimulateQuest(questInfo, sequence, step);

    public void SimulateQuest(Quest? quest, byte sequence, int step) =>
        _tracker.SimulateQuest(quest, sequence, step);

    public void StopSimulate() => _tracker.StopSimulate();

    public void SetNextQuest(Quest? quest) => _tracker.SetNextQuest(quest);

    public void SetGatheringQuest(Quest? quest) => _tracker.SetGatheringQuest(quest);

    public void SetPendingQuest(QuestProgress? quest) => _tracker.SetPendingQuest(quest);

    protected override void UpdateCurrentTask()
    {
        if (_gameFunctions.IsOccupied() && !_gameFunctions.IsOccupiedWithCustomDeliveryNpc(CurrentQuest?.Quest))
            return;

        if (StopBeforeTeleport &&
            _taskQueue.CurrentTaskExecutor == null &&
            _taskQueue.TryPeek(out ITask? nextTask) &&
            TeleportTaskDetector.IsUpcomingTeleport(nextTask, _clientState.TerritoryType))
        {
            _logger.LogInformation("Stopping before teleport as requested (upcoming task: {Task})", nextTask);
            _chatGui.Print("Stopping before teleport as requested.", CommandHandler.MessageTag, CommandHandler.TagColor);
            _movementController.Stop();
            Stop("Stop before teleport");
            return;
        }

        base.UpdateCurrentTask();
    }

    protected override void OnTaskComplete(ITask task)
    {
        if (task is WaitAtEnd.WaitQuestCompleted)
            SimulatedQuest = null;

    }

    protected override void OnNextStep(ILastTask task) => IncreaseStepCount(task.ElementId, task.Sequence, true);

    protected override void OnRetryStep()
    {
        if (CurrentQuest == null)
        {
            _logger.LogWarning("OnRetryStep: no current quest, cannot retry");
            return;
        }

        _logger.LogInformation("Retrying current step for quest {QuestId} (sequence {Sequence}, step {Step})",
            CurrentQuest.Quest.Id, CurrentQuest.Sequence, CurrentQuest.Step);
        CheckNextTasks("RetryStep");
    }

    public void Start(string label)
    {
        using IDisposable? scope = _logger.BeginScope($"Q/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.Automatic;
        ExecuteNextStep();
    }

    public void StartGatheringQuest(string label)
    {
        using IDisposable? scope = _logger.BeginScope($"GQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.GatheringOnly;
        ExecuteNextStep();
    }

    public void StartSingleQuest(string label)
    {
        using IDisposable? scope = _logger.BeginScope($"SQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        AutomationType = EAutomationType.SingleQuestA;
        ExecuteNextStep();
    }

    public void StartSingleStep(string label)
    {
        using IDisposable? scope = _logger.BeginScope($"SS/{label}");
        RedeemRewardItems.ResetAttemptedItems();
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
                CurrentQuestDetails?.Progress.Step == CompletedStepValue &&
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

                _taskQueue.Enqueue(task);
            }

            ResetAutoRefreshState();
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
                    _taskQueue.TryDequeue(out ITask? _);
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

    public void SkipSimulatedTask() => _taskQueue.CurrentTaskExecutor = null;

    public bool IsInterruptible()
    {
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
        if (!IsInterruptible() || NextQuest != null || GatheringQuest != null || SimulatedQuest != null)
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
        _logger.LogInformation("Last update: {Update}", _lastTaskUpdate);
        return IsRunning || DateTime.Now <= _lastTaskUpdate.Add(timeSpan);
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (_taskQueue.CurrentTaskExecutor is IConditionChangeAware conditionChangeAware)
            conditionChangeAware.OnConditionChange(flag, value);
    }

    private void OnNormalToast(ref SeString message, ref ToastOptions options, ref bool isHandled) => _gatheringController.OnNormalToast(message);

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
                var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, null)
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
        else
        {
            _chatGui.PrintError($"No associated quest ({info.QuestId}).", "Questionable");
            return false;
        }
    }

    public override void Dispose()
    {
        _toastGui.ErrorToast -= OnErrorToast;
        _toastGui.Toast -= OnNormalToast;
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
