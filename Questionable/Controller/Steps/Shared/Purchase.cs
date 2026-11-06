using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Controller.Steps.Shared;

// Knightshopper chooses the vendor and travels for gil purchases.
internal static class Purchase
{
    // Non-gil currencies stay with the quest path.
    public static bool IsHandledByKnightshopper(QuestStep step, GilShopData gilShopData) =>
        step is { InteractionType: EInteractionType.PurchaseItem, ItemId: { } itemId } &&
        step.PurchaseMenu?.ExcelSheet != "SpecialShop" &&
        gilShopData.IsSoldForGil(itemId);

    internal sealed class Factory(GilShopData gilShopData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!IsHandledByKnightshopper(step, gilShopData))
                return [];

            return [new BuyTask(step.ItemId!.Value, Math.Max(1, step.ItemCount.GetValueOrDefault(1)), step.ItemQuality)];
        }
    }

    internal sealed record BuyTask(uint ItemId, int ItemCount, EItemQuality Quality = EItemQuality.Any) : ITask
    {
        public override string ToString() => $"Buy({ItemCount}x {ItemId})";
    }

    internal sealed class DoBuy : TaskExecutor<BuyTask>, IStoppableTaskExecutor
    {
        private readonly KnightshopperIpc _knightshopperIpc;
        private readonly PurchaseOperationState _operation;
        private readonly MovementController _movementController;
        private Guid? _owner;
        private long? _completedAt;

        public DoBuy(KnightshopperIpc knightshopperIpc, PurchaseOperationState operation,
            MovementController movementController)
        {
            _knightshopperIpc = knightshopperIpc;
            _operation = operation;
            _movementController = movementController;
        }

        protected override bool Start()
        {
            _owner = null;
            _completedAt = null;
            if (_operation.HasOperation)
                throw new TaskException(_operation.BlockReason!);
            if (GetOwnedCount(Task) >= Task.ItemCount)
                return false;

            if (Task.Quality == EItemQuality.HQ)
                throw new TaskException("A gil shop cannot supply the required HQ quest item");

            if (!_knightshopperIpc.IsReady())
                throw new TaskException("Knightshopper is required for buying quest items, but isn't installed, enabled or up to date");

            if (_knightshopperIpc.GetBusy() != false)
                throw new TaskException("Knightshopper is busy or unavailable; finish its current work before buying quest items");

            // Drop pending Questionable paths before the provider takes over navigation.
            _movementController.Stop();
            KnightshopperIpc.EStartResult result =
                _knightshopperIpc.BuyWithGil(Task.ItemId, (uint)Task.ItemCount, out _, out string message);
            if (result is KnightshopperIpc.EStartResult.Started or KnightshopperIpc.EStartResult.Uncertain)
                _owner = _operation.OperationId;
            return result switch
            {
                KnightshopperIpc.EStartResult.Started => true,
                KnightshopperIpc.EStartResult.EmptyList when GetOwnedCount(Task) >= Task.ItemCount => false,
                KnightshopperIpc.EStartResult.NotAllowed => throw new TaskException(
                    "Knightshopper blocked the purchase. Enable its IPC permissions and add Questionable to its allowed plugins."),
                var _ => throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({result}): {message}")
            };
        }

        public override ETaskResult Update()
        {
            if (!_owner.HasValue || _operation.OperationId != _owner || _operation.RequiresReconciliation)
                throw new TaskException(_operation.BlockReason ?? "Knightshopper is no longer buying for Questionable");

            ExternalOperationStatus state = _operation.Status;
            switch (state)
            {
                case ExternalOperationStatus.Succeeded:
                    if (GetOwnedCount(Task) >= Task.ItemCount)
                    {
                        _operation.TryReleaseTerminal(_owner);
                        return ETaskResult.TaskComplete;
                    }

                    // Allow the final inventory update to arrive after the provider's result.
                    _completedAt ??= TimeProvider.System.GetTimestamp();
                    if (TimeProvider.System.GetElapsedTime(_completedAt.Value) < TimeSpan.FromSeconds(2))
                        return ETaskResult.StillRunning;
                    _operation.TryReleaseTerminal(_owner);
                    throw new TaskException($"Knightshopper finished, but the inventory does not contain {Task.ItemCount}x {Task.ItemId}");

                case ExternalOperationStatus.Failed:
                case ExternalOperationStatus.Cancelled:
                    string message = _operation.Message;
                    _operation.TryReleaseTerminal(_owner);
                    throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({state}): {message}");

                default:
                    return ETaskResult.StillRunning;
            }
        }

        public void StopNow()
        {
            if (_owner.HasValue && _operation.OperationId == _owner)
                _knightshopperIpc.CancelOwned();
        }

        public override bool ShouldInterruptOnDamage() => false;

        private static unsafe int GetOwnedCount(BuyTask task)
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
