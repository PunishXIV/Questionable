namespace Questionable.Controller;

/// <summary>Retains fishing ownership through queue resets and acknowledged normal stops.</summary>
[RegisterSingleton]
internal sealed class FishingOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("AutoHook", "fishing for quest items", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _submittedAt;
    private long? _lastStopAttempt;
    private bool _stopAccepted;

    public ExternalOperationStatus Status => CurrentStatus;
    public bool HasStarted { get; private set; }
    public bool StopRequested { get; private set; }
    public bool OwnershipLost { get; private set; }

    public new Guid BeginSubmission()
    {
        Guid owner = base.BeginSubmission();
        HasStarted = StopRequested = OwnershipLost = _stopAccepted = false;
        _lastStopAttempt = null;
        _submittedAt = _clock.GetTimestamp();
        return owner;
    }

    public void ObserveStatus(Guid owner, AutoHookIpc.EOwnedFishingState state, bool hasStarted, string message)
    {
        if (OperationId != owner || IsTerminal)
            return;

        HasStarted |= hasStarted;
        if (state == AutoHookIpc.EOwnedFishingState.OwnershipLost)
        {
            OwnershipLost = true;
            MarkUncertain(owner);
        }

        ExternalOperationStatus status = state switch
        {
            AutoHookIpc.EOwnedFishingState.Starting or AutoHookIpc.EOwnedFishingState.Running =>
                StopRequested ? ExternalOperationStatus.CancellationRequested : ExternalOperationStatus.Running,
            AutoHookIpc.EOwnedFishingState.Stopping => ExternalOperationStatus.CancellationRequested,
            AutoHookIpc.EOwnedFishingState.Stopped => ExternalOperationStatus.Succeeded,
            AutoHookIpc.EOwnedFishingState.Failed => ExternalOperationStatus.Failed,
            _ => ExternalOperationStatus.Unknown,
        };
        ObserveOperationStatus(owner, status, message);

        if (!HasStarted && (state is AutoHookIpc.EOwnedFishingState.Starting or AutoHookIpc.EOwnedFishingState.Running) &&
            _clock.GetElapsedTime(_submittedAt) >= TimeSpan.FromSeconds(30))
        {
            RequestStop();
            MarkUncertain(owner);
        }
    }

    public void RequestStop()
    {
        if (!HasOperation || IsTerminal || OwnershipLost)
            return;
        StopRequested = true;
        // Unknown must stay unknown: a missing provider cannot acknowledge cancellation.
        if (Status != ExternalOperationStatus.Unknown)
            ObserveOperationStatus(OperationId!.Value, ExternalOperationStatus.CancellationRequested, "Stopping fishing");
    }

    public bool TryBeginStop(out Guid owner)
    {
        owner = OperationId.GetValueOrDefault();
        if (!HasOperation || IsTerminal || OwnershipLost || !(StopRequested || IsAbandoned) || _stopAccepted ||
            (_lastStopAttempt is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(2)))
            return false;

        _lastStopAttempt = _clock.GetTimestamp();
        return true;
    }

    public void ObserveStop(Guid owner, bool accepted)
    {
        if (OperationId == owner)
            _stopAccepted |= accepted;
    }
}
