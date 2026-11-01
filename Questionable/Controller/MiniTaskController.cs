using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;
using MountStep = Questionable.Controller.Steps.Common.MountStep;

namespace Questionable.Controller;

internal abstract class MiniTaskController<T> : IDisposable
{
    private readonly Regex _actionCanceledText;
    private readonly string _cantExecuteDueToStatusText;

    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly string _eventCanceledText;
    private readonly InterruptHandler _interruptHandler;
    private readonly ILogger<T> _logger;
    private readonly IServiceProvider _serviceProvider;
    protected readonly TaskQueue _taskQueue;

    protected MiniTaskController(IChatGui chatGui, ICondition condition, IServiceProvider serviceProvider,
        InterruptHandler interruptHandler, IDataManager dataManager, ILogger<T> logger)
    {
        _chatGui = chatGui;
        _logger = logger;
        _taskQueue = new TaskQueue(logger);
        _serviceProvider = serviceProvider;
        _interruptHandler = interruptHandler;
        _condition = condition;

        _eventCanceledText = DataManagerAdapter.GetString<LogMessage>(dataManager, 1318, x => x.Text)!; // Event canceled.
        _actionCanceledText = DataManagerAdapter.GetRegex<LogMessage>(dataManager, 1314, x => x.Text)!; // Action canceled. You are under attack.
        _cantExecuteDueToStatusText = DataManagerAdapter.GetString<LogMessage>(dataManager, 7728, x => x.Text)!; // Unable to execute command while suffering status affliction.
        _interruptHandler.Interrupted += HandleInterruption;
    }

    public virtual void Dispose() => _interruptHandler.Interrupted -= HandleInterruption;

    // External operations may outlive their executor. Derived controllers can hold replacement
    // work without preventing the current executor from updating and verifying its result.
    protected virtual bool CanStartNextTask() => true;

    protected virtual void UpdateCurrentTask()
    {
        if (_taskQueue.CurrentTaskExecutor == null)
        {
            if (!CanStartNextTask() || !_taskQueue.TryDequeue(out ITask? upcomingTask))
                return;

            try
            {
                _logger.LogInformation("Starting task {TaskName}", upcomingTask.ToString());
                ITaskExecutor taskExecutor =
                    _serviceProvider.GetRequiredKeyedService<ITaskExecutor>(upcomingTask.GetType());
                if (!_taskQueue.StartTask(taskExecutor, upcomingTask))
                    _logger.LogTrace("Task {TaskName} was skipped", upcomingTask.ToString());
            }
            catch (Exception e)
            {
                OnTaskFailure(e, upcomingTask);
                _logger.LogError(e, "Failed to start task {TaskName}", upcomingTask.ToString());
                var msg = _LF("Failed to start task '{0}'", upcomingTask);
                _chatGui.PrintError(
                    _LF("{0} Please check /xllog for more details.", msg), CommandHandler.MessageTag, CommandHandler.TagColor);
                Stop("Task failed to start");
                _serviceProvider.GetRequiredService<NotificationMasterIpc>().NotifyOnFailure(msg);
            }

            return;
        }

        ETaskResult result;
        try
        {
            if (_taskQueue.CurrentTaskExecutor.WasInterrupted())
            {
                InterruptQueueWithCombat();
                return;
            }

            result = _taskQueue.CurrentTaskExecutor.Update();
        }
        catch (Exception e)
        {
            OnTaskFailure(e, _taskQueue.CurrentTaskExecutor.CurrentTask);
            _logger.LogError(e, "Failed to update task {TaskName}",
                _taskQueue.CurrentTaskExecutor.CurrentTask.ToString());
            var msg = _LF("Could not complete '{0}': {1}.", _taskQueue.CurrentTaskExecutor.CurrentTask, e.Message);
            _chatGui.PrintError(
                _LF("{0} Please check /xllog for more details.", msg), CommandHandler.MessageTag, CommandHandler.TagColor);
            Stop("Task failed to update");
            _serviceProvider.GetRequiredService<NotificationMasterIpc>().NotifyOnFailure(msg);
            return;
        }

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
                        ITaskExecutor taskExecutor =
                            _serviceProvider.GetRequiredKeyedService<ITaskExecutor>(nextTask.GetType());
                        try
                        {
                            _taskQueue.StartTask(taskExecutor, nextTask);
                        }
                        catch (Exception e)
                        {
                            OnTaskFailure(e, nextTask);
                            _logger.LogError(e, "Failed to start final task after skipping");
                            Stop("Task failed to start");
                        }
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
        if (_condition[ConditionFlag.InCombat])
        {
            if (_condition[ConditionFlag.Mounted])
                tasks.Add(new MountStep.UnmountTask());

            tasks.Add(Combat.Factory.CreateTask(elementId: null, -1, isLastStep: false, EEnemySpawnType.QuestInterruption, [], [], [], combatItemUse: null));
        }
        tasks.Add(new WaitAtEnd.WaitDelay());
        if (!_taskQueue.InterruptWith(tasks))
        {
            Stop("Task cancellation failed");
            return;
        }

        LogTasksAfterInterruption();
    }

    private void InterruptWithoutCombat()
    {
        // Duty providers handle their own transient status/toast interruptions. Do not
        // cancel the owned run merely because its ownership blocks replacement tasks.
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
            if (_actionCanceledText.IsMatch(message.TextValue) &&
                !_condition[ConditionFlag.InFlight] &&
                _taskQueue.CurrentTaskExecutor?.ShouldInterruptOnDamage() == true)
            {
                InterruptQueueWithCombat();
            }
            else if (GameFunctions.GameStringEquals(_cantExecuteDueToStatusText, message.TextValue) ||
                     GameFunctions.GameStringEquals(_eventCanceledText, message.TextValue))
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
