using Dalamud.Game.Text;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;

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
            {
                yield return new GatheringTask(itemToGather);
                yield return new WaitAtEnd.WaitDelay();
            }
        }
    }

    internal sealed record GatheringTask(GatheredItem GatheredItem) : ITask
    {
        public override string ToString()
        {
            if (GatheredItem.Collectability == 0)
                return $"Gather({GatheredItem.ItemCount}x {GatheredItem.ItemId})";

            return
                $"Gather({GatheredItem.ItemCount}x {GatheredItem.ItemId} {SeIconChar.Collectible.ToIconString()} {GatheredItem.Collectability})";
        }
    }

    internal sealed class StartGathering(GatheringController gatheringController) : TaskExecutor<GatheringTask>,
        IStoppableTaskExecutor
    {
        public override ETaskResult Update()
        {
            return gatheringController.Update() switch
            {
                GatheringController.EStatus.Complete => ETaskResult.TaskComplete,
                GatheringController.EStatus.Failed => throw new TaskException(
                    $"Gather n Chill stopped before gathering {Task.GatheredItem.ItemCount}x {Task.GatheredItem.ItemId}"),
                var _ => ETaskResult.StillRunning
            };
        }

        public void StopNow() => gatheringController.Stop("StopNow");

        // Gather n Chill handles movement and gathering; combat interrupts would just fight it
        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            return gatheringController.Start(new(Task.GatheredItem.ItemId, Task.GatheredItem.ItemCount,
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
