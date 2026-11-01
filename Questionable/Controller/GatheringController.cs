using FFXIVClientStructs.FFXIV.Client.Game;

namespace Questionable.Controller;

/// <summary>Verifies transient gathering without editing the provider's saved list.</summary>
[RegisterSingleton]
internal sealed class GatheringController
{
    public enum EStatus
    {
        Gathering,
        Complete,
    }

    private readonly IGatherChillIpc _gatherChillIpc;
    private readonly GatheringOperationState _operation;
    private readonly Func<GatheringRequest, int> _countItems;
    private readonly Action _stopMovement;
    private GatheringRequest? _currentRequest;

    public GatheringController(IGatherChillIpc gatherChillIpc, GatheringOperationState operation,
        MovementController movementController)
        : this(gatherChillIpc, operation, GetOwnedCount, movementController.Stop)
    {
    }

    internal GatheringController(IGatherChillIpc gatherChillIpc, GatheringOperationState operation,
        Func<GatheringRequest, int> countItems, Action stopMovement)
    {
        _gatherChillIpc = gatherChillIpc;
        _operation = operation;
        _countItems = countItems;
        _stopMovement = stopMovement;
    }

    public bool Start(GatheringRequest request, out Guid? owner)
    {
        owner = null;
        if (_operation.HasOperation)
            throw new TaskException(_operation.BlockReason!);
        if (request.ItemId == 0 || request.Quantity <= 0 || request.Collectability > short.MaxValue)
            throw new TaskException("Invalid gathering item, quantity or collectability");
        int remaining = request.Quantity - _countItems(request);
        if (remaining <= 0)
            return false;

        if (!_gatherChillIpc.IsReady())
            throw new TaskException("Gather n Chill is required for gathering, but isn't installed, enabled or up to date");
        if (_gatherChillIpc.GetBusy() != false)
            throw new TaskException("Gather n Chill is busy or unavailable; finish its current work before gathering quest items");
        if (!_gatherChillIpc.CanGather(request.ItemId))
            throw new TaskException($"Gather n Chill can't gather item {request.ItemId}");

        _stopMovement();
        _currentRequest = request;
        GatherStartResult result = _gatherChillIpc.StartRequest(request.ItemId, remaining, out string message);
        if (result is GatherStartResult.Started or GatherStartResult.Uncertain)
            owner = _operation.OperationId;
        if (result != GatherStartResult.Started)
            throw new TaskException($"Gather n Chill didn't start gathering {request.ItemId} ({result}): {message}");
        return true;
    }

    public EStatus Update(Guid? owner)
    {
        if (!owner.HasValue || owner != _operation.OperationId || _currentRequest == null)
            throw new TaskException("Gather n Chill request ownership was lost");
        if (_operation.RequiresReconciliation || _operation.IsAbandoned)
            throw new TaskException(_operation.BlockReason!);
        if (!_operation.IsTerminal)
            return EStatus.Gathering;

        GatheringRequest request = _currentRequest;
        ExternalOperationStatus status = _operation.Status;
        string message = _operation.Message;
        bool satisfied = status == ExternalOperationStatus.Succeeded && _countItems(request) >= request.Quantity;
        _operation.TryReleaseTerminal(owner);
        _currentRequest = null;
        if (satisfied)
            return EStatus.Complete;

        throw new TaskException($"Gather n Chill ended ({status}) without completing {request.Quantity}x {request.ItemId} at collectability {request.Collectability}: {message}");
    }

    public void Stop(string label) => _gatherChillIpc.CancelOwned();

    public void StopOwned(Guid? owner)
    {
        if (owner.HasValue && owner == _operation.OperationId)
            _gatherChillIpc.CancelOwned();
    }

    public IList<string> GetRemainingTaskNames() =>
        _operation.HasOperation && _currentRequest is { } request
            ? [$"Gather n Chill: {request.Quantity}x {request.ItemId}"]
            : [];

    private static unsafe int GetOwnedCount(GatheringRequest request)
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

    public sealed record GatheringRequest(uint ItemId, int Quantity, ushort Collectability = 0);
}
