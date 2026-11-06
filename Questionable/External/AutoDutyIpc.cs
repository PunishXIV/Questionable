using Dalamud.Game.ClientState.Conditions;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutoDutyIpc : Ipc, IDisposable
{
    public delegate void RunForPluginDelegate(string pluginName, uint territoryType, int loops = 0, bool bareMode = false);

    [EzIPC] public Func<uint, bool> ContentHasPath { get; private set; } = null!;
    [EzIPC] public Func<bool> IsStopped { get; private set; } = null!;
    [EzIPC] public Action Stop { get; private set; } = null!;
    [EzIPC] public Func<string, string> GetConfig { get; private set; } = null!;
    [EzIPC] public Action<string, object> SetConfig { get; private set; } = null!;
    [EzIPC] public Action<string, bool> PluginConfig { get; private set; } = null!;
    [EzIPC] public RunForPluginDelegate RunForPlugin { get; private set; } = null!;

    private readonly EzIPCDisposalToken[] _disposalTokens;

    public AutoDutyIpc() => _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);

    public override string InternalName => "AutoDuty";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => GetStopped() != null;

    public bool IsAvailable()
    {
        try
        {
            IsStopped();
            return true;
        }
        catch (Exception e) when (IpcInvoke.IsMissing(e))
        {
            return false;
        }
        catch (Exception)
        {
            // A failed status read does not prove absence; protect possible manual work.
            return true;
        }
    }

    public bool? GetStopped()
    {
        try { return IsStopped(); }
        catch (Exception) { return null; }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}

[RegisterSingleton]
internal sealed class AutoDutyOperationProvider : OwnedOperationProvider, IDisposable
{
    private const string ProfileName = "Questionable";
    private readonly AutoDutyIpc _ipc;
    private readonly Configuration _configuration;
    private readonly TerritoryData _territoryData;
    private readonly ILogger<AutoDutyOperationProvider> _logger;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IDutyState _dutyState;
    private readonly NavmeshIpc _navmesh;
    private readonly IDutyState.DutyCompletedDelegate _dutyCompleted;
    private bool _loggedReleaseFailure;

    public AutoDutyOperationProvider(AutoDutyIpc ipc, Configuration configuration, TerritoryData territoryData,
        ILogger<AutoDutyOperationProvider> logger, AutoDutyOperationState operation, IClientState clientState,
        ICondition condition, IDutyState dutyState, NavmeshIpc navmesh) : base(operation)
    {
        _ipc = ipc;
        _configuration = configuration;
        _territoryData = territoryData;
        _logger = logger;
        Operation = operation;
        _clientState = clientState;
        _condition = condition;
        _dutyState = dutyState;
        _navmesh = navmesh;
        _dutyCompleted = _ =>
        {
            if (_clientState.IsLoggedIn)
                Operation.ObserveDutyCompleted(_clientState.TerritoryType);
        };
        _dutyState.DutyCompleted += _dutyCompleted;
    }

    public AutoDutyOperationState Operation { get; }

    public override int Order => 500;
    public override string Name => "AutoDuty";
    protected override string CancelLabel => "AutoDuty";

    protected override bool IsOwningExecutor(ITaskExecutor? executor) => executor is Duty.StartAutoDutyExecutor;
    protected override void CancelOwned() => CancelRun();
    protected override void ObserveIdle() => PollOperation();

    // AutoDuty owns its death and retry behaviour while it runs our duty.
    public override bool HandlesPlayerDeath(ITaskExecutor? executor) => OwnsExecutor(executor);

    public override bool MayBeMovingCharacter() => _ipc.IsAvailable() && _ipc.GetStopped() != true;

    public override OperationStatusView? GetStatusView()
    {
        if (!HasOperation)
            return null;

        return new(Operation.BlockReason ?? _L("Running a duty with AutoDuty"),
            Operation.RequiresReconciliation
                ? new OperationRecoveryView(_L("I stopped AutoDuty"), Operation.CanConfirmIdle,
                    [_L("Stop AutoDuty and leave its duty or queue before confirming. Movement and temporary settings must be settled. Confirmation does not restart questing.")],
                    Detail: Operation.Message)
                : null);
    }

