namespace Questionable.Controller.Operations;

/// <summary>Local ownership for AutoDuty's void Run/Stop API; there is no remote operation ID.</summary>
[RegisterSingleton]
internal sealed class AutoDutyOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("AutoDuty", "running a duty", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _entryWaitSince;
    private long? _stoppingSince;
    private bool _stopAttempted;
    private bool _observedStop;
    private bool _settingsRestored;
    private bool? _stopped;

    public uint TerritoryId { get; private set; }
    public bool RunSubmitted { get; private set; }
    public bool ObservedRunning { get; private set; }
    public bool EnteredDuty { get; private set; }
    public bool DutyCompleted { get; private set; }
    public bool ProviderLost { get; private set; }
    public bool CanRestoreSettings => HasOperation && _stopped == true && HasStableIdle &&
        (RequiresReconciliation || _observedStop || !RunSubmitted);

    public Guid BeginSubmission(uint territoryId)
    {
        Guid owner = base.BeginSubmission();
        TerritoryId = territoryId;
        RunSubmitted = ObservedRunning = EnteredDuty = DutyCompleted = ProviderLost = false;
        _stopAttempted = _observedStop = _settingsRestored = false;
        _stopped = null;
        _entryWaitSince = _clock.GetTimestamp();
        _stoppingSince = null;
        return owner;
    }

    public void MarkRunSubmitted() => RunSubmitted = true;

    public void RequireReconciliation(string message, bool providerLost = false)
    {
        if (OperationId is not { } owner)
            return;
        ProviderLost |= providerLost;
        MarkUncertain(owner);
        SetStatus(_settingsRestored ? ExternalOperationStatus.Unknown : ExternalOperationStatus.CancellationRequested, message);
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
            RequireReconciliation("AutoDuty status was lost; check its work and temporary settings before confirming idle", providerLost: true);

        if (stopped == false && _observedStop)
            RequireReconciliation("AutoDuty started again after stopping; its new work is not owned by Questionable", providerLost: true);

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
                    "Waiting for duty, movement and settings cleanup");
            }

            // A real queue or loading screen is progress, even when it takes hours.
            if (game.Available && (game.InQueue || game.Loading))
                _entryWaitSince = _clock.GetTimestamp();
            else if (RunSubmitted && (!ObservedRunning || !EnteredDuty) &&
                     _clock.GetElapsedTime(_entryWaitSince) >= TimeSpan.FromSeconds(ObservedRunning ? 120 : 15))
                RequireReconciliation("AutoDuty did not enter or queue for the requested duty; its void Run call may have been rejected");

            if (_stoppingSince is { } stopping && _clock.GetElapsedTime(stopping) >= TimeSpan.FromSeconds(15))
                RequireReconciliation("AutoDuty stop or native duty exit has not been confirmed");
        }

        bool? busy = stopped == null || !game.Available ? null :
            stopped != true || !game.Idle || game.InQueue || game.Loading || game.InDuty || game.TerritoryId == TerritoryId;
        ObserveIdle(busy);
    }

    public void SettingsRestored()
    {
        _settingsRestored = true;
        if (RequiresReconciliation)
            SetStatus(ExternalOperationStatus.Unknown, Message);
    }

    public void FinishIfSettled()
    {
        if (!_settingsRestored || !CanRestoreSettings || RequiresReconciliation)
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
