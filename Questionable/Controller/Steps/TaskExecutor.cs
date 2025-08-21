using System;
using System.Collections.Generic;

namespace Questionable.Controller.Steps;

internal interface ITaskExecutor
{
    ITask CurrentTask { get; }
    public InteractionProgressContext? ProgressContext { get; }

    Type GetTaskType();

    bool Start(ITask task);

    bool ShouldInterruptOnDamage();

    bool WasInterrupted();

    ETaskResult Update();
}

internal interface IExtraTaskCreator : ITaskExecutor
{
    IEnumerable<ITask> CreateExtraTasks();
}

internal interface IStoppableTaskExecutor : ITaskExecutor
{
    void StopNow();
}

internal interface IDebugStateProvider : ITaskExecutor
{
    string? GetDebugState();
}

internal abstract class TaskExecutor<T> : ITaskExecutor
    where T : class, ITask
{
    protected T Task { get; set; } = null!;
    public InteractionProgressContext? ProgressContext { get; set; }
    ITask ITaskExecutor.CurrentTask => Task;

    public virtual bool WasInterrupted()
    {
        if (ProgressContext is {} progressContext)
        {
            progressContext.Update();
            return progressContext.WasInterrupted();
        }

        return false;
    }

    public Type GetTaskType() => typeof(T);

    protected abstract bool Start();

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
}
