using FFXIVClientStructs.FFXIV.Client.Game;
using static Questionable.Controller.Steps.ITaskExecutor;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace Questionable.Controller.Steps.Shared;

internal static class Purchase
{
    // Knightshopper's currency ids, from the IPC docs it ships with.
    internal const int CompanySeals = 1;
    internal const int Gil = 2;
    internal const int Tomestone = 7;

    // Null leaves the purchase to the quest path: special shops that take anything but tomestones.
    public static int? GetCurrency(QuestStep step, GilShopData gilShopData, TomestoneShopData tomestoneShopData,
        SealShopData sealShopData)
    {
        if (step is not { InteractionType: EInteractionType.PurchaseItem, ItemId: { } itemId })
            return null;

        if (step.PurchaseMenu?.ExcelSheet == "SpecialShop")
            return step.PurchaseMenu.Key is { Type: ExcelRef.EType.RowId } shop &&
                   tomestoneShopData.IsSoldForTomestones(shop.AsRowId(), itemId)
                ? Tomestone
                : null;

        if (gilShopData.IsSoldForGil(itemId))
            return Gil;
        return sealShopData.IsSoldForSeals(itemId) ? CompanySeals : null;
    }

    public static bool IsHandledByKnightshopper(QuestStep step, GilShopData gilShopData,
        TomestoneShopData tomestoneShopData, SealShopData sealShopData) =>
        GetCurrency(step, gilShopData, tomestoneShopData, sealShopData) != null;

    internal sealed class Factory(GilShopData gilShopData, TomestoneShopData tomestoneShopData,
        SealShopData sealShopData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (GetCurrency(step, gilShopData, tomestoneShopData, sealShopData) is not { } currency)
                return [];

            return [new BuyTask(step.ItemId!.Value, Math.Max(1, step.ItemCount.GetValueOrDefault(1)), step.ItemQuality,
                currency)];
        }
    }

    internal sealed record BuyTask(uint ItemId, int ItemCount, EItemQuality Quality = EItemQuality.Any,
        int Currency = Gil, bool Optional = false) : ITask
    {
        public override string ToString() => $"Buy({ItemCount}x {ItemId})";
    }

    internal sealed class BuyExecutor : TaskExecutor<BuyTask>, IStoppable
    {
        private readonly KnightshopperIpc _knightshopperIpc;
        private readonly PurchaseState _state;
        private readonly MovementController _movementController;
        private readonly IChatGui? _chatGui;
        private readonly IDataManager? _dataManager;
        private readonly ILogger<BuyExecutor>? _logger;
        private Guid? _operationId;
        private long? _completedAt;

        public BuyExecutor(KnightshopperIpc knightshopperIpc, PurchaseState state,
            MovementController movementController, IChatGui? chatGui = null, IDataManager? dataManager = null,
            ILogger<BuyExecutor>? logger = null)
        {
            _knightshopperIpc = knightshopperIpc;
            _state = state;
            _movementController = movementController;
            _chatGui = chatGui;
            _dataManager = dataManager;
            _logger = logger;
        }

        protected override bool Start()
        {
            _operationId = null;
            _completedAt = null;
            try
            {
                return StartBuying();
            }
            catch (TaskException e) when (Task.Optional)
            {
                Skip(e);
                return false;
            }
        }

        public override ETaskResult Update()
        {
            try
            {
                return UpdateBuying();
            }
            catch (TaskException e) when (Task.Optional && !_state.NeedsConfirmation)
            {
                StopNow();
                Skip(e);
                return ETaskResult.TaskComplete;
            }
        }

        private string ItemName() =>
            _dataManager?.GetExcelSheet<LuminaItem>().GetRowOrDefault(Task.ItemId)?.Name.ExtractText()
            ?? Task.ItemId.ToString(CultureInfo.InvariantCulture);

        private void Skip(TaskException e)
        {
            string item = ItemName();
            _logger?.LogWarning("Skipped buying {Count}x {Item}: {Reason}", Task.ItemCount, item, e.Message);
            _chatGui?.Print(_LF("Skipped buying {0}: {1}", item, e.Message), CommandHandler.MessageTag,
                CommandHandler.TagColor);
        }

        private bool StartBuying()
        {
            if (_state.HasOperation)
                throw new TaskException(_state.BlockReason!);
            int owned = CountInInventory(Task);
            if (owned >= Task.ItemCount)
                return false;

            if (Task.Quality == EItemQuality.HQ)
                throw new TaskException("A shop cannot supply the required HQ quest item");

            if (Task.Currency == Gil)
                CheckGil(Task.ItemCount - owned);

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
                KnightshopperIpc.EStartResult.InvalidCurrency when Task.Currency == CompanySeals => throw new TaskException(
                    "This Knightshopper version can't buy with company seals. Update Knightshopper, then restart Questionable."),
                var _ => throw new TaskException(string.Create(CultureInfo.InvariantCulture, $"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({result}): {message}"))
            };
        }

        private ETaskResult UpdateBuying()
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
                    throw new TaskException(string.Create(CultureInfo.InvariantCulture, $"Knightshopper finished, but the inventory does not contain {Task.ItemCount}x {Task.ItemId}"));

                case ExternalOperationStatus.Failed:
                case ExternalOperationStatus.Cancelled:
                    string message = _state.Message;
                    _state.TryClearFinished(_operationId);
                    throw new TaskException(string.Create(CultureInfo.InvariantCulture, $"Knightshopper didn't buy {Task.ItemCount}x {Task.ItemId} ({status}): {message}"));

                default:
                    return ETaskResult.StillRunning;
            }
        }

        // Knightshopper reports a purchase it couldn't afford as finished, which hides the reason.
        private unsafe void CheckGil(int count)
        {
            uint price = _dataManager?.GetExcelSheet<LuminaItem>().GetRowOrDefault(Task.ItemId)?.PriceMid ?? 0;
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (price == 0 || inventoryManager == null)
                return;

            if (GilShortage(price, count, inventoryManager->GetGil(), ItemName()) is { } reason)
                throw new NeedsPlayerException(reason);
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

            // Bought gear can be equipped as an upgrade right away; it still counts as bought.
            return CountItems(task.Quality,
                inventoryManager->GetInventoryItemCount(task.ItemId, isHq: false, checkEquipped: true),
                inventoryManager->GetInventoryItemCount(task.ItemId, isHq: true, checkEquipped: true));
        }
    }

    internal static string? GilShortage(uint price, int count, uint gil, string item)
    {
        long need = (long)price * count;
        return need > gil ? string.Create(CultureInfo.InvariantCulture, $"Not enough gil to buy {count}x {item}: it costs {need} gil and you have {gil}.") : null;
    }

    internal static int CountItems(EItemQuality quality, int normal, int high) => quality switch
    {
        EItemQuality.NQ => normal,
        EItemQuality.HQ => high,
        _ => normal + high,
    };
}
