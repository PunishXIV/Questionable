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

// WIP
internal static class UnequipItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.UnequipItem)
                return null;

            ArgumentNullException.ThrowIfNull(step.ItemId);
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

        //private static readonly IReadOnlyList<InventoryType> SourceInventoryTypes =
        //[
        //    InventoryType.ArmoryMainHand,
        //    InventoryType.ArmoryOffHand,
        //    InventoryType.ArmoryHead,
        //    InventoryType.ArmoryBody,
        //    InventoryType.ArmoryHands,
        //    InventoryType.ArmoryLegs,
        //    InventoryType.ArmoryFeets,

        //    InventoryType.ArmoryEar,
        //    InventoryType.ArmoryNeck,
        //    InventoryType.ArmoryWrist,
        //    InventoryType.ArmoryRings,

        //    InventoryType.ArmorySoulCrystal,

        //    InventoryType.Inventory1,
        //    InventoryType.Inventory2,
        //    InventoryType.Inventory3,
        //    InventoryType.Inventory4,
        //];

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

            foreach (ushort x in _targetSlots)
            {
                InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                if (itemSlot != null && itemSlot->ItemId != Task.ItemId)
                    return ETaskResult.TaskComplete;
            }

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

            InventoryContainer* equippedContainer = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            if (equippedContainer == null)
                return;

            foreach (ushort slot in _targetSlots)
            {
                InventoryItem* itemSlot = equippedContainer->GetInventorySlot(slot);

                if (itemSlot != null && itemSlot->ItemId != Task.ItemId)
                {
                    logger.LogInformation("Already unequipped {Item}, skipping step", _item?.Name.ToString());
                    return;
                }
            }

            // TODO: Implement actual unequip logic (MoveItemSlot to armory chest)
            logger.LogWarning("Unequip not yet implemented for item {ItemId}", Task.ItemId);
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
                var _ => null
            };
        }
    }
}
