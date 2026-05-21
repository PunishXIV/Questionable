namespace Questionable.Controller.Steps;

internal enum ETaskResult
{
    StillRunning,

    TaskComplete,

    /// <summary>
    ///     This step is complete, regardless of what any other following tasks would do.
    /// </summary>
    SkipRemainingTasksForStep,

    /// <summary>
    ///     Assumes the task executor implements <see cref="IExtraTaskCreator" />.
    /// </summary>
    CreateNewTasks,

    /// <summary>
    ///     The current step has effectively failed and should be re-run from scratch. The remaining task queue
    ///     is cleared and the controller is asked to regenerate tasks for the current quest step.
    /// </summary>
    RetryStep,

    NextStep,
    End
}
