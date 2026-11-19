namespace Questionable.Controller.Steps;

internal enum ETaskResult
{
    StillRunning,

    TaskComplete,

    SkipRemainingTasksForStep,

    // Requires an IExtraTaskCreator executor.
    CreateNewTasks,

    RetryStep,

    NextStep,
    End
}
