using FFXIVClientStructs.FFXIV.Client.Game;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Controller.Steps.Shared;

// Knightshopper chooses the vendor and travels for gil and tomestone purchases.
internal static class Purchase
{
    // Knightshopper's currency ids, from the IPC docs it ships with.
    internal const int Gil = 2;
    internal const int Tomestone = 7;

    // Null leaves the purchase to the quest path: special shops that take anything but tomestones.
    public static int? GetCurrency(QuestStep step, GilShopData gilShopData, TomestoneShopData tomestoneShopData)
    {
        if (step is not { InteractionType: EInteractionType.PurchaseItem, ItemId: { } itemId })
            return null;

        if (step.PurchaseMenu?.ExcelSheet == "SpecialShop")
            return step.PurchaseMenu.Key is { Type: ExcelRef.EType.RowId } shop &&
                   tomestoneShopData.IsSoldForTomestones(shop.AsRowId(), itemId)
                ? Tomestone
                : null;

        return gilShopData.IsSoldForGil(itemId) ? Gil : null;
    }

    public static bool IsHandledByKnightshopper(QuestStep step, GilShopData gilShopData,
        TomestoneShopData tomestoneShopData) =>
        GetCurrency(step, gilShopData, tomestoneShopData) != null;

    internal sealed class Factory(GilShopData gilShopData, TomestoneShopData tomestoneShopData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (GetCurrency(step, gilShopData, tomestoneShopData) is not { } currency)
                return [];

            return [new BuyTask(step.ItemId!.Value, Math.Max(1, step.ItemCount.GetValueOrDefault(1)), step.ItemQuality,
                currency)];
        }
    }

    internal sealed record BuyTask(uint ItemId, int ItemCount, EItemQuality Quality = EItemQuality.Any,
        int Currency = Gil) : ITask
    {
        public override string ToString() => $"Buy({ItemCount}x {ItemId})";
    }

    internal sealed class BuyExecutor : TaskExecutor<BuyTask>, IStoppable
    {
        private readonly KnightshopperIpc _knightshopperIpc;
        private readonly PurchaseState _state;
        private readonly MovementController _movementController;
        private Guid? _operationId;
        private long? _completedAt;

        public BuyExecutor(KnightshopperIpc knightshopperIpc, PurchaseState state,
            MovementController movementController)
        {
            _knightshopperIpc = knightshopperIpc;
            _state = state;
            _movementController = movementController;
        }

        protected override bool Start()
        {
            _operationId = null;
            _completedAt = null;
            if (_state.HasOperation)
                throw new TaskException(_state.BlockReason!);
            if (CountInInventory(Task) >= Task.ItemCount)
                return false;

            if (Task.Quality == EItemQuality.HQ)
                throw new TaskException("A shop cannot supply the required HQ quest item");

            if (!_knightshopperIpc.IsReady())
                throw new NeedsPlayerException("Knightshopper is required for buying quest items, but isn't installed, enabled or up to date");

            if (_knightshopperIpc.GetBusy() != false)
                throw new NeedsPlayerException("Knightshopper is busy or unavailable; finish its current work before buying quest items");

            // Drop pending Questionable paths before the provider takes over navigation.
            _movementController.Stop();
            KnightshopperIpc.EStartResult result =
                _knightshopperIpc.Buy(Task.Currency, Task.ItemId, (uint)Task.ItemCount, out _, out string message);
            if (result is KnightshopperIpc.EStartResult.Started or KnightshopperIpc.EStartResult.Uncertain)
                _operationId = _state.Id;
            return result switch
            {
                KnightshopperIpc.EStartResult.Started => true,
                KnightshopperIpc.EStartResult.EmptyList when CountInInventory(Task) >= Task.ItemCount => false,
                KnightshopperIpc.EStartResult.NotAllowed => throw new TaskException(
                    "Knightshopper blocked the purchase. Enable its IPC permissions and add Questionable to its allowed plugins."),
                var _ => throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({result}): {message}")
            };
        }

        public override ETaskResult Update()
        {
            if (!_operationId.HasValue || _state.Id != _operationId || _state.NeedsConfirmation)
                throw new TaskException(_state.BlockReason ?? "Knightshopper is no longer buying for Questionable");

            ExternalOperationStatus status = _state.Status;
            switch (status)
            {
                case ExternalOperationStatus.Succeeded:
                    if (CountInInventory(Task) >= Task.ItemCount)
                    {
                        _state.TryClearFinished(_operationId);
                        return ETaskResult.TaskComplete;
                    }

                    // Allow the final inventory update to arrive after the provider's result.
                    _completedAt ??= TimeProvider.System.GetTimestamp();
                    if (TimeProvider.System.GetElapsedTime(_completedAt.Value) < TimeSpan.FromSeconds(2))
                        return ETaskResult.StillRunning;
                    _state.TryClearFinished(_operationId);
                    throw new TaskException($"Knightshopper finished, but the inventory does not contain {Task.ItemCount}x {Task.ItemId}");

                case ExternalOperationStatus.Failed:
                case ExternalOperationStatus.Cancelled:
                    string message = _state.Message;
                    _state.TryClearFinished(_operationId);
                    throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({status}): {message}");

                default:
                    return ETaskResult.StillRunning;
            }
        }

        public void StopNow()
        {
            if (_operationId.HasValue && _state.Id == _operationId)
                _knightshopperIpc.CancelWork();
        }

        public override bool ShouldInterruptOnDamage() => false;

        private static unsafe int CountInInventory(BuyTask task)
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                throw new TaskException("Inventory is unavailable; cannot verify the purchase");

            return CountItems(task.Quality,
                inventoryManager->GetInventoryItemCount(task.ItemId, isHq: false, checkEquipped: false),
                inventoryManager->GetInventoryItemCount(task.ItemId, isHq: true, checkEquipped: false));
        }
    }

    internal static int CountItems(EItemQuality quality, int normal, int high) => quality switch
    {
        EItemQuality.NQ => normal,
        EItemQuality.HQ => high,
        _ => normal + high,
    };
}
