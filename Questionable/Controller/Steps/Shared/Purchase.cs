// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
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

            return [new BuyTask(step.ItemId!.Value, Math.Max(1, step.ItemCount.GetValueOrDefault(1)))];
        }
    }

    internal sealed record BuyTask(uint ItemId, int ItemCount) : ITask
    {
        public override string ToString() => $"Buy({ItemCount}x {ItemId})";
    }

    internal sealed class DoBuy(KnightshopperIpc knightshopperIpc, ILogger<DoBuy> logger)
        : TaskExecutor<BuyTask>, IStoppableTaskExecutor
    {
        private Guid _operationId = Guid.Empty;

        protected override bool Start()
        {
            if (GetOwnedCount() >= Task.ItemCount)
                return false;

            if (!knightshopperIpc.IsReady())
                throw new TaskException("Knightshopper is required for buying quest items, but isn't installed, enabled or up to date");

            KnightshopperIpc.EStartResult result =
                knightshopperIpc.BuyWithGil(Task.ItemId, (uint)Task.ItemCount, out _operationId, out string message);
            return result switch
            {
                KnightshopperIpc.EStartResult.Started => true,
                KnightshopperIpc.EStartResult.NotAllowed => throw new TaskException(
                    "Knightshopper blocked the purchase. Enable its IPC permissions and add Questionable to its allowed plugins."),
                var _ => throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({result}): {message}")
            };
        }

        public override ETaskResult Update()
        {
            (KnightshopperIpc.EPurchaseState state, string message) = knightshopperIpc.GetStatus(_operationId);
            switch (state)
            {
                case KnightshopperIpc.EPurchaseState.Succeeded:
                    return ETaskResult.TaskComplete;

                case KnightshopperIpc.EPurchaseState.Failed:
                case KnightshopperIpc.EPurchaseState.Cancelled:
                    throw new TaskException($"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({state}): {message}");

                default:
                    return ETaskResult.StillRunning;
            }
        }

        public void StopNow()
        {
            if (_operationId == Guid.Empty)
                return;

            logger.LogInformation("Cancelling Knightshopper purchase {OperationId}", _operationId);
            knightshopperIpc.Cancel(_operationId);
        }

        // Knightshopper handles travel and the shop windows
        public override bool ShouldInterruptOnDamage() => false;

        private unsafe int GetOwnedCount()
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            return inventoryManager == null ? 0 : inventoryManager->GetInventoryItemCount(Task.ItemId);
        }
    }
}
