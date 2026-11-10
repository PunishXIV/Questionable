using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.Steps.Shared;

internal static class Fish
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Fish)
                yield break;

            // An unavailable provider must not turn an unmet fishing objective into an empty queue.
            yield return new Task(quest, step.ItemsToGather.FirstOrDefault(), step.CompletionQuestVariablesFlags);
            foreach (GatheredItem item in step.ItemsToGather.Skip(1))
                yield return new Task(quest, item, step.CompletionQuestVariablesFlags);
        }
    }

    // Feesh switches job and travels itself.
    public static bool IsHandledByFeesh(QuestStep step, FeeshIpc feeshIpc) =>
        step is { InteractionType: EInteractionType.Fish, ItemsToGather.Count: > 0 } &&
        step.ItemsToGather.All(item => item.Collectability == 0) &&
        feeshIpc.IsAvailable() &&
        step.ItemsToGather.All(item => feeshIpc.CanCatch(item.ItemId));

    internal sealed record Task(Quest Quest, GatheredItem? GatheredItem,
        IList<QuestWorkValue?> CompletionFlags) : ITask
    {
        public bool HasCompletionFlags { get; } =
            QuestWorkUtils.HasCompletionFlags(CompletionFlags);

        public override string ToString() =>
            $"Fish{(HasCompletionFlags ? "*" : "")}{(GatheredItem != null ? $"({GatheredItem.ItemCount}x {GatheredItem.ItemId})" : "")}";
    }

    internal sealed class Executor : TaskExecutor<Task>, IStoppable
    {
        private readonly FeeshIpc _feeshIpc;
        private readonly FishingState _state;
        private readonly IDataManager _dataManager;
        private readonly ILogger<Executor> _logger;
        private readonly IQuestGameState _gameState;
        private Guid? _operationId;
        private long? _completedAt;

        public Executor(FeeshIpc feeshIpc, FishingState state, IDataManager dataManager,
            ILogger<Executor> logger, IQuestGameState gameState)
        {
            _feeshIpc = feeshIpc;
            _state = state;
            _dataManager = dataManager;
            _logger = logger;
            _gameState = gameState;
        }

        private string ItemName(uint itemId) =>
            _dataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ToString() is { Length: > 0 } name
                ? name
                : itemId.ToString(CultureInfo.InvariantCulture);

        protected override bool Start()
        {
            _operationId = null;
            _completedAt = null;
            if (_state.HasOperation)
                throw new TaskException(_state.BlockReason!);
            if (HasCompletedObjectives(Task, _gameState))
                return false;
            if (Task.GatheredItem is not { } item)
                throw new TaskException("This fishing step has no fish for Feesh to catch. Fish manually, then restart Questionable.");
            if (UnsupportedReason(item) is { } reason)
                throw new TaskException(reason);

            int missing = item.ItemCount - CountEligibleInInventory(item);
            FeeshStartResult result = _feeshIpc.StartCatch(item.ItemId, Math.Max(1, missing), out string message);
            if (result is FeeshStartResult.Started or FeeshStartResult.Uncertain)
                _operationId = _state.Id;
            if (result != FeeshStartResult.Started || !_operationId.HasValue)
                throw new TaskException(result switch
                {
                    FeeshStartResult.Busy => "Feesh is busy. Finish or stop its current work in WahTools, then restart Questionable.",
                    FeeshStartResult.Refused => $"Feesh refused to catch {ItemName(item.ItemId)}; the Dalamud log says why. Fix that in Feesh or fish manually, then restart Questionable.",
                    FeeshStartResult.NotReady => "Feesh is not available. Check that WahTools is loaded.",
                    _ => $"Feesh did not confirm the catch job ({result}): {message}",
                });
            _logger.LogInformation("Started Feesh catch job {Id} for quest {QuestId}", _operationId, Task.Quest.Id);
            return true;
        }

        private string? UnsupportedReason(GatheredItem item)
        {
            if (item.Collectability > 0)
                return $"Feesh can't catch collectable fish yet. Fish {ItemName(item.ItemId)} (collectability {item.Collectability}) manually, then restart Questionable.";
            if (!_feeshIpc.IsAvailable())
                return "Fishing needs Feesh from WahTools. Install it or fish manually, then restart Questionable.";
            if (!_feeshIpc.CanCatch(item.ItemId))
                return $"Feesh doesn't know where to catch {ItemName(item.ItemId)}. Fish it manually, then restart Questionable.";
            return null;
        }

        public override ETaskResult Update()
        {
            if (!_operationId.HasValue || _state.Id != _operationId)
                throw new TaskException("Feesh is no longer fishing for Questionable");

            if (_state.IsFinished)
            {
                // Cancelled after the quest flags were met still counts as done.
                bool ended = _state.Status is ExternalOperationStatus.Succeeded or ExternalOperationStatus.Cancelled;
                if (ended && HasCompletedObjectives(Task, _gameState))
                {
                    Release();
                    return ETaskResult.TaskComplete;
                }

                _completedAt ??= TimeProvider.System.GetTimestamp();
                if (ended && TimeProvider.System.GetElapsedTime(_completedAt.Value) < TimeSpan.FromSeconds(2))
                    return ETaskResult.StillRunning;
                string message = _state.Message;
                Release();
                throw new TaskException($"Feesh stopped before the fishing objective was met: {message}");
            }

            if (_state.NeedsConfirmation)
                throw new TaskException(_state.BlockReason!);
            if (HasCompletedObjectives(Task, _gameState) && !_state.StopRequested)
                _feeshIpc.RequestStop();
            return ETaskResult.StillRunning;
        }

        private void Release()
        {
            if (_state.TryClearFinished(_operationId) && Task.GatheredItem is { } item)
                _feeshIpc.ReleaseCatch(item.ItemId);
        }

        public void StopNow()
        {
            if (_operationId.HasValue && _state.Id == _operationId)
                _feeshIpc.CancelWork();
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal static bool ObjectivesSatisfied(Task task, bool matchesQuestWork, int eligibleItemCount) =>
        task.HasCompletionFlags && matchesQuestWork ||
        task.GatheredItem is { } item && eligibleItemCount >= item.ItemCount;

    private static bool HasCompletedObjectives(Task task, IQuestGameState gameState)
    {
        // Matching quest flags count as done even for quests whose fish are consumed or are key items.
        if (task.HasCompletionFlags && gameState.GetQuestProgressInfo(task.Quest.Id) is { } work &&
            QuestWorkUtils.Matches(task.CompletionFlags, work))
            return true;
        if (task.GatheredItem is not { } item)
            return false;
        return ObjectivesSatisfied(task, false, CountEligibleInInventory(item));
    }

    private static unsafe int CountEligibleInInventory(GatheredItem item)
    {
        if (item.ItemId == 0 || item.ItemCount <= 0 || item.Collectability > short.MaxValue)
            throw new TaskException("Invalid fishing item requirement");
        InventoryManager* inventory = InventoryManager.Instance();
        if (inventory == null)
            throw new TaskException("Inventory is unavailable; cannot verify the fishing objective");
        return CountEligibleItems(item, (id, highQuality, collectability) =>
            inventory->GetInventoryItemCount(id, isHq: highQuality, checkEquipped: false, minCollectability: collectability));
    }

    internal static int CountEligibleItems(GatheredItem item, Func<uint, bool, short, int> count) =>
        item.Collectability > 0
            ? count(checked(item.ItemId + 500000), false, checked((short)item.Collectability))
            : count(item.ItemId, false, 0) + count(item.ItemId, true, 0) + count(checked(item.ItemId + 500000), false, 0);
}
