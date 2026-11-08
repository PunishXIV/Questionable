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

    internal sealed class Executor
    (
        IDataManager dataManager,
        ILogger<Executor> logger) : EquipmentSlots.GearExecutor<Task>(dataManager)
    {
        protected override uint ItemId => Task.ItemId;
        protected override string FailureText => "Unable to unequip gear.";

        protected override void CheckItem(Item item)
        {
            if (EquipmentSlots.ArmoryFor(item.EquipSlotCategory.RowId) == null)
                throw new InvalidOperationException("Item has no armory destination");
        }

        protected override unsafe bool IsDone(InventoryManager* inventoryManager) =>
            !EquipmentSlots.IsEquipped(inventoryManager, TargetSlots, Task.ItemId);

        protected override unsafe void Move(InventoryManager* inventoryManager)
        {
            if (IsDone(inventoryManager))
            {
                logger.LogInformation("Already unequipped {Item}, skipping step", Item.Name.ToString());
                return;
            }

            InventoryType armoryType = EquipmentSlots.ArmoryFor(Item.EquipSlotCategory.RowId)!.Value;
            InventoryContainer* armoryContainer = inventoryManager->GetInventoryContainer(armoryType);
            if (armoryContainer == null)
                return;

            foreach (ushort equippedSlot in TargetSlots)
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
    }
}
