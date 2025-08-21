using System;

namespace Questionable.Controller.Steps.Common;

internal static class WaitCondition
{
    internal sealed record Task(Func<bool> Predicate, string Description) : ITask
    {
        public override string ToString() => Description;
    }

    internal sealed class WaitConditionExecutor : TaskExecutor<Task>
    {
        private DateTime _continueAt = DateTime.MaxValue;

        protected override bool Start() => !Task.Predicate();

        public override ETaskResult Update()
        {
            if (_continueAt == DateTime.MaxValue)
            {
                if (Task.Predicate())
                    _continueAt = DateTime.Now.AddSeconds(0.5);
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
