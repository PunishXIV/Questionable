using Questionable.Controller.Steps.Shared;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

// WahTools runs every IPC catch under one shared owner, so its release and stop reach other plugins' catches too.
[RegisterSingleton]
internal sealed class FeeshIpc : Ipc, IDisposable
{
    private readonly ILogger<FeeshIpc> _logger;
    private readonly FishingState _operation;
    private readonly IFramework _framework;
    private readonly object _catchDoneLock = new();
    private readonly EzIPCDisposalToken[] _disposalTokens;
    private bool? _catchDoneResult;
    private uint? _requestedFish;
    private uint? _heldFish;
    private long? _releaseTriedAt;

    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("Feesh.Catch")] private readonly Func<uint, int, bool> _catch = null!;
    [EzIPC("Feesh.CatchCollectable")] private readonly Func<uint, int, int, bool> _catchCollectable = null!;
    [EzIPC("Feesh.CatchStatus")] private readonly Func<string> _catchStatus = null!;
    [EzIPC("Feesh.Stop")] private readonly Func<bool> _stop = null!;
    [EzIPC("Feesh.ReleaseHeldFish")] private readonly Func<bool> _releaseHeldFish = null!;

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
    public bool IsAvailable() => ReadStatus() != null;
    public bool? GetBusy() => Read(_isBusy);

    // Catch takes how many more to catch, and keeps them from being discarded until released.
    public FeeshStartResult StartCatch(uint fishId, int additionalCount, int minCollectability, out string message)
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
        {
            _catchDoneResult = null;
            _requestedFish = fishId;
        }

        try
        {
            _logger.LogInformation("Requesting {Count} additional {FishId} from Feesh (collectability {Min})",
                additionalCount, fishId, minCollectability);
            if (!(minCollectability > 0
                    ? _catchCollectable(fishId, additionalCount, minCollectability)
                    : _catch(fishId, additionalCount)))
            {
                _operation.RejectRequest(id);
                return GetBusy() == true ? FeeshStartResult.Busy : FeeshStartResult.Refused;
            }

            _operation.AcceptCatch(id);
            // A release still retrying from an earlier catch would drop this one's hold.
            _releaseTriedAt = null;
            _heldFish = fishId;
            return FeeshStartResult.Started;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Feesh catch submission has an uncertain result");
            _operation.MarkUncertain(id);
            _releaseTriedAt = null;
            _heldFish = fishId;
            message = e.Message;
            return FeeshStartResult.Uncertain;
        }
    }

    public void PollOperation()
    {
        RetryRelease();
        if (_operation.Id is not { } id)
            return;

        string? text = ReadStatus();
        EFeeshJobState state = ParseState(text);
        bool? catchDone;
        lock (_catchDoneLock)
            catchDone = _catchDoneResult;
        _operation.ObserveCatch(id, state, text ?? "Feesh is unavailable", catchDone, GetBusy());
        SendStop(state);
        if (_operation.NeedsConfirmation)
            ObserveIdle();
    }

    public void RequestStop()
    {
        _operation.RequestStop();
        SendStop(ParseState(ReadStatus()));
    }

    public void CancelWork()
    {
        RetryRelease();
        if (!_operation.HasOperation)
            return;
        _operation.Abandon();
        RequestStop();
        if (_heldFish != null)
            ReleaseHeldFish();
    }

    public void ObserveIdle() => _operation.ObserveIdle(GetBusy());

    public void ReleaseHeldFish()
    {
        _heldFish = null;
        _releaseTriedAt = TimeProvider.System.GetTimestamp();
        TryRelease();
    }

    private void TryRelease()
    {
        try
        {
            // False means nothing is held any more, which also completes cleanup.
            _releaseHeldFish();
            _releaseTriedAt = null;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to release Feesh's held fish");
        }
    }

    // QuestController stops polling once the job is released, but the release can still be pending.
    private void OnFrameworkUpdate(IFramework framework) => RetryRelease();

    private void RetryRelease(bool force = false)
    {
        if (_releaseTriedAt is not { } lastAttempt)
            return;
        if (!force && TimeProvider.System.GetElapsedTime(lastAttempt) < TimeSpan.FromSeconds(2))
            return;

        _releaseTriedAt = TimeProvider.System.GetTimestamp();
        TryRelease();
    }

    // CatchStatus: "Idle", "fishing <progress>", "finishing <progress>", then "done|partial|failed|stopped <progress>[: why]".
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
            "IDLE" => EFeeshJobState.None,
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
    private void OnCatchDone(uint fishId, bool done)
    {
        // May arrive off the framework thread; the next poll applies it. Other plugins' catches report here too.
        lock (_catchDoneLock)
        {
            if (fishId == _requestedFish)
                _catchDoneResult = done;
        }
    }

    // Stop ends everything Feesh is doing, so it's only sent while a catch job is running.
    private void SendStop(EFeeshJobState state)
    {
        if (!_operation.TryBeginStop(out Guid id))
            return;
        if (state is not (EFeeshJobState.Fishing or EFeeshJobState.Finishing))
        {
            _operation.ObserveStop(id, true);
            return;
        }

        try
        {
            _operation.ObserveStop(id, _stop());
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to stop Feesh catch job {Id}", id);
        }
    }

    private string? ReadStatus()
    {
        try
        {
            return _catchStatus() ?? "";
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
        IpcInvoke.TryOnFrameworkThread(_framework, () => RetryRelease(force: true), _logger);

        // Framework unloading can skip both the final retry and the controller's cancellation.
        if (_heldFish != null || _releaseTriedAt != null)
            _logger.LogWarning("Feesh still keeps fish caught for Questionable. Release them in WahTools.");
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

    protected override bool MatchesExecutor(ITaskExecutor? executor) => executor is Fish.Executor;
    protected override void PollOperation() => feeshIpc.PollOperation();
    protected override void CancelWork() => feeshIpc.CancelWork();
    protected override void ObserveIdle() => feeshIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = feeshIpc.GetBusy();
        return busy == true || (busy == null && feeshIpc.IsAvailable());
    }

    public override bool HandlesTravel(QuestStep step) => Fish.IsHandledByFeesh(step, feeshIpc);

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Feesh before restarting."),
        _L("Fishing for quest items with Feesh"),
        _L("I stopped Feesh"),
        _L("Finish or stop fishing in Feesh before confirming."),
        _L("Feesh must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Feesh"), _L("Fishing"), _L("Stopping fishing"));

    public override string? GetStartBlockReason() =>
        feeshIpc.IsAvailable() && feeshIpc.GetBusy() != false
            ? _L("Feesh is busy or its status is unavailable. Finish its current work before starting questing.")
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
        // FeeshIpc only sends Stop while a catch job is running, so this is safe after a lost start reply.
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
