namespace Questionable.Controller.Steps;

internal enum ETaskResult
{
    StillRunning,

    TaskComplete,

    /// <summary>
    /// This step is complete, regardless of what any other following tasks would do.
    /// </summary>
    SkipRemainingTasksForStep,

    /// <summary>
    /// Assumes the task executor implements <see cref="IExtraTaskCreator"/>.
    /// </summary>
    CreateNewTasks,

    NextStep,
    End,
}