    public override OperationPill? GetTitleBarPill() => null;

    public override string? GetStartBlockReason() =>
        MayBeMovingCharacter()
            ? _L("AutoDuty is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;

    public bool IsConfiguredToRunContent(DutyOptions? dutyOptions) =>
        dutyOptions is { ContentFinderConditionId: > 0 } &&
        _configuration.Duties.RunInstancedContentWithAutoDuty &&
        !_configuration.Duties.BlacklistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId) &&
        (dutyOptions.Enabled || _configuration.Duties.WhitelistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId)) &&
        HasPath(dutyOptions.ContentFinderConditionId);

    public bool HasPath(uint cfcId)
    {
        var data = FindContent(cfcId);
        return data != null && IpcInvoke.SafeFunc(() => _ipc.ContentHasPath(data.TerritoryId), false,
            _logger, "Unable to query AutoDuty for path in territory {TerritoryType}", data.TerritoryId);
    }

    private TerritoryData.ContentFinderConditionData? FindContent(uint cfcId) =>
        _territoryData.TryGetContentFinderCondition(cfcId, out var data) ? data : null;

    public void StartInstance(uint cfcId, Duty.DutyMode dutyMode, bool pathEnabled = true)
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);
        if (!IsConfiguredToRunContent(new DutyOptions { ContentFinderConditionId = cfcId, Enabled = pathEnabled }))
            throw new TaskException("AutoDuty is disabled or this duty is excluded or unsupported.");
        var data = FindContent(cfcId) ?? throw new TaskException($"Unknown ContentFinderConditionId {cfcId}");
        if (_ipc.GetStopped() != true)
            throw new TaskException("AutoDuty is unavailable or busy; refusing to replace its work.");
        var game = ReadGameState();
        if (!game.Available || !game.Idle || game.InQueue || game.Loading || game.InDuty)
            throw new TaskException("Cannot start AutoDuty while native duty, queue or movement is active.");

        Guid owner = Operation.BeginSubmission(data.TerritoryId);
        _loggedReleaseFailure = false;
        try
        {
            // Before SetConfig, so it writes to our profile instead of the user's.
            _ipc.PluginConfig(ProfileName, true);
        }
        catch (Exception e) when (IpcInvoke.IsMissing(e))
        {
            Operation.RejectSubmission(owner);
            throw new TaskException("Update AutoDuty: this version can't run duties in a separate Questionable profile.", e);
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation("AutoDuty configuration failed; its profile will be switched back while idle");
            throw new TaskException(_LF("Unable to run content with AutoDuty: {0}", e.Message), e);
        }

        try
        {
            ApplySetting("Meta.Unsynced", (dutyMode == Duty.DutyMode.Regular).ToString());
            ApplySetting("Meta.DutyModeEnum", dutyMode.ToString());
            ApplySetting("DutyConfig.AutoExitDuty", "True");
            if (_ipc.GetStopped() != true)
            {
                Operation.RequireReconciliation("AutoDuty became busy during configuration; its work is not owned");
                throw new TaskException("AutoDuty became busy during configuration.");
            }
            Operation.MarkRunSubmitted();
            _ipc.RunForPlugin(ProfileName, data.TerritoryId, 1, true);
            // Run returns nothing; watch for activity instead of retrying it.
            PollOperation();
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation(Operation.RunSubmitted
                ? "AutoDuty submission is uncertain; check its work before restarting"
                : "AutoDuty configuration failed; its profile will be switched back while idle");
            throw new TaskException(_LF("Unable to run content with AutoDuty: {0}", e.Message), e);
        }
    }

    protected override void PollOperation()
    {
        if (!HasOperation)
            return;
        Operation.Observe(_ipc.GetStopped(), ReadGameState());
        if (!Operation.CanReleaseProfile)
            return;
        if (ReleaseProfile())
        {
            Operation.ProfileReleased();
            Operation.FinishIfSettled();
        }
    }

    public void CancelRun()
    {
        if (!HasOperation)
            return;
        PollOperation();
        if (!Operation.BeginStop())
            return;
        try
        {
            // Stop is global, so only send it while our run owns AutoDuty.
            _ipc.Stop();
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation("AutoDuty stop request failed; stopping is not confirmed");
            _logger.LogWarning(e, "Unable to confirm the AutoDuty stop request");
        }
    }

    private AutoDutyGameState GameState() => new(_clientState.IsLoggedIn, _clientState.TerritoryType,
        _condition[ConditionFlag.InDutyQueue],
        _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51],
        _condition[ConditionFlag.BoundByDuty],
        !_condition[ConditionFlag.InCombat] && !_condition[ConditionFlag.Casting] &&
        !_condition[ConditionFlag.Occupied] && !_condition[ConditionFlag.OccupiedInCutSceneEvent] &&
        !_condition[ConditionFlag.WatchingCutscene] && !_condition[ConditionFlag.Unconscious]);

    private AutoDutyGameState ReadGameState()
    {
        try
        {
            var state = GameState();
            // An unknown vnav state counts as moving.
            if (_navmesh.GetPathRunning() is not { } running ||
                _navmesh.GetPathfindInProgress() is not { } pathfinding ||
                _navmesh.GetSimpleMoveInProgress() is not { } simpleMove)
                return default;
            return state with { Idle = state.Idle && !running && !pathfinding && !simpleMove };
        }
        catch (Exception)
        {
            return default;
        }
    }

    // Read first: every write makes AutoDuty save the profile to disk, and an unknown key reads back empty.
    private void ApplySetting(string key, string value)
    {
        string current = _ipc.GetConfig(key) ?? string.Empty;
        if (current.Length == 0)
            throw new InvalidOperationException($"Update AutoDuty: it doesn't support {key}.");

        if (Matches(current, value))
            return;
        _ipc.SetConfig(key, value);
        if (!Matches(_ipc.GetConfig(key), value))
            throw new InvalidOperationException($"AutoDuty did not apply {key}.");
    }

    private static bool Matches(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    // AutoDuty switches back itself when a run stops; this covers runs that never started.
    private bool ReleaseProfile()
    {
        try
        {
            _ipc.PluginConfig(ProfileName, false);
            return true;
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation("AutoDuty didn't switch back from Questionable's profile; this will be retried while idle");
            if (!_loggedReleaseFailure)
            {
                _logger.LogWarning(e, "Unable to switch AutoDuty back from Questionable's profile");
                _loggedReleaseFailure = true;
            }
            return false;
        }
    }

    public void Dispose() => _dutyState.DutyCompleted -= _dutyCompleted;
}

