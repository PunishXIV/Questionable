using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Controller.Steps.Shared;

/// <summary>
///     Gil shop purchases are handed to Knightshopper, which picks a vendor and travels there itself.
/// </summary>
internal static class Purchase
{
    /// <summary>
    ///     Steps where QST shouldn't travel to or talk to the vendor, since Knightshopper does both. Anything not sold
    ///     for gil (GC seals, tomestones, special currencies) stays with the quest path.
    /// </summary>
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
        private readonly IKnightshopperIpc _knightshopperIpc;
        private readonly PurchaseOperationState _operation;
        private readonly Func<BuyTask, int> _countItems;
        private readonly Action _stopMovement;
        private readonly TimeProvider _clock;
        private Guid? _owner;
        private long? _completedAt;

        public DoBuy(IKnightshopperIpc knightshopperIpc, PurchaseOperationState operation,
            MovementController movementController)
            : this(knightshopperIpc, operation, GetOwnedCount, movementController.Stop)
        {
        }

        internal DoBuy(IKnightshopperIpc knightshopperIpc, PurchaseOperationState operation,
            Func<BuyTask, int> countItems, Action stopMovement, TimeProvider? clock = null)
        {
            _knightshopperIpc = knightshopperIpc;
            _operation = operation;
            _countItems = countItems;
            _stopMovement = stopMovement;
            _clock = clock ?? TimeProvider.System;
        }

        protected override bool Start()
        {
            _owner = null;
            _completedAt = null;
            if (_operation.HasOperation)
                throw new TaskException(_operation.BlockReason!);
            if (_countItems(Task) >= Task.ItemCount)
                return false;

            if (Task.Quality == EItemQuality.HQ)
                throw new TaskException("A gil shop cannot supply the required HQ quest item");

            if (!_knightshopperIpc.IsReady())
                throw new TaskException("Knightshopper is required for buying quest items, but isn't installed, enabled or up to date");

            if (_knightshopperIpc.GetBusy() != false)
                throw new TaskException("Knightshopper is busy or unavailable; finish its current work before buying quest items");

            // Drop pending Questionable paths before the provider takes over navigation.
            _stopMovement();
            KnightshopperIpc.EStartResult result =
                _knightshopperIpc.BuyWithGil(Task.ItemId, (uint)Task.ItemCount, out _, out string message);
            if (result is KnightshopperIpc.EStartResult.Started or KnightshopperIpc.EStartResult.Uncertain)
                _owner = _operation.OperationId;
            return result switch
            {
                KnightshopperIpc.EStartResult.Started => true,
                KnightshopperIpc.EStartResult.EmptyList when _countItems(Task) >= Task.ItemCount => false,
                KnightshopperIpc.EStartResult.NotAllowed => throw new TaskException(
                    "Knightshopper blocked the purchase. Enable its IPC permissions and add Questionable to its allowed plugins."),
                var _ => throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({result}): {message}")
            };
        }

        public override ETaskResult Update()
        {
            if (!_owner.HasValue || _operation.OperationId != _owner || _operation.RequiresReconciliation)
                throw new TaskException(_operation.BlockReason ?? "Knightshopper purchase ownership was lost");

            ExternalOperationStatus state = _operation.Status;
            switch (state)
            {
                case ExternalOperationStatus.Succeeded:
                    if (_countItems(Task) >= Task.ItemCount)
                    {
                        _operation.TryReleaseTerminal(_owner);
                        return ETaskResult.TaskComplete;
                    }

                    // Allow the final inventory update to arrive after the provider's result.
                    _completedAt ??= _clock.GetTimestamp();
                    if (_clock.GetElapsedTime(_completedAt.Value) < TimeSpan.FromSeconds(2))
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

        // Knightshopper handles travel and the shop windows
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
