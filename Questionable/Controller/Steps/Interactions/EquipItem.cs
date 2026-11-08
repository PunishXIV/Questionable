using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.Steps.Interactions;

internal static class EquipItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.EquipItem)
                return null;
            if (!step.ItemId.HasValue)
                throw new ArgumentNullException(nameof(step.ItemId));

            return new Task(step.ItemId.Value);
        }
    }

    internal sealed record Task(uint ItemId) : ITask
    {
        public override string ToString() => $"Equip({ItemId})";
    }

    internal sealed class Executor
    (
        IDataManager dataManager,
        ILogger<Executor> logger) : EquipmentSlots.GearExecutor<Task>(dataManager)
    {
        private static readonly IReadOnlyList<InventoryType> SourceInventoryTypes =
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

            InventoryType.ArmorySoulCrystal,

            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4
        ];

        protected override uint ItemId => Task.ItemId;
        protected override string FailureText => "Unable to equip gear.";

        protected override unsafe bool IsDone(InventoryManager* inventoryManager) =>
            EquipmentSlots.IsEquipped(inventoryManager, TargetSlots, Task.ItemId);

        protected override unsafe void Move(InventoryManager* inventoryManager)
        {
            if (inventoryManager->GetInventoryContainer(InventoryType.EquippedItems) == null)
                return;

            if (IsDone(inventoryManager))
            {
                logger.LogInformation("Already equipped {Item}, skipping step", Item.Name.ToString());
                return;
            }

            foreach (InventoryType sourceInventoryType in SourceInventoryTypes)
            {
                InventoryContainer* sourceContainer = inventoryManager->GetInventoryContainer(sourceInventoryType);
                if (sourceContainer == null)
                    continue;

                if (inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType, isHq: true) == 0 &&
                    inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType) == 0)
                {
                    continue;
                }

                for (ushort sourceSlot = 0; sourceSlot < sourceContainer->Size; sourceSlot++)
                {
                    InventoryItem* sourceItem = sourceContainer->GetInventorySlot(sourceSlot);
                    if (sourceItem == null || sourceItem->ItemId != Task.ItemId)
                        continue;

                    ushort targetSlot = TargetSlots
                        .Where(x =>
                        {
                            InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                            return itemSlot == null || itemSlot->ItemId == 0;
                        })
                        .Concat(TargetSlots).First();

                    logger.LogInformation(
                        "Equipping item from {SourceInventory}, {SourceSlot} to {TargetInventory}, {TargetSlot}",
                        sourceInventoryType, sourceSlot, InventoryType.EquippedItems, targetSlot);

                    int result = inventoryManager->MoveItemSlot(sourceInventoryType, sourceSlot,
                        InventoryType.EquippedItems, targetSlot, a6: true);
                    logger.LogInformation("MoveItemSlot result: {Result}", result);
                    return;
                }
            }

            throw new TaskException($"Could not equip item {Task.ItemId}.");
        }
    }
}
