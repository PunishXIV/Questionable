namespace Questionable.Controller;

/// <summary>Owns the Feesh catch job across queue resets and normal stops.</summary>
[RegisterSingleton]
internal sealed class FishingOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Feesh", "fishing for quest items", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _submittedAt;
    private long? _lastStopAttempt;
    private bool _stopAccepted;

    public ExternalOperationStatus Status => CurrentStatus;
    public bool HasStarted { get; private set; }
    public bool StopRequested { get; private set; }

    public new Guid BeginSubmission()
    {
        Guid owner = base.BeginSubmission();
        HasStarted = StopRequested = _stopAccepted = false;
        _lastStopAttempt = null;
        _submittedAt = _clock.GetTimestamp();
        return owner;
    }

    /// <summary>Feesh has no job IDs, so the local ID is used for both.</summary>
    public void AcceptCatch(Guid owner) => AcceptSubmission(owner, owner);

    /// <param name="catchDone"><c>CatchDone</c>'s result for this job; null until it fires.</param>
    /// <param name="busy"><c>WahTools.IsBusy</c>; null if unreadable.</param>
    public void ObserveCatch(Guid owner, EFeeshJobState state, string message, bool? catchDone, bool? busy)
    {
        if (OperationId != owner || IsTerminal)
            return;

        bool active = state is EFeeshJobState.Fishing or EFeeshJobState.Finishing;
        HasStarted |= active || catchDone != null;
        ExternalOperationStatus running = StopRequested || IsAbandoned
            ? ExternalOperationStatus.CancellationRequested
            : ExternalOperationStatus.Running;

        // CatchDone fires once the rod is away and travel has stopped.
        if (catchDone is { } met)
        {
            ObserveOperationStatus(owner, Final(state, met), message);
            return;
        }

        if (active)
        {
            ObserveOperationStatus(owner, running, message);
            return;
        }

        if (HasStarted && state is EFeeshJobState.Done or EFeeshJobState.Partial or EFeeshJobState.Failed
                or EFeeshJobState.Cancelled or EFeeshJobState.Stopped)
        {
            // Without CatchDone, finish once WahTools stays idle. Observe stopping once: it resets the idle streak.
            if (CurrentStatus != ExternalOperationStatus.CancellationRequested)
                ObserveOperationStatus(owner, ExternalOperationStatus.CancellationRequested, message);
            ObserveIdle(busy);
            if (HasStableIdle)
                ObserveOperationStatus(owner, Final(state, state == EFeeshJobState.Done), message);
            return;
        }

        // Until our job is seen running, an ended line may be the previous job's.
        bool startupTimedOut = !HasStarted && _clock.GetElapsedTime(_submittedAt) >= TimeSpan.FromSeconds(30);
        bool unknown = state is EFeeshJobState.Unavailable or EFeeshJobState.Unrecognized ||
                       (HasStarted && state == EFeeshJobState.None) || startupTimedOut;
        ObserveOperationStatus(owner, unknown ? ExternalOperationStatus.Unknown : running, message);

        if (startupTimedOut)
        {
            RequestStop();
            MarkUncertain(owner);
        }
    }

    private static ExternalOperationStatus Final(EFeeshJobState state, bool met) =>
        state == EFeeshJobState.Cancelled ? ExternalOperationStatus.Cancelled
        : met ? ExternalOperationStatus.Succeeded
        : ExternalOperationStatus.Failed;

    public void RequestStop()
    {
        if (!HasOperation || IsTerminal)
            return;
        StopRequested = true;
        // Unknown stays unknown, and observing stopping again would reset the idle streak.
        if (Status is not (ExternalOperationStatus.Unknown or ExternalOperationStatus.CancellationRequested))
            ObserveOperationStatus(OperationId!.Value, ExternalOperationStatus.CancellationRequested, "Stopping fishing");
    }

    public bool TryBeginStop(out Guid owner)
    {
        owner = OperationId.GetValueOrDefault();
        // CancelCatch only ends our own job, so it is safe after a lost start reply.
        if (!HasOperation || IsTerminal || !(StopRequested || IsAbandoned) || _stopAccepted ||
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
