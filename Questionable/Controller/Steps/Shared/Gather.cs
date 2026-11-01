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
        private Guid? _owner;

        public override ETaskResult Update()
        {
            return gatheringController.Update(_owner) switch
            {
                GatheringController.EStatus.Complete => ETaskResult.TaskComplete,
                var _ => ETaskResult.StillRunning
            };
        }

        public void StopNow() => gatheringController.StopOwned(_owner);

        // Gather n Chill handles movement and gathering; combat interrupts would just fight it
        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            return gatheringController.Start(new(Task.GatheredItem.ItemId, Task.GatheredItem.ItemCount,
                Task.GatheredItem.Collectability), out _owner);
        }
    }
}