// AutoDuty's Run/Stop API provides no remote operation ID.
[RegisterSingleton]
internal sealed class AutoDutyOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("AutoDuty", "running a duty", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _entryWaitSince;
    private long? _stoppingSince;
    private bool _stopAttempted;
    private bool _observedStop;
    private bool _profileReleased;
    private bool? _stopped;

    public uint TerritoryId { get; private set; }
    public bool RunSubmitted { get; private set; }
    public bool ObservedRunning { get; private set; }
    public bool EnteredDuty { get; private set; }
    public bool DutyCompleted { get; private set; }
    public bool CanReleaseProfile => HasOperation && _stopped == true && HasStableIdle &&
        (RequiresReconciliation || _observedStop || !RunSubmitted);

    public Guid BeginSubmission(uint territoryId)
    {
        Guid owner = base.BeginSubmission();
        TerritoryId = territoryId;
        RunSubmitted = ObservedRunning = EnteredDuty = DutyCompleted = false;
        _stopAttempted = _observedStop = _profileReleased = false;
        _stopped = null;
        _entryWaitSince = _clock.GetTimestamp();
        _stoppingSince = null;
        return owner;
    }

    public void MarkRunSubmitted() => RunSubmitted = true;

    public void RequireReconciliation(string message)
    {
        if (OperationId is not { } owner)
            return;
        MarkUncertain(owner);
        SetStatus(_profileReleased ? ExternalOperationStatus.Unknown : ExternalOperationStatus.CancellationRequested, message);
    }

    public void ObserveDutyCompleted(uint territoryId)
    {
        if (HasOperation && RunSubmitted && territoryId == TerritoryId)
            EnteredDuty = DutyCompleted = true;
    }

    public bool BeginStop()
    {
        bool canStop = HasOperation && ObservedRunning && _stopped == false &&
            !RequiresReconciliation && !_observedStop && !_stopAttempted && !IsTerminal;
        AbandonCurrent();
        _stoppingSince ??= _clock.GetTimestamp();
        if (canStop)
        {
            _stopAttempted = true;
            SetStatus(ExternalOperationStatus.CancellationRequested, "Waiting for AutoDuty to stop");
        }
        return canStop;
    }

    public void Observe(bool? stopped, AutoDutyGameState game)
    {
        if (!HasOperation || IsTerminal)
            return;

        _stopped = stopped;
        if (game.Available && !game.Loading && game.TerritoryId == TerritoryId && RunSubmitted)
            EnteredDuty = true;

        if (stopped == null)
            RequireReconciliation("AutoDuty status was lost; check its work before confirming idle");

        if (stopped == false && _observedStop)
            RequireReconciliation("AutoDuty started again after stopping; Questionable didn't start it");

        if (!RequiresReconciliation)
        {
            if (stopped == false && RunSubmitted)
            {
                ObservedRunning = true;
                SetStatus(IsAbandoned ? ExternalOperationStatus.CancellationRequested : ExternalOperationStatus.Running,
                    EnteredDuty ? "Running duty" : "Waiting to enter duty");
            }
            else if (stopped == true && ObservedRunning)
            {
                _observedStop = true;
                // A normal stop can be followed by a long cutscene or load; only a cancel starts the deadline.
                SetStatus(IsAbandoned ? ExternalOperationStatus.CancellationRequested : ExternalOperationStatus.Running,
                    "Waiting for duty, movement and profile cleanup");
            }

            // A real queue or loading screen is progress, even when it takes hours.
            if (game.Available && (game.InQueue || game.Loading))
                _entryWaitSince = _clock.GetTimestamp();
            else if (RunSubmitted && (!ObservedRunning || !EnteredDuty) &&
                     _clock.GetElapsedTime(_entryWaitSince) >= TimeSpan.FromSeconds(ObservedRunning ? 120 : 15))
                RequireReconciliation("AutoDuty didn't enter or queue for the duty; it may have refused the run");

            if (_stoppingSince is { } stopping && _clock.GetElapsedTime(stopping) >= TimeSpan.FromSeconds(15))
                RequireReconciliation("AutoDuty hasn't confirmed it stopped and left the duty");
        }

        bool? busy = stopped == null || !game.Available ? null :
            stopped != true || !game.Idle || game.InQueue || game.Loading || game.InDuty || game.TerritoryId == TerritoryId;
        ObserveIdle(busy);
    }

    public void ProfileReleased()
    {
        _profileReleased = true;
        if (RequiresReconciliation)
            SetStatus(ExternalOperationStatus.Unknown, Message);
    }

    public void FinishIfSettled()
    {
        if (!_profileReleased || !CanReleaseProfile || RequiresReconciliation)
            return;
        SetStatus(IsAbandoned ? ExternalOperationStatus.Cancelled :
            EnteredDuty && DutyCompleted ? ExternalOperationStatus.Succeeded : ExternalOperationStatus.Failed,
            EnteredDuty && DutyCompleted ? "Duty completed and exited" : "AutoDuty stopped without verified duty completion");
    }

    private void SetStatus(ExternalOperationStatus status, string message)
    {
        // Repeating a non-Unknown status would reset OwnedOperationState's idle streak.
        if (OperationId is { } owner && (Status != status || Message != message))
            ObserveOperationStatus(owner, status, message);
    }
}

internal readonly record struct AutoDutyGameState(
    bool Available, uint TerritoryId, bool InQueue, bool Loading, bool InDuty, bool Idle);
