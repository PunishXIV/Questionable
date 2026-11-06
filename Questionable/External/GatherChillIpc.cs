using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton<IGatherChillIpc, GatherChillIpc>]
internal sealed class GatherChillIpc(IDalamudPluginInterface pluginInterface, ILogger<GatherChillIpc> logger,
    GatheringOperationState operation) : Ipc, IGatherChillIpc
{
    internal const string Owner = "Questionable";

    private const int MinimumApiVersion = 3;

    private readonly ICallGateSubscriber<int> _apiVersion = pluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
    private readonly ICallGateSubscriber<uint, bool> _canGather = pluginInterface.GetIpcSubscriber<uint, bool>("GatherChill.CanGather");

    private readonly ICallGateSubscriber<string, uint, int, (int, Guid, string)> _startJob =
        pluginInterface.GetIpcSubscriber<string, uint, int, (int, Guid, string)>("GatherChill.StartJob");

    private readonly ICallGateSubscriber<Guid, (int, int, int, string)> _getJob =
        pluginInterface.GetIpcSubscriber<Guid, (int, int, int, string)>("GatherChill.GetJob");

    private readonly ICallGateSubscriber<string, Guid, bool> _cancelJob =
        pluginInterface.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob");

    public override string InternalName => "GatherChill";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    // Older providers can still be moving the character even though we cannot submit jobs to them.
    public bool IsAvailable() => Read(() => _apiVersion.InvokeFunc() > 0) == true;
    public override bool IsReady() => Read(() => _apiVersion.InvokeFunc() >= MinimumApiVersion) == true;
    public bool? GetBusy() => Read(_isBusy.InvokeFunc);
    public bool CanGather(uint itemId) => Read(() => _canGather.InvokeFunc(itemId)) == true;

    public GatherStartResult StartRequest(uint itemId, int additionalCount, out string message)
    {
        message = "";
        if (operation.HasOperation)
            return GatherStartResult.Busy;
        if (itemId == 0 || additionalCount <= 0)
            return GatherStartResult.Invalid;
        if (!IsReady())
            return GatherStartResult.NotReady;
        if (GetBusy() != false)
            return GatherStartResult.Busy;
        
        Guid owner = operation.BeginSubmission();
        try
        {
            logger.LogInformation("Asking Gather n Chill for {Count} more of {ItemId}", additionalCount, itemId);
            (int result, Guid jobId, message) = _startJob.InvokeFunc(Owner, itemId, additionalCount);
            if (result == (int)GatherStartResult.Started && jobId != Guid.Empty)
            {
                operation.AcceptSubmission(owner, jobId);
                return GatherStartResult.Started;
            }

            // Gather n Chill's refusals use the same numbers as GatherStartResult.
            if (result != (int)GatherStartResult.Started && Enum.IsDefined((GatherStartResult)result) &&
                result >= (int)GatherStartResult.Busy)
            {
                operation.RejectSubmission(owner);
                return (GatherStartResult)result;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to start a Gather n Chill job");
            message = e.Message;
        }

        operation.MarkUncertain(owner);
        return GatherStartResult.Uncertain;
    }

    public void PollOperation()
    {
        if (operation.OperationId is not { } owner)
            return;
        if (operation.ProviderOperationId is { } jobId)
        {
            var (state, message) = GetJob(jobId);
            operation.ObserveStatus(owner, state, message);
        }
        if (operation.IsAbandoned)
            CancelOwned();
        if (operation.RequiresReconciliation)
            ObserveIdle();
    }

    public void CancelOwned()
    {
        if (!operation.HasOperation)
            return;
        operation.AbandonCurrent();
        if (!operation.TryBeginCancellation(out Guid jobId))
            return;
        try
        {
            logger.LogInformation("Cancelling Gather n Chill job {JobId}", jobId);
            operation.ObserveCancellation(jobId, _cancelJob.InvokeFunc(Owner, jobId));
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to cancel Gather n Chill job {JobId}", jobId);
        }
    }

    public void ObserveIdle() => operation.ObserveIdle(IsReady() ? GetBusy() : null);

    private (ExternalOperationStatus State, string Message) GetJob(Guid jobId)
    {
        try
        {
            (int state, int _, int _, string message) = _getJob.InvokeFunc(jobId);
            return ((ExternalOperationStatus)state, message);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to read Gather n Chill job {JobId}", jobId);
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
}

internal interface IGatherChillIpc
{
    bool IsAvailable();
    bool IsReady();
    bool? GetBusy();
    bool CanGather(uint itemId);
    GatherStartResult StartRequest(uint itemId, int additionalCount, out string message);
    void PollOperation();
    void CancelOwned();
    void ObserveIdle();
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
