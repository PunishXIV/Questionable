namespace Questionable.Controller.Steps.Common;

internal abstract class AbstractDelayedTaskExecutor<T>(TimeSpan delay) : TaskExecutor<T>
where T : class, ITask
{
    private DateTime _continueAt;

    protected AbstractDelayedTaskExecutor()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    protected TimeSpan Delay { get; set; } = delay;

    protected sealed override bool Start()
    {
        bool started = StartInternal();
        _continueAt = DateTime.Now.Add(Delay);
        return started;
    }

    protected abstract bool StartInternal();

    public override ETaskResult Update()
    {
        if (_continueAt >= DateTime.Now)
            return ETaskResult.StillRunning;

        return UpdateInternal();
    }

    protected virtual ETaskResult UpdateInternal() => ETaskResult.TaskComplete;
}
