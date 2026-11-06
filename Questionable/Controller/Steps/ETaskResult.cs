namespace Questionable.Controller.Steps;

internal enum ETaskResult
{
    StillRunning,

    TaskComplete,

    SkipRemainingTasksForStep,

    // Requires an IExtraTaskCreator executor.
    CreateNewTasks,

    // Clears the queue and regenerates the current step.
    RetryStep,

    NextStep,
    End
}
