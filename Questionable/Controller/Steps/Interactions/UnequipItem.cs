using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Questing;
using Quest = Questionable.Model.Quest;

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

            if (!IsItemEquipped(inventoryManager))
                return ETaskResult.TaskComplete;

            Unequip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        public bool OnErrorToast(SeString message)
        {
            string? insufficientArmoryChestSpace = DataManagerAdapter.GetString<LogMessage>(dataManager, 709, x => x.Text);
            if (GameFunctions.GameStringEquals(message.TextValue, insufficientArmoryChestSpace))
                _attempts = MaxAttempts;

            return false;
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            _item = dataManager.GetExcelSheet<Item>().GetRowOrDefault(Task.ItemId) ??
                    throw new ArgumentOutOfRangeException(nameof(Task.ItemId));
            _targetSlots = GetEquipSlot(_item) ?? throw new InvalidOperationException("Not a piece of equipment");

            if (GetArmoryInventoryType(_item.Value) == null)
                throw new InvalidOperationException("Item has no armory destination");

            Unequip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        private unsafe bool IsItemEquipped(InventoryManager* inventoryManager)
        {
            foreach (ushort slot in _targetSlots)
            {
                InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, slot);
                if (itemSlot != null && itemSlot->ItemId == Task.ItemId)
                    return true;
            }

            return false;
        }

        private unsafe void Unequip()
        {
            ++_attempts;
            if (_attempts > MaxAttempts)
                throw new TaskException("Unable to unequip gear.");

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            if (!IsItemEquipped(inventoryManager))
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

                if (!TryFindFirstEmptySlot(armoryContainer, out ushort targetSlot))
                {
                    logger.LogWarning("Armory container {ArmoryType} is full, cannot unequip item {ItemId}",
                        armoryType, Task.ItemId);
                    throw new TaskException("Unable to unequip gear - armory chest is full.");
                }

                logger.LogInformation(
                    "Unequipping item from {SourceInventory}, {SourceSlot} to {TargetInventory}, {TargetSlot}",
                    InventoryType.EquippedItems, equippedSlot, armoryType, targetSlot);

                int result = inventoryManager->MoveItemSlot(InventoryType.EquippedItems, equippedSlot,
                    armoryType, targetSlot, true);
                logger.LogInformation("MoveItemSlot result: {Result}", result);
                return;
            }
        }

        private static unsafe bool TryFindFirstEmptySlot(InventoryContainer* container, out ushort slot)
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

        private static InventoryType? GetArmoryInventoryType(Item item) =>
            item.EquipSlotCategory.RowId switch
            {
                >= 1 and <= 11 => ArmoryInventoryTypes[item.EquipSlotCategory.RowId - 1],
                12 => InventoryType.ArmoryRings,
                13 => InventoryType.ArmoryMainHand,
                17 => InventoryType.ArmorySoulCrystal,
                _ => null
            };

        private static List<ushort>? GetEquipSlot(Item? item)
        {
            if (item == null)
                return [];
            return item.Value.EquipSlotCategory.RowId switch
            {
                >= 1 and <= 11 => [(ushort)(item.Value.EquipSlotCategory.RowId - 1)],
                12 => [11, 12], // rings
                13 => [0],
                17 => [13], // soul crystal
                var _ => null
            };
        }
    }
}
