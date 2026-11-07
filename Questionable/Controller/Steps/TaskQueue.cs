using System.Diagnostics.CodeAnalysis;
namespace Questionable.Controller.Steps;

internal sealed class TaskQueue(ILogger logger)
{
    private readonly List<ITask> _completedTasks = [];
    private readonly List<ITask> _tasks = [];
    public ITaskExecutor? CurrentTaskExecutor { get; set; }

    public IEnumerable<ITask> RemainingTasks => _tasks;
    public bool AllTasksComplete => CurrentTaskExecutor == null && _tasks.Count == 0;

    public void Enqueue(ITask task) => _tasks.Add(task);

    public void EnqueueAll(IEnumerable<ITask> tasks) => _tasks.InsertRange(0, tasks);

    public bool TryDequeue([NotNullWhen(true)] out ITask? task)
    {
        if (!TryPeek(out task))
            return false;

        if (task.ShouldRedoOnInterrupt())
            _completedTasks.Add(task);

        _tasks.RemoveAt(0);
        return true;
    }

    public bool TryPeek([NotNullWhen(true)] out ITask? task)
    {
        task = _tasks.FirstOrDefault();
        return task != null;
    }

    public bool StartTask(ITaskExecutor executor, ITask task)
    {
        if (CurrentTaskExecutor != null)
            throw new InvalidOperationException("A task is already running.");

        // Keep the executor during startup so partial acquisitions can be cleaned up.
        CurrentTaskExecutor = executor;
        try
        {
            if (executor.Start(task))
                return true;

            CurrentTaskExecutor = null;
            return false;
        }
        catch
        {
            CancelCurrentTask();
            throw;
        }
    }

    public bool CancelCurrentTask()
    {
        ITaskExecutor? executor = CurrentTaskExecutor;
        // Clear it before calling external code, including reentrant or throwing cleanup.
        CurrentTaskExecutor = null;
        try
        {
            if (executor is ITaskExecutor.IStoppable stoppable)
                stoppable.StopNow();
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to cancel task {ExecutorType}", executor?.GetType().FullName?.Split('.')[^1].Replace('+', '.'));
            return false;
        }
    }

    public bool Reset()
    {
        _tasks.Clear();
        _completedTasks.Clear();
        return CancelCurrentTask();
    }

    public bool InterruptWith(List<ITask> interruptionTasks)
    {
        List<ITask?> newTasks =
        [
            ..interruptionTasks,
            .._completedTasks.Where(x => !ReferenceEquals(x, CurrentTaskExecutor?.CurrentTask)),
            CurrentTaskExecutor?.CurrentTask,
            .._tasks
        ];
        if (!Reset())
            return false;
        _tasks.AddRange(newTasks.OfType<ITask>());
        return true;
    }
}
