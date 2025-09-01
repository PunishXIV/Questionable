using Questionable.Controller.Steps.Common;
using Questionable.Model;
using Questionable.Model.Questing;
using System;

namespace Questionable.Controller.Steps.Shared;

internal static class WaitAtStart
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.DelaySecondsAtStart == null)
                return null;

            return new WaitDelay(TimeSpan.FromSeconds(step.DelaySecondsAtStart.Value));
        }
    }


    internal sealed record WaitDelay(TimeSpan Delay) : ITask
    {
        public override string ToString() => $"Wait[S](seconds: {Delay.TotalSeconds})";
    }

    internal sealed class WaitDelayExecutor : AbstractDelayedTaskExecutor<WaitDelay>
    {
        protected override bool StartInternal()
        {
            Delay = Task.Delay;
            return true;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
