using System;
using System.Collections.Generic;
namespace Questionable.Controller.Steps;

internal abstract class TaskExecutor<T> : ITaskExecutor
where T : class, ITask
{
    protected T Task { get; set; } = null!;
    public InteractionProgressContext? ProgressContext { get; set; }
    ITask ITaskExecutor.CurrentTask => Task;

    public virtual bool WasInterrupted()
    {
        if (ProgressContext is { } progressContext)
        {
            progressContext.Update();
            return progressContext.WasInterrupted();
        }

        return false;
    }

    public Type GetTaskType() => typeof(T);

    public bool Start(ITask task)
    {
        if (task is T t)
        {
            Task = t;
            return Start();
        }

        throw new TaskException($"Unable to cast {task.GetType()} to {typeof(T)}");
    }

    public abstract ETaskResult Update();

    public abstract bool ShouldInterruptOnDamage();

    protected abstract bool Start();
}
