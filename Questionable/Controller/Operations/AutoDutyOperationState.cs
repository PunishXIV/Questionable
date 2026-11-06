namespace Questionable.Controller.Operations;

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
            RequireReconciliation("AutoDuty started again after stopping; its new work is not owned by Questionable");

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
                RequireReconciliation("AutoDuty did not enter or queue for the requested duty; its void Run call may have been rejected");

            if (_stoppingSince is { } stopping && _clock.GetElapsedTime(stopping) >= TimeSpan.FromSeconds(15))
                RequireReconciliation("AutoDuty stop or native duty exit has not been confirmed");
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
