using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Action = Questionable.Controller.Steps.Interactions.Action;

namespace Questionable.Controller.Steps.Shared;

internal static class Gather
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Gather)
                yield break;

            foreach (GatheredItem itemToGather in step.ItemsToGather)
                yield return new DelayedGatheringTask(itemToGather, quest, sequence.Sequence, step);
        }
    }

    internal sealed record DelayedGatheringTask(GatheredItem GatheredItem, Quest Quest, byte Sequence, QuestStep Step) : ITask
    {
        public override string ToString() => $"Gathering(pending for {GatheredItem.ItemId})";
    }

    internal sealed class DelayedGatheringExecutor
    (
        GatheringPointRegistry gatheringPointRegistry,
        TerritoryData territoryData,
        IClientState clientState,
        IObjectTable objectTable,
        IServiceProvider serviceProvider,
        ILogger<DelayedGatheringExecutor> logger) : TaskExecutor<DelayedGatheringTask>, IExtraTaskCreator
    {
        public override ETaskResult Update() => ETaskResult.CreateNewTasks;

        public IEnumerable<ITask> CreateExtraTasks()
        {
            Job currentClassJob = (Job)((IPlayerCharacter)objectTable[0]!).ClassJob.RowId;
            GatheringPointId? gatheringPointId;
            if (Task.Step.GatheringPoint is ushort gatheringPoint)
                gatheringPointId = new(gatheringPoint);
            else if (!gatheringPointRegistry.TryGetGatheringPointId(Task.GatheredItem.ItemId, currentClassJob,
                out gatheringPointId))
            {
                throw new TaskException($"No gathering point found for item {Task.GatheredItem.ItemId}");
            }

            if (!gatheringPointRegistry.TryGetGatheringPoint(gatheringPointId, out GatheringRoot? gatheringRoot))
                throw new TaskException($"No path found for gathering point {gatheringPointId.Value}");

            if (HasRequiredItems(Task.GatheredItem))
                yield break;

            if (currentClassJob == Job.MIN)
                yield return new Action.TriggerStatusIfMissing(EStatus.Prospect, EAction.Prospect);
            else if (currentClassJob == Job.BTN)
                yield return new Action.TriggerStatusIfMissing(EStatus.Triangulate, EAction.Triangulate);

            using (IDisposable? _ = logger.BeginScope("Gathering(inner)"))
            {
                QuestSequence gatheringSequence = new()
                {
                    Sequence = 0,
                    Steps = gatheringRoot.Steps
                };
                foreach (QuestStep gatheringStep in gatheringSequence.Steps)
                {
                    foreach (ITask task in serviceProvider.GetRequiredService<TaskCreator>()
                        .CreateTasks(Task.Quest, Task.Sequence, gatheringSequence, gatheringStep))
                    {
                        if (task is WaitAtEnd.NextStep)
                            yield return new SkipMarker();
                        else
                            yield return task;
                    }
                }
            }

            uint territoryId = gatheringRoot.Steps.Last().TerritoryId;
            yield return new WaitCondition.Task(() => clientState.TerritoryType == territoryId,
                $"Wait(territory: {territoryData.GetNameAndId(territoryId)})");

            yield return new WaitNavmesh.Task();

            yield return new GatheringTask(gatheringPointId, Task.GatheredItem);
            yield return new WaitAtEnd.WaitDelay();
        }

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start() => true;

        private unsafe bool HasRequiredItems(GatheredItem itemToGather)
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            return inventoryManager != null &&
                   inventoryManager->GetInventoryItemCount(itemToGather.ItemId,
                       minCollectability: (short)itemToGather.Collectability) >=
                   itemToGather.ItemCount;
        }
    }

    internal sealed record GatheringTask
    (
        GatheringPointId GatheringPointId,
        GatheredItem GatheredItem) : ITask
    {
        public override string ToString()
        {
            if (GatheredItem.Collectability == 0)
                return $"Gather({GatheredItem.ItemCount}x {GatheredItem.ItemId})";
            else
            {
                return
                    $"Gather({GatheredItem.ItemCount}x {GatheredItem.ItemId} {SeIconChar.Collectible.ToIconString()} {GatheredItem.Collectability})";
            }
        }
    }

    internal sealed class StartGathering(GatheringController gatheringController) : TaskExecutor<GatheringTask>,
        IToastAware
    {
        public override ETaskResult Update()
        {
            if (gatheringController.Update() == GatheringController.EStatus.Complete)
                return ETaskResult.TaskComplete;

            return ETaskResult.StillRunning;
        }

        public bool OnErrorToast(SeString message)
        {
            bool isHandled = false;
            gatheringController.OnErrorToast(ref message, ref isHandled);
            return isHandled;
        }

        // we're on a gathering class, so combat doesn't make much sense (we also can't change classes in combat...)
        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            return gatheringController.Start(new(Task.GatheringPointId,
                Task.GatheredItem.ItemId, Task.GatheredItem.AlternativeItemId, Task.GatheredItem.ItemCount,
                Task.GatheredItem.Collectability));
        }
    }

    /// <summary>
    ///     A task that does nothing, but if we're skipping a step, this will be the task next in queue to be executed (instead
    ///     of progressing to the next step) if gathering.
    /// </summary>
    internal sealed class SkipMarker : ITask
    {
        public override string ToString() => "Gather/SkipMarker";
    }

    internal sealed class DoSkip : TaskExecutor<SkipMarker>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
