using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class StepDisabled
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!step.Disabled)
                return null;

            return new Task();
        }
    }

    internal sealed class Task : ITask
    {
        public override string ToString() => "StepDisabled";
    }

    internal sealed class Executor(ILogger<Task> logger) : TaskExecutor<Task>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            logger.LogInformation("Skipping step, as it is disabled");
            return ETaskResult.SkipRemainingTasksForStep;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
