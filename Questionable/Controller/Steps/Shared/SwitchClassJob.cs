using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class SwitchClassJob
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SwitchClass)
                yield break;

            Job classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();
            if (ClassJobUtils.ClassToJobStone(classJob) is (Job job, ushort jobStone))
            {
                yield return new Task(job);
                yield return new UnequipItem.Task(jobStone);
                yield break;
            }
            yield return new Task(classJob);
        }
    }

    internal sealed record Task(Job ClassJob) : ITask
    {
        public override string ToString() => $"SwitchJob({ClassJob})";
    }

    internal sealed class Executor(ClassJobUtils classJobUtils) : DelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            var result = classJobUtils.SwitchJob(Task.ClassJob);
            if (!result)
                throw new TaskException($"No gearset found for {Task.ClassJob}");
            return !result;
        }

        protected unsafe override ETaskResult UpdateInternal()
        {
            if (PlayerState.Instance()->CurrentClassJobId == (uint)Task.ClassJob)
                return ETaskResult.TaskComplete;
            if (EzThrottler.Throttle("SwitchJob"))
                StartInternal();
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
