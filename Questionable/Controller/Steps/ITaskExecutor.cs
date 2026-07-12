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

    internal interface IStoppableTaskExecutor : ITaskExecutor
    {
        void StopNow();
    }

    internal interface IExtraTaskCreator : ITaskExecutor
    {
        IEnumerable<ITask> CreateExtraTasks();
    }

    internal interface IDebugStateProvider : ITaskExecutor
    {
        string? GetDebugState();
    }
}
