namespace Questionable.Controller.Steps.Shared;

internal static class StepDisabled
{
    internal sealed class Factory(QuestFunctions questFunctions) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!step.Disabled && (step.InteractionType == EInteractionType.CompleteQuest ||
                                   !questFunctions.OnlyTurnInLeft(quest, sequence.Sequence)))
                return null;

            return new Task();
        }
    }

    internal sealed class Task : ITask
    {
        public override string ToString() => "StepDisabled";
    }

    internal sealed class Executor(ILogger<Executor> logger) : TaskExecutor<Task>
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
