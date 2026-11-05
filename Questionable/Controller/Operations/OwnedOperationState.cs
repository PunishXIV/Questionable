namespace Questionable.Controller.Operations;

/// <summary>Shared ownership and recovery rules; provider IDs are retained when the API supplies them.</summary>
internal abstract class OwnedOperationState(string providerName, string activity, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _unknownSince;
    private long? _stoppingSince;
    private long? _lastCancelAttempt;
    private bool _cancelAccepted;
    private long? _idleSince;
    private long? _lastIdleObservation;

    public Guid? OperationId { get; private set; }
    public Guid? ProviderOperationId { get; private set; }
    public bool HasOperation => OperationId.HasValue;
    public bool IsAbandoned { get; private set; }
    public bool RequiresReconciliation { get; private set; }
    public ExternalOperationStatus Status { get; private set; }
    public string Message { get; private set; } = "";
    public bool IsTerminal => Status is ExternalOperationStatus.Succeeded or ExternalOperationStatus.Failed or ExternalOperationStatus.Cancelled;
    public string? BlockReason => !HasOperation ? null : RequiresReconciliation
        ? $"Check {providerName}: finish or stop its work, then confirm it is stopped."
        : IsAbandoned || Status == ExternalOperationStatus.CancellationRequested
            ? $"Waiting for {providerName} to stop its work."
            : $"{providerName} is {activity}. Stop Questionable before starting other work.";

    public bool CanConfirmIdle => HasOperation && RequiresReconciliation && Status == ExternalOperationStatus.Unknown && HasStableIdle;

    protected bool HasStableIdle =>
        _idleSince is { } idleSince && _lastIdleObservation is { } observedAt &&
        _clock.GetElapsedTime(idleSince) >= TimeSpan.FromSeconds(2) &&
        _clock.GetElapsedTime(observedAt) <= TimeSpan.FromSeconds(1);

    public Guid BeginSubmission()
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);

        OperationId = Guid.NewGuid();
        Status = ExternalOperationStatus.Unknown;
        _unknownSince = _clock.GetTimestamp();
        return OperationId.Value;
    }

    public void AcceptSubmission(Guid owner, Guid? providerId)
    {
        if (OperationId != owner)
            return;
        if (providerId == Guid.Empty)
        {
            MarkUncertain(owner);
            return;
        }

        ProviderOperationId = providerId;
        Status = ExternalOperationStatus.Running;
        _unknownSince = null;
    }

    public void RejectSubmission(Guid owner)
    {
        if (OperationId == owner && ProviderOperationId == null)
            Clear();
    }

    public void MarkUncertain(Guid owner)
    {
        if (OperationId == owner)
            RequiresReconciliation = true;
    }

    protected void ObserveOperationStatus(Guid owner, ExternalOperationStatus status, string message)
    {
        if (OperationId != owner || IsTerminal)
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

        if (Status == ExternalOperationStatus.CancellationRequested)
            _stoppingSince ??= now;

        if ((_unknownSince is { } unknown && _clock.GetElapsedTime(unknown, now) >= TimeSpan.FromSeconds(10)) ||
            (_stoppingSince is { } stopping && _clock.GetElapsedTime(stopping, now) >= TimeSpan.FromSeconds(15)))
            RequiresReconciliation = true;

        if (IsTerminal && IsAbandoned)
            Clear();
    }

    public void AbandonCurrent()
    {
        if (!HasOperation)
            return;
        IsAbandoned = true;
        _stoppingSince ??= _clock.GetTimestamp();
        if (IsTerminal)
            Clear();
    }

    public bool TryBeginCancellation(out Guid providerId)
    {
        providerId = ProviderOperationId.GetValueOrDefault();
        if (!HasOperation || !IsAbandoned || providerId == Guid.Empty || IsTerminal || _cancelAccepted ||
            (_lastCancelAttempt is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(2)))
            return false;

        _lastCancelAttempt = _clock.GetTimestamp();
        return true;
    }

    public void ObserveCancellation(Guid providerId, bool accepted)
    {
        if (ProviderOperationId == providerId)
            _cancelAccepted |= accepted;
    }

    public bool TryReleaseTerminal(Guid? owner)
    {
        if (!owner.HasValue || OperationId != owner || !IsTerminal)
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
        OperationId = ProviderOperationId = null;
        Status = ExternalOperationStatus.Unknown;
        Message = "";
        IsAbandoned = RequiresReconciliation = _cancelAccepted = false;
        _unknownSince = _stoppingSince = _lastCancelAttempt = _idleSince = _lastIdleObservation = null;
    }
}
