using System;
using Questionable.Controller.Steps.Common;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Interactions;

internal static class StatusOff
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.StatusOff)
                return null;

            ArgumentNullException.ThrowIfNull(step.Status);
            return new Task(step.Status.Value);
        }
    }

    internal sealed record Task(EStatus Status) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"StatusOff({Status})";
    }

    internal sealed class DoStatusOff(
        GameFunctions gameFunctions)
        : AbstractDelayedTaskExecutor<Task>
    {
        protected override bool StartInternal()
        {
            if (gameFunctions.HasStatus(Task.Status))
                return GameFunctions.RemoveStatus(Task.Status);

            return false;
        }

        public override ETaskResult Update()
        {
            return gameFunctions.HasStatus(Task.Status) ? ETaskResult.StillRunning : ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
