namespace Questionable.Controller.Steps.Common;

internal static class WaitCondition
{
    /// <summary>
    /// Can be used to insert a WaitCondition, or can be used as a wrapper for
    /// another function that should run at a given time within a task queue
    /// </summary>
    /// <param name="Predicate">A function that returns a boolean indicating success</param>
    /// <param name="Description">Shown in the task list ingame. Ideal format: Wait(description)</param>
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
