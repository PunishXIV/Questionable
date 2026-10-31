using FFXIVClientStructs.FFXIV.Client.Game;

namespace Questionable.Controller;

/// <summary>
///     Hands gathering off to Gather n Chill and keeps track of what we asked it for, so we only stop it when we
///     started it.
/// </summary>
[RegisterSingleton]
internal sealed unsafe class GatheringController(GatherChillIpc gatherChillIpc, ILogger<GatheringController> logger)
{
    public enum EStatus
    {
        Gathering,
        Complete,
        Failed
    }

    private GatheringRequest? _currentRequest;

    public bool Start(GatheringRequest gatheringRequest)
    {
        if (!gatherChillIpc.IsReady())
            throw new TaskException("Gather n Chill is required for gathering, but isn't installed, enabled or up to date");

        if (!gatherChillIpc.CanGather(gatheringRequest.ItemId))
            throw new TaskException($"Gather n Chill can't gather item {gatheringRequest.ItemId}");

        int remaining = gatheringRequest.Quantity - GetOwnedCount(gatheringRequest);
        if (remaining <= 0)
            return false;

        if (!gatherChillIpc.GatherItem(gatheringRequest.ItemId, remaining))
            throw new TaskException($"Gather n Chill didn't start gathering item {gatheringRequest.ItemId} (busy?)");

        _currentRequest = gatheringRequest;
        return true;
    }

    public EStatus Update()
    {
        if (_currentRequest == null)
            return EStatus.Complete;

        if (gatherChillIpc.IsBusy())
            return EStatus.Gathering;

        GatheringRequest request = _currentRequest;
        _currentRequest = null;
        if (GetOwnedCount(request) >= request.Quantity)
            return EStatus.Complete;

        logger.LogWarning("Gather n Chill stopped before gathering {Quantity}x {ItemId} (collectability {Collectability})",
            request.Quantity, request.ItemId, request.Collectability);
        return EStatus.Failed;
    }

    public void Stop(string label)
    {
        if (_currentRequest == null)
            return;

        logger.LogInformation("Stopping Gather n Chill ({Label})", label);
        _currentRequest = null;
        gatherChillIpc.Stop();
    }

    public IList<string> GetRemainingTaskNames()
    {
        if (_currentRequest is { } request)
            return [$"Gather n Chill: {request.Quantity}x {request.ItemId}"];

        return [];
    }

    private static int GetOwnedCount(GatheringRequest request)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return 0;

        return inventoryManager->GetInventoryItemCount(request.ItemId,
            minCollectability: (short)request.Collectability);
    }

    public sealed record GatheringRequest
    (
        uint ItemId,
        int Quantity,
        ushort Collectability = 0);
}
