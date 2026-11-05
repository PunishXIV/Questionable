namespace Questionable.Controller.Operations;

/// <summary>
/// Retains legacy Artisan submissions independently of the task queue. The current IPC cannot
/// identify or cancel an operation, so abandoned or uncertain work needs explicit reconciliation.
/// </summary>
[RegisterSingleton]
internal sealed class CraftingOperationState(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _idleSince;
    private long? _lastObservedAt;
    private ArtisanStatus _status;

    public Guid? OperationId { get; private set; }
    public bool HasOperation => OperationId.HasValue;
    public bool RequiresReconciliation { get; private set; }

    public string? BlockReason => !HasOperation ? null : RequiresReconciliation
        ? "Artisan may still have crafting work. Finish or stop it in Artisan, then confirm it is stopped."
        : "Artisan is handling a crafting request. Stop Questionable before starting other work.";

    public bool CanConfirmIdle => HasOperation && RequiresReconciliation && HasStableIdle;

    private bool HasStableIdle => _status == ArtisanStatus.Idle && _idleSince is { } idleSince &&
        _lastObservedAt is { } observedAt &&
        _clock.GetElapsedTime(idleSince) >= TimeSpan.FromSeconds(2) &&
        _clock.GetElapsedTime(observedAt) <= TimeSpan.FromSeconds(1);

    public Guid BeginSubmission()
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);

        OperationId = Guid.NewGuid();
        RequiresReconciliation = false;
        _idleSince = null;
        _lastObservedAt = null;
        _status = ArtisanStatus.Queued;
        return OperationId.Value;
    }

    public void MarkUncertain(Guid operationId)
    {
        if (OperationId == operationId)
            AbandonCurrent();
    }

    public void AbandonCurrent()
    {
        if (!HasOperation)
            return;

        RequiresReconciliation = true;
    }

    public void Abandon(Guid? operationId)
    {
        if (operationId.HasValue && OperationId == operationId)
            AbandonCurrent();
    }

    public void ObserveStatus(ArtisanStatus status)
    {
        if (!HasOperation)
            return;

        long now = _clock.GetTimestamp();
        if (_lastObservedAt is { } previous && _clock.GetElapsedTime(previous, now) > TimeSpan.FromSeconds(1))
            _idleSince = null;

        _status = status;
        _lastObservedAt = now;
        if (status == ArtisanStatus.Idle)
            _idleSince ??= _lastObservedAt;
        else
            _idleSince = null;

        // A later idle response cannot prove that a replacement provider finished our request.
        if (status == ArtisanStatus.Unavailable)
            RequiresReconciliation = true;
    }

    public bool CanContinue(Guid? operationId) => operationId.HasValue && OperationId == operationId &&
        !RequiresReconciliation;

    public bool TryComplete(Guid? operationId)
    {
        if (!CanContinue(operationId) || !HasStableIdle)
            return false;

        Clear();
        return true;
    }

    /// <summary>Called only after the user confirms they finished or stopped Artisan's work.</summary>
    public bool TryConfirmIdle()
    {
        if (!CanConfirmIdle)
            return false;

        Clear();
        return true;
    }

    private void Clear()
    {
        OperationId = null;
        RequiresReconciliation = false;
        _idleSince = null;
        _lastObservedAt = null;
    }
}
