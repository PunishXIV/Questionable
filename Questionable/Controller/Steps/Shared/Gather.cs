using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LLib.GameData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
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

            foreach (var itemToGather in step.ItemsToGather)
            {
                yield return new DelayedGatheringTask(itemToGather, quest, sequence.Sequence);
            }
        }
    }

    internal sealed record DelayedGatheringTask(GatheredItem GatheredItem, Quest Quest, byte Sequence) : ITask
    {
        public override string ToString() => $"Gathering(pending for {GatheredItem.ItemId})";
    }

    internal sealed class DelayedGatheringExecutor(
        GatheringPointRegistry gatheringPointRegistry,
        TerritoryData territoryData,
        IClientState clientState,
        IServiceProvider serviceProvider,
        ILogger<DelayedGatheringExecutor> logger) : TaskExecutor<DelayedGatheringTask>, IExtraTaskCreator
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => ETaskResult.CreateNewTasks;

        public IEnumerable<ITask> CreateExtraTasks()
        {
            EClassJob currentClassJob = (EClassJob)clientState.LocalPlayer!.ClassJob.RowId;
            if (!gatheringPointRegistry.TryGetGatheringPointId(Task.GatheredItem.ItemId, currentClassJob,
                    out GatheringPointId? gatheringPointId))
                throw new TaskException($"No gathering point found for item {Task.GatheredItem.ItemId}");

            if (!gatheringPointRegistry.TryGetGatheringPoint(gatheringPointId, out GatheringRoot? gatheringRoot))
                throw new TaskException($"No path found for gathering point {gatheringPointId}");

            if (HasRequiredItems(Task.GatheredItem))
                yield break;

            if (currentClassJob == EClassJob.Miner)
                yield return new Action.TriggerStatusIfMissing(EStatus.Prospect, EAction.Prospect);
            else if (currentClassJob == EClassJob.Botanist)
                yield return new Action.TriggerStatusIfMissing(EStatus.Triangulate, EAction.Triangulate);

            using (var _ = logger.BeginScope("Gathering(inner)"))
            {
                QuestSequence gatheringSequence = new QuestSequence
                {
                    Sequence = 0,
                    Steps = gatheringRoot.Steps
                };
                foreach (var gatheringStep in gatheringSequence.Steps)
                {
                    foreach (var task in serviceProvider.GetRequiredService<TaskCreator>()
                                 .CreateTasks(Task.Quest, Task.Sequence, gatheringSequence, gatheringStep))
                        if (task is WaitAtEnd.NextStep)
                            yield return new SkipMarker();
                        else
                            yield return task;
                }
            }

            ushort territoryId = gatheringRoot.Steps.Last().TerritoryId;
            yield return new WaitCondition.Task(() => clientState.TerritoryType == territoryId,
                $"Wait(territory: {territoryData.GetNameAndId(territoryId)})");

            yield return new WaitNavmesh.Task();

            yield return new GatheringTask(gatheringPointId, Task.GatheredItem);
            yield return new WaitAtEnd.WaitDelay();
        }

        private unsafe bool HasRequiredItems(GatheredItem itemToGather)
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            return inventoryManager != null &&
                   inventoryManager->GetInventoryItemCount(itemToGather.ItemId,
                       minCollectability: (short)itemToGather.Collectability) >=
                   itemToGather.ItemCount;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record GatheringTask(
        GatheringPointId gatheringPointId,
        GatheredItem gatheredItem) : ITask
    {
        public override string ToString()
        {
            if (gatheredItem.Collectability == 0)
                return $"Gather({gatheredItem.ItemCount}x {gatheredItem.ItemId})";
            else
                return
                    $"Gather({gatheredItem.ItemCount}x {gatheredItem.ItemId} {SeIconChar.Collectible.ToIconString()} {gatheredItem.Collectability})";
        }
    }

    internal sealed class StartGathering(GatheringController gatheringController) : TaskExecutor<GatheringTask>,
        IToastAware
    {
        protected override bool Start()
        {
            return gatheringController.Start(new GatheringController.GatheringRequest(Task.gatheringPointId,
                Task.gatheredItem.ItemId, Task.gatheredItem.AlternativeItemId, Task.gatheredItem.ItemCount,
                Task.gatheredItem.Collectability));
        }

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
    }

    /// <summary>
    /// A task that does nothing, but if we're skipping a step, this will be the task next in queue to be executed (instead of progressing to the next step) if gathering.
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
