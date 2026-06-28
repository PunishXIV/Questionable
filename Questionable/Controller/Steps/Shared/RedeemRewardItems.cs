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
        Configuration configuration = Configuration.Instance();
        if (!configuration.Advanced.AutoRedeemRewardItems)
            return [];

        List<ITask> tasks = [];
        HashSet<uint> seenItemIds = [];
        HashSet<uint> blacklist = configuration.Advanced.AutoRedeemItemBlacklist;
        unsafe
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return tasks;

            void TryAdd(ItemReward itemReward)
            {
                if (!seenItemIds.Add(itemReward.ItemId))
                    return;

                if (blacklist.Contains(itemReward.ItemId))
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

        // Don't block the queue forever if we never reach a usable state (e.g. stuck in combat)
        // or if the item never actually gets consumed.
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

        private DateTime _giveUpAt;
        private DateTime _continueAt;
        private bool _usedItem;
        private int _itemCountBeforeUse;

        protected override bool Start()
        {
            // A coffer needs a free inventory slot. If there's none we can't open it, so skip.
            if (Task.ItemReward.Type is EItemRewardType.Coffer && GameFunctions.GetFreeInventorySlots() < 1)
                return false;

            _giveUpAt = DateTime.Now.Add(GiveUpAfter);
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return ETaskResult.TaskComplete;

            bool timedOut = DateTime.Now > _giveUpAt;

            if (!_usedItem)
            {
                if (timedOut)
                    return ETaskResult.TaskComplete;

                // Wait until the character can actually use an item (not mounted, in combat,
                // casting, animation-locked, between areas, ...) before trying to redeem.
                if (!IsReadyToUseItem())
                    return ETaskResult.StillRunning;

                _itemCountBeforeUse = inventoryManager->GetInventoryItemCount(Task.ItemReward.ItemId);

                // Already gone (e.g. consumed elsewhere) - nothing to do.
                if (_itemCountBeforeUse == 0)
                    return ETaskResult.TaskComplete;

                if (!gameFunctions.UseItem(Task.ItemReward.ItemId))
                    return ETaskResult.StillRunning;

                TimeSpan castTime = Task.ItemReward.CastTime;
                if (castTime < MinimumCastTime)
                    castTime = MinimumCastTime;

                _usedItem = true;
                _continueAt = DateTime.Now
                    .Add(castTime)
                    .AddSeconds(3);
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.Casting])
                return ETaskResult.StillRunning;

            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;

            // UseItem() can report success without actually consuming the item (e.g. a transient
            // "cannot use right now"). If the count didn't drop, try again until it does or we give up,
            // so we don't silently skip items in a multi-reward batch.
            if (inventoryManager->GetInventoryItemCount(Task.ItemReward.ItemId) >= _itemCountBeforeUse && !timedOut)
            {
                _usedItem = false;
                return ETaskResult.StillRunning;
            }

            return ETaskResult.TaskComplete;
        }

        private bool IsReadyToUseItem()
        {
            if (condition[ConditionFlag.Mounted])
                return false;

            if (condition[ConditionFlag.InCombat])
                return false;

            return !gameFunctions.IsOccupied();
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
