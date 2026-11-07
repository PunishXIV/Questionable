using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class GatherChillIpc : Ipc, IDisposable
{
    internal const string CallerName = "Questionable";

    private const int MinimumApiVersion = 3;

    private readonly ILogger<GatherChillIpc> _logger;
    private readonly GatheringState _operation;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("ApiVersion")] private readonly Func<int> _apiVersion = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("CanGather")] private readonly Func<uint, bool> _canGather = null!;
    [EzIPC("StartJob")] private readonly Func<string, uint, int, (int, Guid, string)> _startJob = null!;
    [EzIPC("GetJob")] private readonly Func<Guid, (int, int, int, string)> _getJob = null!;
    [EzIPC("CancelJob")] private readonly Func<string, Guid, bool> _cancelJob = null!;

    public GatherChillIpc(ILogger<GatherChillIpc> logger, GatheringState operation)
    {
        _logger = logger;
        _operation = operation;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "GatherChill";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    // Older providers can still be moving the character even though we cannot submit jobs to them.
    public bool IsAvailable() => Read(() => _apiVersion() > 0) == true;
    public override bool IsReady() => Read(() => _apiVersion() >= MinimumApiVersion) == true;
    public bool? GetBusy() => Read(_isBusy);
    public bool CanGather(uint itemId) => Read(() => _canGather(itemId)) == true;

    public GatherStartResult StartRequest(uint itemId, int additionalCount, out string message)
    {
        message = "";
        if (_operation.HasOperation)
            return GatherStartResult.Busy;
        if (itemId == 0 || additionalCount <= 0)
            return GatherStartResult.Invalid;
        if (!IsReady())
            return GatherStartResult.NotReady;
        if (GetBusy() != false)
            return GatherStartResult.Busy;

        Guid id = _operation.BeginRequest();
        try
        {
            _logger.LogInformation("Asking Gather n Chill for {Count} more of {ItemId}", additionalCount, itemId);
            (int result, Guid jobId, message) = _startJob(CallerName, itemId, additionalCount);
            if (result == (int)GatherStartResult.Started && jobId != Guid.Empty)
            {
                _operation.AcceptRequest(id, jobId);
                return GatherStartResult.Started;
            }

            // Gather n Chill's refusals use the same numbers as GatherStartResult.
            if (result != (int)GatherStartResult.Started && Enum.IsDefined((GatherStartResult)result) &&
                result >= (int)GatherStartResult.Busy)
            {
                _operation.RejectRequest(id);
                return (GatherStartResult)result;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to start a Gather n Chill job");
            message = e.Message;
        }

        _operation.MarkUncertain(id);
        return GatherStartResult.Uncertain;
    }

    public void PollOperation()
    {
        if (_operation.Id is not { } id)
            return;
        if (_operation.ProviderId is { } jobId)
        {
            var (state, message) = GetJob(jobId);
            _operation.ObserveJob(id, state, message);
        }
        if (_operation.IsAbandoned)
            CancelWork();
        if (_operation.NeedsConfirmation)
            ObserveIdle();
    }

    public void CancelWork()
    {
        if (!_operation.HasOperation)
            return;
        _operation.Abandon();
        if (!_operation.TryBeginCancel(out Guid jobId))
            return;
        try
        {
            _logger.LogInformation("Cancelling Gather n Chill job {JobId}", jobId);
            _operation.ObserveCancel(jobId, _cancelJob(CallerName, jobId));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to cancel Gather n Chill job {JobId}", jobId);
        }
    }

    public void ObserveIdle() => _operation.ObserveIdle(IsReady() ? GetBusy() : null);

    private (ExternalOperationStatus State, string Message) GetJob(Guid jobId)
    {
        try
        {
            (int state, int _, int _, string message) = _getJob(jobId);
            return ((ExternalOperationStatus)state, message);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to read Gather n Chill job {JobId}", jobId);
            return (ExternalOperationStatus.Unknown, e.Message);
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

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}

internal enum GatherStartResult
{
    Uncertain = -1,
    Started = 0,
    Busy = 1,
    Unsupported = 2,
    Invalid = 3,
    NotReady = 4,
}

[RegisterSingleton<IOperationProvider, GatherChillProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class GatherChillProvider(GatherChillIpc gatherChillIpc, GatheringState operation)
    : PluginOperationProvider(operation)
{
    public override int Order => 300;
    public override string Name => "Gather n Chill";
    protected override string CancelLabel => "gathering";
    public override bool HandlesLeadInTravel => true;

    protected override bool MatchesExecutor(ITaskExecutor? executor) => executor is Gather.StartGathering;
    protected override void PollOperation() => gatherChillIpc.PollOperation();
    protected override void CancelWork() => gatherChillIpc.CancelWork();
    protected override void ObserveIdle() => gatherChillIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = gatherChillIpc.GetBusy();
        return busy == true || (busy == null && gatherChillIpc.IsAvailable());
    }

    // Gathering always goes through Gather n Chill; steps naming a quest-specific node (e.g. 3833) still walk there.
    public override bool HandlesTravel(QuestStep step) => step is { InteractionType: EInteractionType.Gather, DataId: null };

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Gather n Chill before restarting."),
        _L("Gathering quest items with Gather n Chill"),
        _L("I stopped Gather n Chill"),
        _L("Finish or stop gathering in Gather n Chill before confirming."),
        _L("Gather n Chill must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Gather n Chill"), _L("Gathering"), _L("Stopping gathering"));

    public override string? GetStartBlockReason() =>
        gatherChillIpc.IsAvailable() && gatherChillIpc.GetBusy() != false
            ? _L("Gather n Chill is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}

[RegisterSingleton]
internal sealed class GatheringState(TimeProvider? timeProvider = null)
    : PluginOperationState("Gather n Chill", "gathering quest items", timeProvider)
{
    public void ObserveJob(Guid id, ExternalOperationStatus status, string message) =>
        ObserveStatus(id, status, message);
}

[RegisterSingleton]
internal sealed class GatheringController(
    GatherChillIpc gatherChillIpc,
    GatheringState operation,
    MovementController movementController)
{
    public enum EStatus
    {
        Gathering,
        Complete,
    }

    private Request? _request;

    public bool Start(Request request, out Guid? id)
    {
        id = null;
        if (operation.HasOperation)
            throw new TaskException(operation.BlockReason!);
        if (request.ItemId == 0 || request.Quantity <= 0 || request.Collectability > short.MaxValue)
            throw new TaskException("Invalid gathering item, quantity or collectability");
        int remaining = request.Quantity - GetItemCount(request);
        if (remaining <= 0)
            return false;

        if (!gatherChillIpc.IsReady())
            throw new TaskException("Gather n Chill is required for gathering, but isn't installed, enabled or up to date");
        if (gatherChillIpc.GetBusy() != false)
            throw new TaskException("Gather n Chill is busy or unavailable; finish its current work before gathering quest items");
        if (!gatherChillIpc.CanGather(request.ItemId))
            throw new TaskException($"Gather n Chill can't gather item {request.ItemId}");

        movementController.Stop();
        _request = request;
        GatherStartResult result = gatherChillIpc.StartRequest(request.ItemId, remaining, out string message);
        if (result is GatherStartResult.Started or GatherStartResult.Uncertain)
            id = operation.Id;
        if (result != GatherStartResult.Started)
            throw new TaskException($"Gather n Chill didn't start gathering {request.ItemId} ({result}): {message}");
        return true;
    }

    public EStatus Update(Guid? id)
    {
        if (!id.HasValue || id != operation.Id || _request == null)
            throw new TaskException("Gather n Chill is no longer running Questionable's request");
        if (operation.NeedsConfirmation || operation.IsAbandoned)
            throw new TaskException(operation.BlockReason!);
        if (!operation.IsFinished)
            return EStatus.Gathering;

        Request request = _request;
        ExternalOperationStatus status = operation.Status;
        string message = operation.Message;
        bool satisfied = status == ExternalOperationStatus.Succeeded && GetItemCount(request) >= request.Quantity;
        operation.TryClearFinished(id);
        _request = null;
        if (satisfied)
            return EStatus.Complete;

        throw new TaskException($"Gather n Chill ended ({status}) without completing {request.Quantity}x {request.ItemId} at collectability {request.Collectability}: {message}");
    }

    public void Stop(string label) => gatherChillIpc.CancelWork();

    public void StopIfCurrent(Guid? id)
    {
        if (id.HasValue && id == operation.Id)
            gatherChillIpc.CancelWork();
    }

    public IList<string> GetRemainingTaskNames() =>
        operation.HasOperation && _request is { } request
            ? [$"Gather n Chill: {request.Quantity}x {request.ItemId}"]
            : [];

    private static unsafe int GetItemCount(Request request)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            throw new TaskException("Inventory is unavailable; cannot verify gathering");

        int collectables = inventoryManager->GetInventoryItemCount(request.ItemId + 500_000,
            checkEquipped: false, minCollectability: (short)request.Collectability);
        if (request.Collectability > 0)
            return collectables;

        return collectables + inventoryManager->GetInventoryItemCount(request.ItemId, checkEquipped: false) +
            inventoryManager->GetInventoryItemCount(request.ItemId, isHq: true, checkEquipped: false);
    }

    public sealed record Request(uint ItemId, int Quantity, ushort Collectability = 0);
}
