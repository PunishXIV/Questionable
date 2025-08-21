using Microsoft.Extensions.Logging;
using Questionable.Model;
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

            return new SkipRemainingTasks();
        }
    }

    internal sealed class SkipRemainingTasks : ITask
    {
        public override string ToString() => "StepDisabled";
    }

    internal sealed class SkipDisabledStepsExecutor(ILogger<SkipRemainingTasks> logger) : TaskExecutor<SkipRemainingTasks>
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
