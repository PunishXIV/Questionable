using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller.Steps.Interactions;

internal static class UseItem
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.SinglePlayerDuty or EInteractionType.CompleteQuest)
            {
                if (step.ItemId == null)
                    return [];
            }
            else if (step.InteractionType != EInteractionType.UseItem)
                return [];
            if (!step.ItemId.HasValue)
                throw new ArgumentNullException(nameof(step.ItemId));

            MountStep.UnmountTask unmount = new();
            if (step.GroundTarget == true)
            {
                ITask task;
                if (step.DataId != null)
                {
                    task = new UseOnGround(quest.Id, step.DataId.Value, step.ItemId.Value,
                        step.CompletionQuestVariablesFlags);
                }
                else
                {
                    if (!step.Position.HasValue)
                        throw new ArgumentNullException(nameof(step.Position));

                    task = new UseOnPosition(quest.Id, step.Position.Value, step.ItemId.Value,
                        step.CompletionQuestVariablesFlags);
                }

                return [unmount, new WaitAtEnd.WaitDelay(), task];
            }
            else if (step.DataId != null)
            {
                UseOnObject task = new(quest.Id, step.DataId.Value, step.ItemId.Value,
                    step.CompletionQuestVariablesFlags);
                return [unmount, task];
            }
            else
            {
                UseOnSelf task = new(quest.Id, step.ItemId.Value, step.CompletionQuestVariablesFlags);
                return [unmount, task];
            }
        }

    }

    internal interface IUseTask : ITask
    {
        ElementId? QuestId { get; }
        uint ItemId { get; }
        IList<QuestWorkValue?> CompletionFlags { get; }
        bool StartingCombat { get; }
    }

    internal abstract class ExecutorBase<T>
    (
        ICondition condition,
        ILogger logger,
        IQuestGameState gameState,
        IDataManager dataManager) : TaskExecutor<T>, IToastAware
    where T : class, IUseTask
    {
        private const int MaxRefusals = 3;
        private static readonly TimeSpan RefusalWindow = TimeSpan.FromSeconds(2);

        private DateTime _continueAt;
        private bool _usedItem;
        private DateTime? _usedAt;
        private int _refusals;
        private string? _lastRefusal;

        private ElementId? QuestId => Task.QuestId;
        protected uint ItemId => Task.ItemId;
        private IList<QuestWorkValue?> CompletionFlags => Task.CompletionFlags;
        private bool StartingCombat => Task.StartingCombat;

        protected abstract bool Use();

        private static string GetItemName(IDataManager dataManager, uint itemId)
        {
            string? name = itemId >= 2_000_000
                ? dataManager.GetExcelSheet<Lumina.Excel.Sheets.EventItem>().GetRowOrDefault(itemId)?.Singular.ExtractText()
                : dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault(itemId)?.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? itemId.ToString(CultureInfo.InvariantCulture) : name;
        }

        protected override unsafe bool Start()
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                throw new TaskException("No InventoryManager");

            if (inventoryManager->GetInventoryItemCount(ItemId) == 0)
                throw new NeedsPlayerException($"You don't have any {GetItemName(dataManager, ItemId)} (normal quality) in your inventory.");

            UseNow();
            return true;
        }

        private void UseNow()
        {
            ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() => _usedItem = Use());
            _usedAt = DateTime.Now;
            _continueAt = DateTime.Now.Add(GetRetryDelay());
        }

        // The game refuses some uses (in combat, wrong area, busy) with an error toast while UseItem still reports success.
        internal static bool IsRefusalOfLastUse(bool usedItem, DateTime? usedAt, DateTime now) =>
            usedItem && usedAt is { } at && now - at <= RefusalWindow;

        public bool OnErrorToast(SeString message)
        {
            if (!IsRefusalOfLastUse(_usedItem, _usedAt, DateTime.Now))
                return false;

            _usedItem = false;
            _usedAt = null;
            _refusals++;
            _lastRefusal = message.TextValue;
            _continueAt = DateTime.Now.AddSeconds(2);
            logger.LogInformation("The game refused item {ItemId} ({Message}), attempt {Attempt}/{MaxAttempts}",
                ItemId, _lastRefusal, _refusals, MaxRefusals);
            return false;
        }

        public override unsafe ETaskResult Update()
        {
            if (QuestId is QuestId realQuestId && QuestWorkUtils.HasCompletionFlags(CompletionFlags))
            {
                QuestProgressInfo? questWork = gameState.GetQuestProgressInfo(realQuestId);
                if (questWork != null &&
                    QuestWorkUtils.Matches(CompletionFlags, questWork))
                    return ETaskResult.TaskComplete;
            }

            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;

            if (_refusals >= MaxRefusals)
                throw new TaskException($"The game refused to use {GetItemName(dataManager, ItemId)}: {_lastRefusal}");

            if (StartingCombat && condition[ConditionFlag.InCombat])
                return ETaskResult.TaskComplete;

            if (!_usedItem)
            {
                UseNow();
                return ETaskResult.StillRunning;
            }

            return ETaskResult.TaskComplete;
        }

        private static TimeSpan GetRetryDelay() => TimeSpan.FromSeconds(5);

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record UseOnGround
    (
        ElementId? QuestId,
        uint DataId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionFlags,
        bool StartingCombat = false) : IUseTask
    {
        public override string ToString() => $"UseItem({ItemId} on ground at {DataId})";
    }

    internal sealed class UseOnGroundExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<UseOnGroundExecutor> logger,
        IQuestGameState gameState,
        IDataManager dataManager)
        : ExecutorBase<UseOnGround>(condition, logger, gameState, dataManager)
    {
        protected override bool Use() => gameFunctions.UseItemOnGround(Task.DataId, ItemId);
    }

    internal sealed record UseOnPosition
    (
        ElementId? QuestId,
        Vector3 Position,
        uint ItemId,
        IList<QuestWorkValue?> CompletionFlags,
        bool StartingCombat = false) : IUseTask
    {
        public override string ToString() => $"UseItem({ItemId} on ground at {Position.ToString("G5", CultureInfo.InvariantCulture)})";
    }

    internal sealed class UseOnPositionExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<UseOnPosition> logger,
        IQuestGameState gameState,
        IDataManager dataManager)
        : ExecutorBase<UseOnPosition>(condition, logger, gameState, dataManager)
    {
        protected override bool Use() => gameFunctions.UseItemOnPosition(Task.Position, ItemId);
    }

    internal sealed record UseOnObject
    (
        ElementId? QuestId,
        uint DataId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionFlags,
        bool StartingCombat = false) : IUseTask
    {
        public override string ToString() => $"UseItem({ItemId} on {DataId})";
    }

    internal sealed class UseOnObjectExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<UseOnObject> logger,
        IQuestGameState gameState,
        IDataManager dataManager)
        : ExecutorBase<UseOnObject>(condition, logger, gameState, dataManager)
    {
        protected override bool Use() => gameFunctions.UseItem(Task.DataId, ItemId);
    }

    internal sealed record UseOnSelf
    (
        ElementId? QuestId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionFlags,
        bool StartingCombat = false) : IUseTask
    {
        public override string ToString() => $"UseItem({ItemId})";
    }

    internal sealed class UseOnSelfExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<UseOnSelf> logger,
        IQuestGameState gameState,
        IDataManager dataManager)
        : ExecutorBase<UseOnSelf>(condition, logger, gameState, dataManager)
    {
        protected override bool Use() => gameFunctions.UseItem(ItemId);
    }
}
