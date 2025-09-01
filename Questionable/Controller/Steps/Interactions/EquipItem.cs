using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LLib;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Functions;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller.Steps.Interactions;

internal static class EquipItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.EquipItem)
                return null;

            ArgumentNullException.ThrowIfNull(step.ItemId);
            return new Task(step.ItemId.Value);
        }
    }

    internal sealed record Task(uint ItemId) : ITask
    {
        public override string ToString() => $"Equip({ItemId})";
    }

    internal sealed class DoEquip(
        IDataManager dataManager,
        ILogger<DoEquip> logger) : TaskExecutor<Task>, IToastAware
    {
        private const int MaxAttempts = 3;

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
            InventoryType.Inventory4,
        ];

        private int _attempts;
        private Item? _item;
        private List<ushort> _targetSlots = null!;
        private DateTime _continueAt = DateTime.MaxValue;

        protected override bool Start()
        {
            _item = dataManager.GetExcelSheet<Item>().GetRowOrDefault(Task.ItemId) ??
                    throw new ArgumentOutOfRangeException(nameof(Task.ItemId));
            _targetSlots = GetEquipSlot(_item) ?? throw new InvalidOperationException("Not a piece of equipment");

            Equip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return ETaskResult.StillRunning;

            foreach (ushort x in _targetSlots)
            {
                var itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                if (itemSlot != null && itemSlot->ItemId == Task.ItemId)
                    return ETaskResult.TaskComplete;
            }

            Equip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        private unsafe void Equip()
        {
            ++_attempts;
            if (_attempts > MaxAttempts)
                throw new TaskException("Unable to equip gear.");

            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            var equippedContainer = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            if (equippedContainer == null)
                return;

            foreach (ushort slot in _targetSlots)
            {
                var itemSlot = equippedContainer->GetInventorySlot(slot);
                if (itemSlot != null && itemSlot->ItemId == Task.ItemId)
                {
                    logger.LogInformation("Already equipped {Item}, skipping step", _item?.Name.ToString());
                    return;
                }
            }

            foreach (InventoryType sourceInventoryType in SourceInventoryTypes)
            {
                var sourceContainer = inventoryManager->GetInventoryContainer(sourceInventoryType);
                if (sourceContainer == null)
                    continue;

                if (inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType, true) == 0 &&
                    inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType) == 0)
                    continue;

                for (ushort sourceSlot = 0; sourceSlot < sourceContainer->Size; sourceSlot++)
                {
                    var sourceItem = sourceContainer->GetInventorySlot(sourceSlot);
                    if (sourceItem == null || sourceItem->ItemId != Task.ItemId)
                        continue;

                    // Move the item to the first available slot
                    ushort targetSlot = _targetSlots
                        .Where(x =>
                        {
                            var itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                            return itemSlot == null || itemSlot->ItemId == 0;
                        })
                        .Concat(_targetSlots).First();

                    logger.LogInformation(
                        "Equipping item from {SourceInventory}, {SourceSlot} to {TargetInventory}, {TargetSlot}",
                        sourceInventoryType, sourceSlot, InventoryType.EquippedItems, targetSlot);

                    int result = inventoryManager->MoveItemSlot(sourceInventoryType, sourceSlot,
                        InventoryType.EquippedItems, targetSlot, true);
                    logger.LogInformation("MoveItemSlot result: {Result}", result);
                    return;
                }
            }

            throw new TaskException($"Could not equip item {Task.ItemId}.");
        }

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
                _ => null
            };
        }

        public bool OnErrorToast(SeString message)
        {
            string? insufficientArmoryChestSpace = dataManager.GetString<LogMessage>(709, x => x.Text);
            if (GameFunctions.GameStringEquals(message.TextValue, insufficientArmoryChestSpace))
                _attempts = MaxAttempts;

            return false;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
