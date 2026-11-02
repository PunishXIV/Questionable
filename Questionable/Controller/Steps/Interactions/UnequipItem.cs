using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.Steps.Interactions;

internal static class UnequipItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.UnequipItem)
                return null;
            if (!step.ItemId.HasValue)
                throw new ArgumentNullException(nameof(step.ItemId));

            return new Task(step.ItemId.Value);
        }
    }

    internal sealed record Task(uint ItemId) : ITask
    {
        public override string ToString() => $"Unequip({ItemId})";
    }

    internal sealed class DoUnequip
    (
        IDataManager dataManager,
        ILogger<DoUnequip> logger) : TaskExecutor<Task>, IToastAware
    {
        private const int MaxAttempts = 3;

        private static readonly InventoryType[] ArmoryInventoryTypes =
        [
            InventoryType.ArmoryMainHand,
            InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead,
            InventoryType.ArmoryBody,
            InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets,
            InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings,
        ];

        private int _attempts;
        private DateTime _continueAt = DateTime.MaxValue;
        private Item? _item;
        private List<ushort> _targetSlots = null!;

        public override unsafe ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return ETaskResult.StillRunning;

            if (!EquipmentSlots.IsEquipped(inventoryManager, _targetSlots, Task.ItemId))
                return ETaskResult.TaskComplete;

            Unequip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        public bool OnErrorToast(SeString message)
        {
            if (EquipmentSlots.IsArmoryFullToast(dataManager, message))
                _attempts = MaxAttempts;

            return false;
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            _item = dataManager.GetExcelSheet<Item>().GetRowOrDefault(Task.ItemId) ??
                    throw new ArgumentOutOfRangeException(nameof(Task.ItemId));
            _targetSlots = EquipmentSlots.For(_item.Value) ?? throw new InvalidOperationException("Not a piece of equipment");

            if (GetArmoryInventoryType(_item.Value) == null)
                throw new InvalidOperationException("Item has no armory destination");

            Unequip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        private unsafe void Unequip()
        {
            ++_attempts;
            if (_attempts > MaxAttempts)
                throw new TaskException("Unable to unequip gear.");

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            if (!EquipmentSlots.IsEquipped(inventoryManager, _targetSlots, Task.ItemId))
            {
                logger.LogInformation("Already unequipped {Item}, skipping step", _item?.Name.ToString());
                return;
            }

            InventoryType armoryType = GetArmoryInventoryType(_item!.Value)!.Value;
            InventoryContainer* armoryContainer = inventoryManager->GetInventoryContainer(armoryType);
            if (armoryContainer == null)
                return;

            foreach (ushort equippedSlot in _targetSlots)
            {
                InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, equippedSlot);
                if (itemSlot == null || itemSlot->ItemId != Task.ItemId)
                    continue;

                if (!EquipmentSlots.TryFindFirstEmptySlot(armoryContainer, out ushort targetSlot))
                {
                    logger.LogWarning("Armory container {ArmoryType} is full, cannot unequip item {ItemId}",
                        armoryType, Task.ItemId);
                    throw new TaskException("Unable to unequip gear - armory chest is full.");
                }

                logger.LogInformation(
                    "Unequipping item from {SourceInventory}, {SourceSlot} to {TargetInventory}, {TargetSlot}",
                    InventoryType.EquippedItems, equippedSlot, armoryType, targetSlot);

                int result = inventoryManager->MoveItemSlot(InventoryType.EquippedItems, equippedSlot,
                    armoryType, targetSlot, a6: true); // TODO define a6
                logger.LogInformation("MoveItemSlot result: {Result}", result);
                return;
            }
        }

        private static InventoryType? GetArmoryInventoryType(Item item) =>
            item.EquipSlotCategory.RowId switch
            {
                >= 1 and <= 11 => ArmoryInventoryTypes[item.EquipSlotCategory.RowId - 1],
                12 => InventoryType.ArmoryRings,
                13 => InventoryType.ArmoryMainHand,
                17 => InventoryType.ArmorySoulCrystal,
                _ => null
            };
    }
}
