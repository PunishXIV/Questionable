namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class GatheringOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Gather n Chill", "gathering quest items", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _finishingSince;
    private bool _accepted;
    private bool _stopAttempted;

    public ExternalOperationStatus Status => CurrentStatus;

    public new Guid BeginSubmission()
    {
        Guid owner = base.BeginSubmission();
        _accepted = _stopAttempted = false;
        _finishingSince = null;
        return owner;
    }

    public void AcceptLegacySubmission(Guid owner)
    {
        AcceptSubmission(owner, null);
        _accepted = OperationId == owner;
    }

    public void MarkLegacyUncertain(Guid owner)
    {
        MarkUncertain(owner);
        ObserveOperationStatus(owner, ExternalOperationStatus.Unknown, "Check Gather n Chill before restarting");
    }

    public bool BeginLegacyStop()
    {
        bool canStop = HasOperation && _accepted && !RequiresReconciliation && !_stopAttempted && !IsTerminal;
        _stopAttempted = true;
        AbandonCurrent();
        return canStop;
    }

    public void ObserveLegacyStatus(bool? busy, bool? childrenIdle)
    {
        if (OperationId is not { } owner || IsTerminal)
            return;
        if (busy == null || childrenIdle == null)
            MarkLegacyUncertain(owner);

        if (RequiresReconciliation)
        {
            ObserveOperationStatus(owner, ExternalOperationStatus.Unknown, "Gathering ownership needs confirmation");
            ObserveIdle(busy == false && childrenIdle == true ? false : busy == null || childrenIdle == null ? null : true);
            return;
        }

        if (busy == true)
        {
            _finishingSince = null;
            ObserveOperationStatus(owner, IsAbandoned ? ExternalOperationStatus.CancellationRequested : ExternalOperationStatus.Running, "Gathering");
            ObserveIdle(true);
            return;
        }

        _finishingSince ??= _clock.GetTimestamp();
        if (_clock.GetElapsedTime(_finishingSince.Value) >= TimeSpan.FromSeconds(15))
        {
            MarkLegacyUncertain(owner);
            ObserveIdle(childrenIdle == true ? false : true);
            return;
        }

        // Legacy IsBusy can become false while travel or gathering animations are settling.
        // Keep the idle streak across those observations, then let the executor verify output.
        if (CurrentStatus != ExternalOperationStatus.CancellationRequested)
            ObserveOperationStatus(owner, ExternalOperationStatus.CancellationRequested, "Waiting for gathering and travel to stop");
        ObserveIdle(childrenIdle != true);
        if (HasStableIdle)
            ObserveOperationStatus(owner, IsAbandoned ? ExternalOperationStatus.Cancelled : ExternalOperationStatus.Succeeded, "Stopped");
    }

    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
