using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Questionable.Controller.Steps.Interactions;

internal static class EquipmentSlots
{
    public static List<ushort>? For(Item item) =>
        item.EquipSlotCategory.RowId switch
        {
            >= 1 and <= 11 => [(ushort)(item.EquipSlotCategory.RowId - 1)],
            12 => [11, 12], // rings
            13 => [0],
            17 => [13], // soul crystal
            _ => null
        };

    public static unsafe bool IsEquipped(InventoryManager* inventoryManager, List<ushort> slots, uint itemId)
    {
        foreach (ushort slot in slots)
        {
            InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, slot);
            if (itemSlot != null && itemSlot->ItemId == itemId)
                return true;
        }

        return false;
    }

    public static bool IsArmoryFullToast(IDataManager dataManager, SeString message) =>
        GameFunctions.GameStringEquals(message.TextValue,
            dataManager.GetString<LogMessage>(709, x => x.Text));

    public static unsafe bool TryFindFirstEmptySlot(InventoryContainer* container, out ushort slot)
    {
        for (ushort i = 0; i < container->Size; i++)
        {
            InventoryItem* itemSlot = container->GetInventorySlot(i);
            if (itemSlot == null || itemSlot->ItemId == 0)
            {
                slot = i;
                return true;
            }
        }

        slot = 0;
        return false;
    }

    internal abstract class GearExecutor<TTask>(IDataManager dataManager) : TaskExecutor<TTask>, IToastAware
    where TTask : class, ITask
    {
        private const int MaxAttempts = 3;

        private int _attempts;
        private DateTime _continueAt = DateTime.MaxValue;

        protected Item Item { get; private set; }
        protected List<ushort> TargetSlots { get; private set; } = null!;
        protected abstract uint ItemId { get; }
        protected abstract string FailureText { get; }

        public override unsafe ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return ETaskResult.StillRunning;

            if (IsDone(inventoryManager))
                return ETaskResult.TaskComplete;

            TryMove();
            _continueAt = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        public bool OnErrorToast(SeString message)
        {
            if (IsArmoryFullToast(dataManager, message))
                _attempts = MaxAttempts;

            return false;
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            Item = dataManager.GetExcelSheet<Item>().GetRowOrDefault(ItemId) ??
                   throw new ArgumentOutOfRangeException(nameof(ItemId));
            TargetSlots = For(Item) ?? throw new InvalidOperationException("Not a piece of equipment");
            CheckItem(Item);

            TryMove();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        protected virtual void CheckItem(Item item)
        {
        }

        protected abstract unsafe bool IsDone(InventoryManager* inventoryManager);

        protected abstract unsafe void Move(InventoryManager* inventoryManager);

        private unsafe void TryMove()
        {
            ++_attempts;
            if (_attempts > MaxAttempts)
                throw new TaskException(FailureText);

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            Move(inventoryManager);
        }
    }
}
