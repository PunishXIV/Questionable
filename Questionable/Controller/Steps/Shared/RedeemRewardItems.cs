using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Quest = Questionable.Model.Quest;
using Mount = Questionable.Controller.Steps.Common.Mount;
namespace Questionable.Controller.Steps.Shared;

internal static class RedeemRewardItems
{
    internal sealed class Factory(QuestData questData, IDataManager dataManager) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AcceptQuest)
                return [];

            return CreateRedeemTasks(questData, dataManager);
        }
    }

    internal static List<ITask> CreateRedeemTasks(QuestData questData, IDataManager dataManager)
    {
        List<ITask> tasks = [];
        HashSet<uint> seenItemIds = [];
        unsafe
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return tasks;

            void TryAdd(ItemReward itemReward)
            {
                if (!seenItemIds.Add(itemReward.ItemId))
                    return;

                if (inventoryManager->GetInventoryItemCount(itemReward.ItemId) > 0 &&
                    !itemReward.IsUnlocked())
                    tasks.Add(new Task(itemReward));
            }

            foreach (ItemReward itemReward in questData.RedeemableItems)
                TryAdd(itemReward);

            HashSet<uint> inventoryItemIds = [];
            for (InventoryType inventoryType = InventoryType.Inventory1;
                 inventoryType <= InventoryType.Inventory4;
                 ++inventoryType)
            {
                InventoryContainer* container = inventoryManager->GetInventoryContainer(inventoryType);
                if (container == null)
                    continue;

                for (int i = 0; i < container->Size; ++i)
                {
                    InventoryItem* slot = container->GetInventorySlot(i);
                    if (slot == null || slot->ItemId == 0)
                        continue;

                    inventoryItemIds.Add(slot->ItemId);
                }
            }

            foreach (uint itemId in inventoryItemIds)
            {
                if (seenItemIds.Contains(itemId))
                    continue;

                Item? item = dataManager.GetExcelSheet<Item>()?.GetRow(itemId);
                if (item == null)
                    continue;

                ItemReward? redeemable = ItemReward.CreateFromItem(item.Value, new QuestId(0));
                if (redeemable != null)
                    TryAdd(redeemable);
            }
        }

        return tasks.Count != 0 ? [new Mount.UnmountTask(), ..tasks] : tasks;
    }

    internal sealed record Task(ItemReward ItemReward) : ITask
    {
        public override string ToString() => $"TryRedeem({ItemReward.Name})";
    }

    internal sealed class Executor
    (
        GameFunctions gameFunctions,
        ICondition condition) : TaskExecutor<Task>
    {
        private static readonly TimeSpan MinimumCastTime = TimeSpan.FromSeconds(4);
        private DateTime _continueAt;

        protected override bool Start()
        {
            if (condition[ConditionFlag.Mounted])
                return false;

            if (Task.ItemReward.Type is EItemRewardType.Coffer && GameFunctions.GetFreeInventorySlots() < 1)
                return false;

            TimeSpan castTime = Task.ItemReward.CastTime;
            if (castTime < MinimumCastTime)
                castTime = MinimumCastTime;

            _continueAt = DateTime.Now
                .Add(castTime)
                .AddSeconds(3);
            return gameFunctions.UseItem(Task.ItemReward.ItemId);
        }

        public override ETaskResult Update()
        {
            if (Task.ItemReward.Type is EItemRewardType.Coffer && GameFunctions.GetFreeInventorySlots() < 1)
                return ETaskResult.StillRunning;

            if (condition[ConditionFlag.Casting])
                return ETaskResult.StillRunning;

            return DateTime.Now <= _continueAt ? ETaskResult.StillRunning : ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
