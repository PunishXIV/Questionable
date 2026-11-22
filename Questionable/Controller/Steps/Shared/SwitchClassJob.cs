using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Interactions;
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

    internal static QuestStep? EarlierSwitch(QuestSequence sequence, QuestStep step) =>
        step.InteractionType == EInteractionType.SwitchClass
            ? null
            : sequence.Steps.Take(sequence.Steps.IndexOf(step)).LastOrDefault(x => x.InteractionType == EInteractionType.SwitchClass);

    internal static unsafe bool IsCurrentJob(Job job) => PlayerState.Instance()->CurrentClassJobId == (uint)job;

    internal static unsafe Job? AcceptedJob(ElementId id, IReadOnlyList<Job> questJobs)
    {
        if (id is not QuestId questId || !QuestManager.Instance()->IsQuestAccepted(questId.Value))
            return null;

        Job accepted = (Job)QuestManager.Instance()->GetQuestById(questId.Value)->AcceptClassJob;
        return NeedsSwitch(accepted, (Job)PlayerState.Instance()->CurrentClassJobId, questJobs) ? accepted : null;
    }

    internal static bool NeedsSwitch(Job accepted, Job current, IReadOnlyList<Job> questJobs) =>
        accepted != Job.ADV && accepted != current && questJobs.Count > 0 && !questJobs.Contains(current);

    internal sealed record Task(Job ClassJob) : ITask
    {
        public override string ToString() => $"SwitchJob({ClassJob})";
    }

    // Waits for the game to report the new job, so the next step doesn't run on the old one.
    internal sealed class Executor(ClassJobUtils classJobUtils) : DelayedTaskExecutor<Task>(TimeSpan.Zero)
    {
        protected override unsafe bool StartInternal()
        {
            if (!classJobUtils.SwitchJob(Task.ClassJob))
                throw new NeedsPlayerException($"No gearset found for {Task.ClassJob}");
            return true;
        }

        private DateTime? _giveUpAt;

        protected unsafe override ETaskResult UpdateInternal()
        {
            if (PlayerState.Instance()->CurrentClassJobId == (uint)Task.ClassJob)
                return ETaskResult.TaskComplete;
            _giveUpAt ??= DateTime.Now.AddSeconds(15);
            if (DateTime.Now > _giveUpAt)
                throw new TaskException($"Could not switch to {Task.ClassJob}");
            if (EzThrottler.Throttle("SwitchJob"))
                StartInternal();
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
