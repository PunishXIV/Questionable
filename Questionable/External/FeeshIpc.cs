using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class FeeshIpc : Ipc, IDisposable
{
    internal const string CallerName = "Questionable";

    private readonly ILogger<FeeshIpc> _logger;
    private readonly FishingState _operation;
    private readonly IFramework _framework;
    private readonly Dictionary<uint, long> _pendingReleases = [];
    private readonly object _catchDoneLock = new();
    private readonly EzIPCDisposalToken[] _disposalTokens;
    private bool? _catchDoneResult;
    private uint? _heldFish;

    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("Feesh.TaggedStand")] private readonly Func<uint, string> _taggedStand = null!;
    [EzIPC("Feesh.Catch")] private readonly Func<string, uint, int, bool, bool> _catch = null!;
    [EzIPC("Feesh.Catching")] private readonly Func<string> _catching = null!;
    [EzIPC("Feesh.CancelCatch")] private readonly Func<string, bool> _cancelCatch = null!;
    [EzIPC("Feesh.ReleaseCatch")] private readonly Func<string, uint, bool> _releaseCatch = null!;

    public FeeshIpc(ILogger<FeeshIpc> logger, FishingState operation, IFramework framework)
    {
        _logger = logger;
        _operation = operation;
        _framework = framework;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
        _framework.Update += OnFrameworkUpdate;
    }

    public override string InternalName => "WahTools";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IsAvailable();
    public bool IsAvailable() => ReadCatching() != null;
    public bool? GetBusy() => Read(_isBusy);

    public bool CanCatch(uint fishId)
    {
        try
        {
            return !string.IsNullOrEmpty(_taggedStand(fishId));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to ask Feesh where to catch {FishId}", fishId);
            return false;
        }
    }

    public FeeshStartResult StartCatch(uint fishId, int additionalCount, out string message)
    {
        message = "";
        if (_operation.HasOperation)
            return FeeshStartResult.Busy;
        if (fishId == 0 || additionalCount <= 0)
            return FeeshStartResult.Invalid;
        if (!IsAvailable())
            return FeeshStartResult.NotReady;
        if (GetBusy() != false)
            return FeeshStartResult.Busy;

        Guid id = _operation.BeginRequest();
        lock (_catchDoneLock)
            _catchDoneResult = null;
        try
        {
            _logger.LogInformation("Requesting {Count} additional {FishId} from Feesh", additionalCount, fishId);
            if (!_catch(CallerName, fishId, additionalCount, true))
            {
                _operation.RejectRequest(id);
                return GetBusy() == true ? FeeshStartResult.Busy : FeeshStartResult.Refused;
            }

            _operation.AcceptCatch(id);
            // The new job now needs Questionable's hold on this fish; an old release must not remove it.
            _pendingReleases.Remove(fishId);
            _heldFish = fishId;
            return FeeshStartResult.Started;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Feesh catch submission has an uncertain result");
            _operation.MarkUncertain(id);
            _pendingReleases.Remove(fishId);
            _heldFish = fishId;
            message = e.Message;
            return FeeshStartResult.Uncertain;
        }
    }

    public void PollOperation()
    {
        RetryPendingReleases();
        if (_operation.Id is not { } id)
            return;

        string? text = ReadCatching();
        bool? catchDone;
        lock (_catchDoneLock)
            catchDone = _catchDoneResult;
        _operation.ObserveCatch(id, ParseState(text), text ?? "Feesh is unavailable", catchDone, GetBusy());
        SendStop();
        if (_operation.NeedsConfirmation)
            ObserveIdle();
    }

    public void RequestStop()
    {
        _operation.RequestStop();
        SendStop();
    }

    public void CancelWork()
    {
        RetryPendingReleases();
        if (!_operation.HasOperation)
            return;
        _operation.Abandon();
        RequestStop();
        if (_heldFish is { } fishId)
            ReleaseCatch(fishId);
    }

    public void ObserveIdle() => _operation.ObserveIdle(GetBusy());

    public void ReleaseCatch(uint fishId)
    {
        if (_heldFish == fishId)
            _heldFish = null;
        TryReleaseCatch(fishId);
    }

    private void TryReleaseCatch(uint fishId)
    {
        _pendingReleases[fishId] = TimeProvider.System.GetTimestamp();
        try
        {
            // False means Questionable no longer holds the fish, which also completes cleanup.
            _releaseCatch(CallerName, fishId);
            _pendingReleases.Remove(fishId);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to release Feesh's hold on {FishId}", fishId);
        }
    }

    // QuestController stops polling once the job is released, but its hold cleanup can still be pending.
    private void OnFrameworkUpdate(IFramework framework) => RetryPendingReleases();

    private void RetryPendingReleases(bool force = false)
    {
        if (_pendingReleases.Count == 0)
            return;
        foreach (var (fishId, lastAttempt) in _pendingReleases.ToArray())
        {
            if (force || TimeProvider.System.GetElapsedTime(lastAttempt) >= TimeSpan.FromSeconds(2))
                TryReleaseCatch(fishId);
        }
    }

    internal static EFeeshJobState ParseState(string? text)
    {
        if (text == null)
            return EFeeshJobState.Unavailable;

        string trimmed = text.TrimStart();
        int end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
            end++;
        return trimmed[..end].ToUpperInvariant() switch
        {
            "" when trimmed.Length == 0 => EFeeshJobState.None,
            "FISHING" => EFeeshJobState.Fishing,
            "FINISHING" => EFeeshJobState.Finishing,
            "DONE" => EFeeshJobState.Done,
            "PARTIAL" => EFeeshJobState.Partial,
            "FAILED" => EFeeshJobState.Failed,
            "CANCELLED" or "CANCELED" => EFeeshJobState.Cancelled,
            "STOPPED" => EFeeshJobState.Stopped,
            _ => EFeeshJobState.Unrecognized,
        };
    }

    [EzIPCEvent("Feesh.CatchDone")]
    private void OnCatchDone(string caller, bool done)
    {
        if (caller != CallerName)
            return;
        // May arrive off the framework thread; the next poll applies it.
        lock (_catchDoneLock)
            _catchDoneResult = done;
    }

    private void SendStop()
    {
        if (!_operation.TryBeginStop(out Guid id))
            return;
        try
        {
            _operation.ObserveStop(id, _cancelCatch(CallerName));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to cancel Feesh catch job {Id}", id);
        }
    }

    private string? ReadCatching()
    {
        try
        {
            return _catching() ?? "";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? Read(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        IPCSubscriber.DisposeAll(_disposalTokens);
        IpcInvoke.TryOnFrameworkThread(_framework, () => RetryPendingReleases(force: true), _logger);

        // Framework unloading can skip both the final retry and the controller's cancellation.
        var remainingHolds = _pendingReleases.Keys.ToHashSet();
        if (_heldFish is { } fishId)
            remainingHolds.Add(fishId);
        if (remainingHolds.Count > 0)
            _logger.LogWarning("Feesh holds could not be released before unloading: {FishIds}. Release them in WahTools.",
                string.Join(", ", remainingHolds));
    }
}

internal enum FeeshStartResult
{
    Started,
    Uncertain,
    Busy,
    Refused,
    NotReady,
    Invalid,
}

internal enum EFeeshJobState
{
    Unavailable,
    None,
    Fishing,
    Finishing,
    Done,
    Partial,
    Failed,
    Cancelled,
    Stopped,
    Unrecognized,
}

[RegisterSingleton<IOperationProvider, FeeshProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class FeeshProvider(FeeshIpc feeshIpc, FishingState operation)
    : PluginOperationProvider(operation)
{
    public override int Order => 400;
    public override string Name => "Feesh";
    protected override string CancelLabel => "Feesh fishing";
    public override bool HandlesLeadInTravel => true;

    protected override bool MatchesExecutor(ITaskExecutor? executor) => executor is Fish.DoFish;
    protected override void PollOperation() => feeshIpc.PollOperation();
    protected override void CancelWork() => feeshIpc.CancelWork();
    protected override void ObserveIdle() => feeshIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = feeshIpc.GetBusy();
        return busy == true || (busy == null && feeshIpc.IsAvailable());
    }

    // Fish Feesh can't catch keep their travel for manual fishing.
    public override bool HandlesTravel(QuestStep step) => Fish.IsHandledByFeesh(step, feeshIpc);

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Feesh before restarting."),
        _L("Fishing for quest items with Feesh"),
        _L("I stopped Feesh"),
        _L("Finish or stop fishing in Feesh before confirming."),
        _L("WahTools must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Feesh"), _L("Fishing"), _L("Stopping fishing"));

    // WahTools drives the character while busy.
    public override string? GetStartBlockReason() =>
        feeshIpc.IsAvailable() && feeshIpc.GetBusy() != false
            ? _L("WahTools is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}

[RegisterSingleton]
internal sealed class FishingState(TimeProvider? timeProvider = null)
    : PluginOperationState("Feesh", "fishing for quest items", timeProvider)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _requestedAt;
    private long? _lastStopAttempt;
    private bool _stopAccepted;

    public bool HasStarted { get; private set; }
    public bool StopRequested { get; private set; }

    public new Guid BeginRequest()
    {
        Guid id = base.BeginRequest();
        HasStarted = StopRequested = _stopAccepted = false;
        _lastStopAttempt = null;
        _requestedAt = _clock.GetTimestamp();
        return id;
    }

    // Feesh has no job IDs, so the local ID is used for both.
    public void AcceptCatch(Guid id) => AcceptRequest(id, id);

    public void ObserveCatch(Guid id, EFeeshJobState state, string message, bool? catchDone, bool? busy)
    {
        if (Id != id || IsFinished)
            return;

        bool active = state is EFeeshJobState.Fishing or EFeeshJobState.Finishing;
        HasStarted |= active || catchDone != null;
        ExternalOperationStatus running = StopRequested || IsAbandoned
            ? ExternalOperationStatus.CancelRequested
            : ExternalOperationStatus.Running;

        // CatchDone fires once the rod is away and travel has stopped.
        if (catchDone is { } met)
        {
            ObserveStatus(id, Final(state, met), message);
            return;
        }

        if (active)
        {
            ObserveStatus(id, running, message);
            return;
        }

        if (HasStarted && state is EFeeshJobState.Done or EFeeshJobState.Partial or EFeeshJobState.Failed
                or EFeeshJobState.Cancelled or EFeeshJobState.Stopped)
        {
            // Without CatchDone, finish once WahTools stays idle. Observe stopping once: it resets the idle streak.
            if (Status != ExternalOperationStatus.CancelRequested)
                ObserveStatus(id, ExternalOperationStatus.CancelRequested, message);
            ObserveIdle(busy);
            if (IdleSettled)
                ObserveStatus(id, Final(state, state == EFeeshJobState.Done), message);
            return;
        }

        // Until our job is seen running, an ended line may be the previous job's.
        bool startupTimedOut = !HasStarted && _clock.GetElapsedTime(_requestedAt) >= TimeSpan.FromSeconds(30);
        bool unknown = state is EFeeshJobState.Unavailable or EFeeshJobState.Unrecognized ||
                       (HasStarted && state == EFeeshJobState.None) || startupTimedOut;
        ObserveStatus(id, unknown ? ExternalOperationStatus.Unknown : running, message);

        if (startupTimedOut)
        {
            RequestStop();
            MarkUncertain(id);
        }
    }

    private static ExternalOperationStatus Final(EFeeshJobState state, bool met) =>
        state == EFeeshJobState.Cancelled ? ExternalOperationStatus.Cancelled
        : met ? ExternalOperationStatus.Succeeded
        : ExternalOperationStatus.Failed;

    public void RequestStop()
    {
        if (!HasOperation || IsFinished)
            return;
        StopRequested = true;
        // Unknown stays unknown, and observing stopping again would reset the idle streak.
        if (Status is not (ExternalOperationStatus.Unknown or ExternalOperationStatus.CancelRequested))
            ObserveStatus(Id!.Value, ExternalOperationStatus.CancelRequested, "Stopping fishing");
    }

    public bool TryBeginStop(out Guid id)
    {
        id = Id.GetValueOrDefault();
        // CancelCatch only ends our own job, so it is safe after a lost start reply.
        if (!HasOperation || IsFinished || !(StopRequested || IsAbandoned) || _stopAccepted ||
            (_lastStopAttempt is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(2)))
            return false;

        _lastStopAttempt = _clock.GetTimestamp();
        return true;
    }

    public void ObserveStop(Guid id, bool accepted)
    {
        if (Id == id)
            _stopAccepted |= accepted;
    }
}
