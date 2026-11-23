using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

internal static class RedeemRewardItems
{
    private static readonly Dictionary<uint, int> AttemptedItems = [];

    internal static void ResetAttemptedItems() => AttemptedItems.Clear();

    internal static bool UsesWholeStack(ItemReward reward) => reward.Type is EItemRewardType.Coffer or EItemRewardType.Mgp;

    internal static unsafe int ItemCount(IEnumerable<ITask> tasks)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return tasks.OfType<Task>().Sum(x => UsesWholeStack(x.ItemReward) && inventoryManager != null
            ? Math.Max(1, inventoryManager->GetInventoryItemCount(x.ItemReward.ItemId))
            : 1);
    }

    internal sealed class Factory(QuestData questData, IDataManager dataManager) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AcceptQuest)
                return [];

            return CreateTasks(questData, dataManager);
        }
    }

    internal static List<ITask> CreateTasks(QuestData questData, IDataManager dataManager, bool overrideConfig = false)
    {
        Configuration configuration = Configuration.Instance();
        if (!configuration.Advanced.AutoRedeemRewardItems && !overrideConfig)
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

                int count = inventoryManager->GetInventoryItemCount(itemReward.ItemId);
                if (count <= 0 || itemReward.IsUnlocked())
                    return;

                if (AttemptedItems.TryGetValue(itemReward.ItemId, out int attemptedCount) && attemptedCount == count)
                    return;

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

                ItemReward? redeemable = ItemReward.Create(item.Value, new QuestId(0));
                if (redeemable != null)
                    TryAdd(redeemable);
            }
        }

        // Coffers last, so the MGP cards and unlock items they give are picked up afterwards.
        return tasks.Count != 0
            ? [new MountStep.UnmountTask(), .. tasks.OrderBy(x => x is Task { ItemReward.Type: EItemRewardType.Coffer })]
            : tasks;
    }

    internal sealed record Task(ItemReward ItemReward) : ITask
    {
        public override string ToString() => $"TryRedeem({ItemReward.Name})";
    }

    internal sealed class Executor
    (
        GameFunctions gameFunctions,
        ICondition condition,
        DalamudInitializer dalamudInitializer,
        QuestData questData,
        IDataManager dataManager,
        ILogger<RedeemRewardItems.Executor> logger) : TaskExecutor<Task>, ITaskExecutor.IExtraTaskCreator
    {
        private static readonly TimeSpan MinimumCastTime = TimeSpan.FromSeconds(4);

        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

        private DateTime _giveUpAt;
        private DateTime _continueAt;
        private bool _usedItem;
        private int _itemCountBeforeUse;

        protected override bool Start()
        {
            // A coffer needs a free inventory slot. If there's none we can't open it, so skip.
            if (Task.ItemReward.Type is EItemRewardType.Coffer && GameFunctions.GetFreeInventorySlots() < 1)
            {
                logger.LogDebug("item is coffer, but no inv slots");
                return false;
            }

            _giveUpAt = DateTime.Now.Add(GiveUpAfter);
            logger.LogTrace("Start");
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            logger.LogTrace("Update");
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
            {
                logger.LogTrace("inventorymanager == null");
                return ETaskResult.TaskComplete;
            }

            bool timedOut = DateTime.Now > _giveUpAt;

            if (!_usedItem)
            {
                if (timedOut)
                {
                    logger.LogTrace("timedOut");
                    return ETaskResult.TaskComplete;
                }

                if (dalamudInitializer.FlagGearUnequippable)
                {
                    logger.LogDebug("FlagGearUnequippable");
                    dalamudInitializer.FlagGearUnequippable = false;
                    return ETaskResult.TaskComplete;
                }

                // The game refuses item use while mounted, in combat, casting or between areas.
                if (!IsReadyToUseItem())
                    return ETaskResult.StillRunning;

                _itemCountBeforeUse = inventoryManager->GetInventoryItemCount(Task.ItemReward.ItemId);

                if (_itemCountBeforeUse == 0)
                {
                    logger.LogTrace("_itemCountBeforeUse == 0");
                    return ETaskResult.TaskComplete;
                }

                if (!gameFunctions.UseItem(Task.ItemReward.ItemId))
                    return ETaskResult.StillRunning;

                AttemptedItems[Task.ItemReward.ItemId] = _itemCountBeforeUse;

                TimeSpan castTime = Task.ItemReward.CastTime;
                if (castTime < MinimumCastTime)
                    castTime = MinimumCastTime;

                _usedItem = true;
                _continueAt = DateTime.Now
                    .Add(castTime);
                logger.LogTrace("condition[ConditionFlag.Casting]");
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.Casting])
            {
                logger.LogTrace("condition[ConditionFlag.Casting]");
                return ETaskResult.StillRunning;
            }

            bool used = inventoryManager->GetInventoryItemCount(Task.ItemReward.ItemId) < _itemCountBeforeUse;
            if (!used && DateTime.Now <= _continueAt)
            {
                logger.LogTrace("DateTime.Now <= _continueAt");
                return ETaskResult.StillRunning;
            }

            // UseItem can report success without consuming the item; retry until the count drops so no reward is skipped.
            if (!used && !timedOut)
            {
                _usedItem = false;
                logger.LogTrace("itemcount >= _itemCountBeforeUse && !timedOut");
                return ETaskResult.StillRunning;
            }

            if (used && HasMoreToUse(inventoryManager))
            {
                _usedItem = false;
                _giveUpAt = DateTime.Now.Add(GiveUpAfter);
                return ETaskResult.StillRunning;
            }

            return used && Task.ItemReward.Type is EItemRewardType.Coffer ? ETaskResult.CreateNewTasks : ETaskResult.TaskComplete;
        }

        // Other coffers are still queued, so only what the coffer gave is added.
        public IEnumerable<ITask> CreateExtraTasks() =>
            CreateTasks(questData, dataManager, overrideConfig: true)
                .Where(x => x is Task { ItemReward.Type: not EItemRewardType.Coffer });

        // Unlock items stop after one use so a duplicate isn't used up.
        private unsafe bool HasMoreToUse(InventoryManager* inventoryManager) =>
            UsesWholeStack(Task.ItemReward) &&
            inventoryManager->GetInventoryItemCount(Task.ItemReward.ItemId) > 0 &&
            !Task.ItemReward.IsUnlocked() &&
            (Task.ItemReward.Type is not EItemRewardType.Coffer || GameFunctions.GetFreeInventorySlots() > 0);

        private unsafe bool IsReadyToUseItem()
        {
            if (condition[ConditionFlag.Mounted])
                return false;

            // A use during the last item's animation lock is refused ("Cannot execute at this time") and costs the 4 s wait.
            if (ActionManager.Instance() is var actionManager && actionManager != null && actionManager->AnimationLock > 0)
                return false;

            if (condition[ConditionFlag.InCombat])
                return false;

            return !gameFunctions.IsOccupied();
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
