using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;
using MountStep = Questionable.Controller.Steps.Common.MountStep;

namespace Questionable.Controller;

internal abstract class MiniTaskController<T> : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly ITaskInterruptionSource _interruptHandler;
    private readonly TaskToastClassifier _toastClassifier;
    private readonly Action<string> _notifyOnFailure;
    private readonly ILogger<T> _logger;
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;
    protected readonly TaskQueue _taskQueue;

    protected MiniTaskController(IChatGui chatGui, ICondition condition, IServiceProvider serviceProvider,
        ITaskInterruptionSource interruptHandler, TaskToastClassifier toastClassifier,
        Action<string> notifyOnFailure, ILogger<T> logger)
    {
        _chatGui = chatGui;
        _logger = logger;
        _taskQueue = new TaskQueue(logger);
        _serviceProvider = serviceProvider;
        _interruptHandler = interruptHandler;
        _toastClassifier = toastClassifier;
        _notifyOnFailure = notifyOnFailure;
        _condition = condition;

        try
        {
            _interruptHandler.Interrupted += HandleInterruption;
        }
        catch
        {
            try
            {
                // An event accessor can attach the handler before reporting a failure.
                UnsubscribeFromInterruptions();
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(cleanupError, "Unable to roll back task interruption subscription");
            }
            throw;
        }
    }

    public virtual void Dispose() => UnsubscribeFromInterruptions();

    private void UnsubscribeFromInterruptions()
    {
        if (_disposed)
            return;
        _disposed = true;
        _interruptHandler.Interrupted -= HandleInterruption;
    }

    // Lets derived controllers hold back new tasks without stopping the current executor.
    protected virtual bool CanStartNextTask() => true;

    protected virtual void UpdateCurrentTask()
    {
        if (_taskQueue.CurrentTaskExecutor == null)
        {
            if (!CanStartNextTask() || !_taskQueue.TryDequeue(out ITask? upcomingTask))
                return;

            StartTask(upcomingTask);

            return;
        }

        ITaskExecutor currentExecutor = _taskQueue.CurrentTaskExecutor;
        ETaskResult result;
        try
        {
            if (currentExecutor.WasInterrupted())
            {
                InterruptQueueWithCombat();
                return;
            }

            result = currentExecutor.Update();
        }
        catch (Exception e)
        {
            OnTaskFailure(e, currentExecutor.CurrentTask);
            _logger.LogError(e, "Failed to update task {TaskName}",
                currentExecutor.CurrentTask.ToString());
            var msg = _LF("Could not complete '{0}': {1}.", currentExecutor.CurrentTask, e.Message);
            _chatGui.PrintError(
                _LF("{0} Please check /xllog for more details.", msg), CommandHandler.MessageTag, CommandHandler.TagColor);
            Stop("Task failed to update");
            _notifyOnFailure(msg);
            return;
        }

        // Updating a task can stop automation or replace the queue (e.g. stopping on quest acceptance).
        if (!ReferenceEquals(_taskQueue.CurrentTaskExecutor, currentExecutor))
            return;

        switch (result)
        {
            case ETaskResult.StillRunning:
                return;

            case ETaskResult.SkipRemainingTasksForStep:
                _logger.LogInformation("{Task} → {Result}, skipping remaining tasks for step",
                    _taskQueue.CurrentTaskExecutor.CurrentTask, result);
                if (!CanStartNextTask())
                {
                    Stop("Replacement tasks blocked");
                    return;
                }
                if (!CancelCurrentTask())
                    return;

                if (!CanStartNextTask())
                    return;

                while (_taskQueue.TryDequeue(out ITask? nextTask))
                {
                    if (nextTask is ILastTask)
                    {
                        StartTask(nextTask);
                        return;
                    }
                }

                return;

            case ETaskResult.TaskComplete:
            case ETaskResult.CreateNewTasks:
                if (!CanStartNextTask())
                {
                    Stop("Task completed with unresolved operation");
                    return;
                }

                _logger.LogInformation("{Task} → {Result}, remaining tasks: {RemainingTaskCount}",
                    _taskQueue.CurrentTaskExecutor.CurrentTask, result, _taskQueue.RemainingTasks.Count());

                OnTaskComplete(_taskQueue.CurrentTaskExecutor.CurrentTask);

                if (result == ETaskResult.CreateNewTasks && _taskQueue.CurrentTaskExecutor is IExtraTaskCreator extraTaskCreator)
                    _taskQueue.EnqueueAll(extraTaskCreator.CreateExtraTasks());

                _taskQueue.CurrentTaskExecutor = null;

                // handled in next update
                return;

            case ETaskResult.NextStep:
                if (!CanStartNextTask())
                {
                    Stop("Next step blocked");
                    return;
                }

                _logger.LogInformation("{Task} → {Result}", _taskQueue.CurrentTaskExecutor.CurrentTask, result);

                ILastTask lastTask = (ILastTask)_taskQueue.CurrentTaskExecutor.CurrentTask;
                _taskQueue.CurrentTaskExecutor = null;

                OnNextStep(lastTask);
                return;

            case ETaskResult.RetryStep:
                _logger.LogInformation("{Task} → {Result}, clearing queue and retrying current step",
                    _taskQueue.CurrentTaskExecutor.CurrentTask, result);

                if (!CanStartNextTask())
                {
                    Stop("Retry blocked");
                    return;
                }

                if (!_taskQueue.Reset())
                {
                    Stop("Task cancellation failed");
                    return;
                }
                if (CanStartNextTask())
                    OnRetryStep();
                return;

            case ETaskResult.End:
                _logger.LogInformation("{Task} → {Result}", _taskQueue.CurrentTaskExecutor.CurrentTask, result);
                Stop("Task end");
                return;
        }
    }

    private void StartTask(ITask task)
    {
        try
        {
            _logger.LogInformation("Starting task {TaskName}", task.ToString());
            ITaskExecutor taskExecutor = _serviceProvider.GetRequiredKeyedService<ITaskExecutor>(task.GetType());
            if (!_taskQueue.StartTask(taskExecutor, task))
                _logger.LogTrace("Task {TaskName} was skipped", task.ToString());
        }
        catch (Exception e)
        {
            OnTaskFailure(e, task);
            _logger.LogError(e, "Failed to start task {TaskName}", task.ToString());
            var msg = _LF("Failed to start task '{0}'", task);
            _chatGui.PrintError(
                _LF("{0} Please check /xllog for more details.", msg), CommandHandler.MessageTag, CommandHandler.TagColor);
            Stop("Task failed to start");
            _notifyOnFailure(msg);
        }
    }

    protected virtual void OnTaskFailure(Exception exception, ITask task)
    {
    }

    protected virtual void OnTaskComplete(ITask task)
    {
    }

    protected virtual void OnNextStep(ILastTask task)
    {
    }

    protected virtual void OnRetryStep()
    {
        Stop("RetryStep not supported");
    }

    public abstract void Stop(string label);

    protected bool CancelCurrentTask()
    {
        if (_taskQueue.CancelCurrentTask())
            return true;

        Stop("Task cancellation failed");
        return false;
    }

    public virtual IList<string> GetRemainingTaskNames() => _taskQueue.RemainingTasks.Select(x => x.ToString() ?? "?").ToList();

    public void InterruptQueueWithCombat()
    {
        if (!CanStartNextTask())
        {
            Stop("Interruption blocked");
            return;
        }

        _logger.LogWarning("Interrupted, attempting to resolve (if in combat)");
        List<ITask> tasks = [];
        bool inCombat = _condition[ConditionFlag.InCombat];
        if (inCombat)
        {
            if (_condition[ConditionFlag.Mounted])
                tasks.Add(new MountStep.UnmountTask());

            tasks.Add(Combat.Factory.CreateTask(elementId: null, -1, isLastStep: false, EEnemySpawnType.QuestInterruption, [], [], [], combatItemUse: null));
        }
        tasks.Add(new WaitAtEnd.WaitDelay());
        // Damage can arrive before InCombat is set, so check again before replaying the interaction.
        if (!inCombat)
            tasks.Add(new Combat.CheckAfterInterruption());
        if (!_taskQueue.InterruptWith(tasks))
        {
            Stop("Task cancellation failed");
            return;
        }

        LogTasksAfterInterruption();
    }

    private void InterruptWithoutCombat()
    {
        // Unrelated manual toasts must not wake an idle queue or replay completed interactions.
        if (_taskQueue.AllTasksComplete)
            return;

        // Duty providers handle their own interruptions; don't cancel the run for them.
        if (_taskQueue.CurrentTaskExecutor is SinglePlayerDuty.WaitSinglePlayerDutyExecutor
            or Duty.StartAutoDutyExecutor)
            return;

        if (!CanStartNextTask())
        {
            Stop("Interruption blocked");
            return;
        }

        _logger.LogWarning("Interrupted, attempting to redo previous tasks (not in combat)");

        if (!_taskQueue.InterruptWith([new WaitAtEnd.WaitDelay()]))
        {
            Stop("Task cancellation failed");
            return;
        }
        LogTasksAfterInterruption();
    }

    private void LogTasksAfterInterruption()
    {
        _logger.LogInformation("Remaining tasks after interruption:");
        foreach (ITask task in _taskQueue.RemainingTasks)
            _logger.LogInformation("- {TaskName}", task);
    }

    public void OnErrorToast(ref SeString message, ref bool isHandled)
    {
        if (_taskQueue.CurrentTaskExecutor is IToastAware toastAware)
        {
            if (toastAware.OnErrorToast(message))
                isHandled = true;
        }

        if (!isHandled)
        {
            if (_toastClassifier.IsActionCancellation(message.TextValue) &&
                !_condition[ConditionFlag.InFlight] &&
                _taskQueue.CurrentTaskExecutor?.ShouldInterruptOnDamage() == true)
            {
                InterruptQueueWithCombat();
            }
            else if (_toastClassifier.IsEventOrStatusCancellation(message.TextValue))
            {
                InterruptWithoutCombat();
            }
        }
    }

    protected virtual void HandleInterruption(object? sender, EventArgs e)
    {
        if (!_condition[ConditionFlag.InFlight] &&
            _taskQueue.CurrentTaskExecutor?.ShouldInterruptOnDamage() == true)
            InterruptQueueWithCombat();
    }
}
