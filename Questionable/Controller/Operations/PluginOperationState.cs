namespace Questionable.Controller.Operations;

internal abstract class PluginOperationState(string providerName, string activity, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _unknownSince;
    private long? _stoppingSince;
    private long? _lastCancelAttempt;
    private bool _cancelAccepted;
    private long? _idleSince;
    private long? _lastIdleObservation;

    public Guid? Id { get; private set; }
    public Guid? ProviderId { get; private set; }
    public bool HasOperation => Id.HasValue;
    public bool IsAbandoned { get; private set; }
    public bool NeedsConfirmation { get; private set; }
    public ExternalOperationStatus Status { get; private set; }
    public string Message { get; private set; } = "";
    public bool IsFinished => Status is ExternalOperationStatus.Succeeded or ExternalOperationStatus.Failed or ExternalOperationStatus.Cancelled;
    public string? BlockReason => !HasOperation ? null : NeedsConfirmation
        ? $"Check {providerName}: finish or stop its work, then confirm it is stopped."
        : IsAbandoned || Status == ExternalOperationStatus.CancelRequested
            ? $"Waiting for {providerName} to stop its work."
            : $"{providerName} is {activity}. Stop Questionable before starting other work.";

    public bool CanConfirmIdle => HasOperation && NeedsConfirmation && Status == ExternalOperationStatus.Unknown && IdleSettled;

    protected bool IdleSettled =>
        _idleSince is { } idleSince && _lastIdleObservation is { } observedAt &&
        _clock.GetElapsedTime(idleSince) >= TimeSpan.FromSeconds(2) &&
        _clock.GetElapsedTime(observedAt) <= TimeSpan.FromSeconds(1);

    public Guid BeginRequest()
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);

        Id = Guid.NewGuid();
        Status = ExternalOperationStatus.Unknown;
        _unknownSince = _clock.GetTimestamp();
        return Id.Value;
    }

    public void AcceptRequest(Guid id, Guid? providerId)
    {
        if (Id != id)
            return;
        if (providerId == Guid.Empty)
        {
            MarkUncertain(id);
            return;
        }

        ProviderId = providerId;
        Status = ExternalOperationStatus.Running;
        _unknownSince = null;
    }

    public void RejectRequest(Guid id)
    {
        if (Id == id && ProviderId == null)
            Clear();
    }

    public void MarkUncertain(Guid id)
    {
        if (Id == id)
            NeedsConfirmation = true;
    }

    protected void ObserveStatus(Guid id, ExternalOperationStatus status, string message)
    {
        if (Id != id || IsFinished)
            return;

        Status = Enum.IsDefined(status) ? status : ExternalOperationStatus.Unknown;
        Message = message;
        long now = _clock.GetTimestamp();
        if (Status == ExternalOperationStatus.Unknown)
            _unknownSince ??= now;
        else
        {
            _unknownSince = null;
            // Running contradicts an idle report, so idle has to be observed afresh.
            _idleSince = _lastIdleObservation = null;
        }

        if (Status == ExternalOperationStatus.CancelRequested)
            _stoppingSince ??= now;

        if ((_unknownSince is { } unknown && _clock.GetElapsedTime(unknown, now) >= TimeSpan.FromSeconds(10)) ||
            (_stoppingSince is { } stopping && _clock.GetElapsedTime(stopping, now) >= TimeSpan.FromSeconds(15)))
            NeedsConfirmation = true;

        if (IsFinished && IsAbandoned)
            Clear();
    }

    public void Abandon()
    {
        if (!HasOperation)
            return;
        IsAbandoned = true;
        _stoppingSince ??= _clock.GetTimestamp();
        if (IsFinished)
            Clear();
    }

    public bool TryBeginCancel(out Guid providerId)
    {
        providerId = ProviderId.GetValueOrDefault();
        if (!HasOperation || !IsAbandoned || providerId == Guid.Empty || IsFinished || _cancelAccepted ||
            (_lastCancelAttempt is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(2)))
            return false;

        _lastCancelAttempt = _clock.GetTimestamp();
        return true;
    }

    public void ObserveCancel(Guid providerId, bool accepted)
    {
        if (ProviderId == providerId)
            _cancelAccepted |= accepted;
    }

    public bool TryClearFinished(Guid? id)
    {
        if (!id.HasValue || Id != id || !IsFinished)
            return false;
        Clear();
        return true;
    }

    public void ObserveIdle(bool? busy)
    {
        long now = _clock.GetTimestamp();
        if (!HasOperation || busy != false || (_lastIdleObservation is { } last &&
                _clock.GetElapsedTime(last, now) > TimeSpan.FromSeconds(1)))
            _idleSince = null;

        _lastIdleObservation = now;
        if (HasOperation && busy == false)
            _idleSince ??= now;
    }

    public bool TryConfirmIdle()
    {
        if (!CanConfirmIdle)
            return false;
        Clear();
        return true;
    }

    private void Clear()
    {
        Id = ProviderId = null;
        Status = ExternalOperationStatus.Unknown;
        Message = "";
        IsAbandoned = NeedsConfirmation = _cancelAccepted = false;
        _unknownSince = _stoppingSince = _lastCancelAttempt = _idleSince = _lastIdleObservation = null;
    }
}
