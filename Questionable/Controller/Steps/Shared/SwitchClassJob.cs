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
            if (classJobUtils.ClassToJobStone(classJob) is (Job job, ushort jobStone))
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

    internal sealed class SwitchClassJobExecutor(ClassJobUtils classJobUtils) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            var result = classJobUtils.SwitchClassJob(Task.ClassJob);
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

        // can we even take damage while switching jobs? we should be out of combat...
        public override bool ShouldInterruptOnDamage() => false;
    }
}
