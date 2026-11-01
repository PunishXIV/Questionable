using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Fishing;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;

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
            yield return new FishTask(quest, step.ItemsToGather.FirstOrDefault(), step.CompletionQuestVariablesFlags);
            foreach (GatheredItem item in step.ItemsToGather.Skip(1))
                yield return new FishTask(quest, item, step.CompletionQuestVariablesFlags);
        }
    }

    internal sealed record FishTask(Quest Quest, GatheredItem? GatheredItem,
        IList<QuestWorkValue?> CompletionQuestVariablesFlags) : ITask
    {
        public bool HasCompletionQuestVariablesFlags { get; } =
            QuestWorkUtils.HasCompletionFlags(CompletionQuestVariablesFlags);
        public string? PreparedPreset { get; init; }

        public override string ToString() =>
            $"Fish{(HasCompletionQuestVariablesFlags ? "*" : "")}{(GatheredItem != null ? $"({GatheredItem.ItemCount}x {GatheredItem.ItemId})" : "")}";
    }

    internal sealed class DoFish : TaskExecutor<FishTask>, IStoppableTaskExecutor, IExtraTaskCreator
    {
        private readonly IAutoHookIpc _autoHookIpc;
        private readonly FishingOperationState _operation;
        private readonly IFishingPresetGenerator _generator;
        private readonly ILogger<DoFish> _logger;
        private readonly Func<FishTask, bool> _objectivesSatisfied;
        private readonly TimeProvider _clock;
        private Guid? _owner;
        private string? _preparedPreset;
        private long? _completedAt;

        public DoFish(IAutoHookIpc autoHookIpc, FishingOperationState operation,
            IFishingPresetGenerator generator, ILogger<DoFish> logger)
            : this(autoHookIpc, operation, generator, logger, HasCompletedObjectives)
        {
        }

        internal DoFish(IAutoHookIpc autoHookIpc, FishingOperationState operation,
            IFishingPresetGenerator generator, ILogger<DoFish> logger, Func<FishTask, bool> objectivesSatisfied,
            TimeProvider? clock = null)
        {
            _autoHookIpc = autoHookIpc;
            _operation = operation;
            _generator = generator;
            _logger = logger;
            _objectivesSatisfied = objectivesSatisfied;
            _clock = clock ?? TimeProvider.System;
        }

        protected override bool Start()
        {
            _owner = null;
            _preparedPreset = null;
            _completedAt = null;
            if (_operation.HasOperation)
                throw new TaskException(_operation.BlockReason!);
            if (_objectivesSatisfied(Task))
                return false;
            if (Task.GatheredItem == null && !Task.HasCompletionQuestVariablesFlags)
                throw new TaskException("Fishing has no item or quest-variable completion condition");
            if (!_autoHookIpc.SupportsOwnedFishing())
                throw new TaskException("Fishing requires AutoHook with owned fishing API v1; the installed provider is missing or unsupported");
            if (_autoHookIpc.GetFishingBusy() != false)
                throw new TaskException("AutoHook is busy or unavailable; finish manual fishing before starting quest fishing");

            if (Task.PreparedPreset == null)
            {
                // Generate and validate locally before unmounting, changing job or touching provider settings.
                if (!FishingData.FishingPresets.TryGetValue((QuestId)Task.Quest.Id, out string? preset))
                    preset = Task.GatheredItem?.FishingOptions?.Preset ?? _generator.CreatePresetFromTask(Task);
                if (string.IsNullOrWhiteSpace(preset))
                    throw new TaskException("The fishing preset is empty");
                _preparedPreset = preset;
                return true;
            }

            AutoHookIpc.EOwnedStartResult result = _autoHookIpc.StartOwnedFishing(Task.PreparedPreset, out string message);
            if (result is AutoHookIpc.EOwnedStartResult.Accepted or AutoHookIpc.EOwnedStartResult.Uncertain)
                _owner = _operation.OperationId;
            if (result != AutoHookIpc.EOwnedStartResult.Accepted || !_owner.HasValue)
                throw new TaskException($"AutoHook did not acknowledge fishing startup ({result}): {message}");
            _logger.LogInformation("Started owned fishing request {OperationId} for quest {QuestId}", _owner, Task.Quest.Id);
            return true;
        }

        public IEnumerable<ITask> CreateExtraTasks()
        {
            yield return new MountStep.UnmountTask();
            yield return new SwitchClassJob.Task(Job.FSH);
            yield return Task with { PreparedPreset = _preparedPreset! };
        }

        public override ETaskResult Update()
        {
            if (_preparedPreset != null)
                return ETaskResult.CreateNewTasks;
            if (!_owner.HasValue || _operation.OperationId != _owner)
                throw new TaskException("AutoHook fishing ownership was lost");

            if (_operation.IsTerminal)
            {
                if (_operation.Status == ExternalOperationStatus.Succeeded && _objectivesSatisfied(Task))
                {
                    _operation.TryReleaseTerminal(_owner);
                    return ETaskResult.TaskComplete;
                }
                _completedAt ??= _clock.GetTimestamp();
                if (_operation.Status == ExternalOperationStatus.Succeeded &&
                    _clock.GetElapsedTime(_completedAt.Value) < TimeSpan.FromSeconds(2))
                    return ETaskResult.StillRunning;
                string message = _operation.Message;
                _operation.TryReleaseTerminal(_owner);
                throw new TaskException($"AutoHook stopped before the fishing objective was verified: {message}");
            }
            if (_operation.RequiresReconciliation)
                throw new TaskException(_operation.BlockReason!);
            if (_objectivesSatisfied(Task))
                _autoHookIpc.RequestStop();
            return ETaskResult.StillRunning;
        }

        public void StopNow()
        {
            if (_owner.HasValue && _operation.OperationId == _owner)
                _autoHookIpc.CancelOwned();
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal static bool ObjectivesSatisfied(FishTask task, bool matchesQuestWork, int eligibleItemCount) =>
        task.HasCompletionQuestVariablesFlags && matchesQuestWork ||
        task.GatheredItem is { } item && eligibleItemCount >= item.ItemCount;

    private static unsafe bool HasCompletedObjectives(FishTask task)
    {
        // Matching quest flags are authoritative even for quests whose fish are consumed or are key items.
        if (task.HasCompletionQuestVariablesFlags && QuestFunctions.GetQuestProgressInfo(task.Quest.Id) is { } work &&
            QuestWorkUtils.MatchesQuestWork(task.CompletionQuestVariablesFlags, work))
            return true;
        if (task.GatheredItem is not { } item)
            return false;
        if (item.ItemId == 0 || item.ItemCount <= 0 || item.Collectability > short.MaxValue)
            throw new TaskException("Invalid fishing item requirement");
        InventoryManager* inventory = InventoryManager.Instance();
        if (inventory == null)
            throw new TaskException("Inventory is unavailable; cannot verify the fishing objective");
        int count = CountEligibleItems(item, (id, highQuality, collectability) =>
            inventory->GetInventoryItemCount(id, isHq: highQuality, checkEquipped: false, minCollectability: collectability));
        return ObjectivesSatisfied(task, false, count);
    }

    internal static int CountEligibleItems(GatheredItem item, Func<uint, bool, short, int> count) =>
        item.Collectability > 0
            ? count(checked(item.ItemId + 500000), false, checked((short)item.Collectability))
            : count(item.ItemId, false, 0) + count(item.ItemId, true, 0) + count(checked(item.ItemId + 500000), false, 0);
}
