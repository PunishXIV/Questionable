using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using LLib;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Functions;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mount = Questionable.Controller.Steps.Common.Mount;

namespace Questionable.Controller;

internal abstract class MiniTaskController<T> : IDisposable
{
    protected readonly TaskQueue _taskQueue = new();

    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly IServiceProvider _serviceProvider;
    private readonly InterruptHandler _interruptHandler;
    private readonly ILogger<T> _logger;

    private readonly Regex _actionCanceledText;
    private readonly string _eventCanceledText;
    private readonly string _cantExecuteDueToStatusText;

    protected MiniTaskController(IChatGui chatGui, ICondition condition, IServiceProvider serviceProvider,
        InterruptHandler interruptHandler, IDataManager dataManager, ILogger<T> logger)
    {
        _chatGui = chatGui;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _interruptHandler = interruptHandler;
        _condition = condition;

        _eventCanceledText = dataManager.GetString<LogMessage>(1318, x => x.Text)!;
        _actionCanceledText = dataManager.GetRegex<LogMessage>(1314, x => x.Text)!;
        _cantExecuteDueToStatusText = dataManager.GetString<LogMessage>(7728, x => x.Text)!;
        _interruptHandler.Interrupted += HandleInterruption;
    }

    protected virtual void UpdateCurrentTask()
    {
        if (_taskQueue.CurrentTaskExecutor == null)
        {
            if (_taskQueue.TryDequeue(out ITask? upcomingTask))
            {
                try
                {
                    _logger.LogInformation("Starting task {TaskName}", upcomingTask.ToString());
                    ITaskExecutor taskExecutor =
                        _serviceProvider.GetRequiredKeyedService<ITaskExecutor>(upcomingTask.GetType());
                    if (taskExecutor.Start(upcomingTask))
                    {
                        _taskQueue.CurrentTaskExecutor = taskExecutor;
                        return;
                    }
                    else
                    {
                        _logger.LogTrace("Task {TaskName} was skipped", upcomingTask.ToString());
                        return;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to start task {TaskName}", upcomingTask.ToString());
                    _chatGui.PrintError(
                        $"Failed to start task '{upcomingTask}', please check /xllog for details.", CommandHandler.MessageTag, CommandHandler.TagColor);
                    Stop("Task failed to start");
                    return;
                }
            }
            else
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
            _logger.LogError(e, "Failed to update task {TaskName}",
                _taskQueue.CurrentTaskExecutor.CurrentTask.ToString());
            _chatGui.PrintError(
                $"Failed to update task '{_taskQueue.CurrentTaskExecutor.CurrentTask}', please check /xllog for details.", CommandHandler.MessageTag, CommandHandler.TagColor);
            Stop("Task failed to update");
            return;
        }

        switch (result)
        {
            case ETaskResult.StillRunning:
                return;

            case ETaskResult.SkipRemainingTasksForStep:
                _logger.LogInformation("{Task} → {Result}, skipping remaining tasks for step",
                    _taskQueue.CurrentTaskExecutor.CurrentTask, result);
                _taskQueue.CurrentTaskExecutor = null;

                while (_taskQueue.TryDequeue(out ITask? nextTask))
                {
                    if (nextTask is ILastTask or Gather.SkipMarker)
                    {
                        ITaskExecutor taskExecutor =
                            _serviceProvider.GetRequiredKeyedService<ITaskExecutor>(nextTask.GetType());
                        taskExecutor.Start(nextTask);
                        _taskQueue.CurrentTaskExecutor = taskExecutor;
                        return;
                    }
                }

                return;

            case ETaskResult.TaskComplete:
            case ETaskResult.CreateNewTasks:
                _logger.LogInformation("{Task} → {Result}, remaining tasks: {RemainingTaskCount}",
                    _taskQueue.CurrentTaskExecutor.CurrentTask, result, _taskQueue.RemainingTasks.Count());

                OnTaskComplete(_taskQueue.CurrentTaskExecutor.CurrentTask);

                if (result == ETaskResult.CreateNewTasks && _taskQueue.CurrentTaskExecutor is IExtraTaskCreator extraTaskCreator)
                    _taskQueue.EnqueueAll(extraTaskCreator.CreateExtraTasks());

                _taskQueue.CurrentTaskExecutor = null;

                // handled in next update
                return;

            case ETaskResult.NextStep:
                _logger.LogInformation("{Task} → {Result}", _taskQueue.CurrentTaskExecutor.CurrentTask, result);

                var lastTask = (ILastTask)_taskQueue.CurrentTaskExecutor.CurrentTask;
                _taskQueue.CurrentTaskExecutor = null;

                OnNextStep(lastTask);
                return;

            case ETaskResult.End:
                _logger.LogInformation("{Task} → {Result}", _taskQueue.CurrentTaskExecutor.CurrentTask, result);
                _taskQueue.CurrentTaskExecutor = null;
                Stop("Task end");
                return;
        }
    }

    protected virtual void OnTaskComplete(ITask task)
    {
    }

    protected virtual void OnNextStep(ILastTask task)
    {
    }

    public abstract void Stop(string label);

    public virtual IList<string> GetRemainingTaskNames() =>
        _taskQueue.RemainingTasks.Select(x => x.ToString() ?? "?").ToList();

    public void InterruptQueueWithCombat()
    {
        _logger.LogWarning("Interrupted, attempting to resolve (if in combat)");
        if (_condition[ConditionFlag.InCombat])
        {
            List<ITask> tasks = [];
            if (_condition[ConditionFlag.Mounted])
                tasks.Add(new Mount.UnmountTask());

            tasks.Add(Combat.Factory.CreateTask(null, -1, false, EEnemySpawnType.QuestInterruption, [], [], [], null));
            tasks.Add(new WaitAtEnd.WaitDelay());
            _taskQueue.InterruptWith(tasks);
        }
        else
            _taskQueue.InterruptWith([new WaitAtEnd.WaitDelay()]);

        LogTasksAfterInterruption();
    }

    private void InterruptWithoutCombat()
    {
        if (_taskQueue.CurrentTaskExecutor is not SinglePlayerDuty.WaitSinglePlayerDutyExecutor)
        {
            _logger.LogWarning("Interrupted, attempting to redo previous tasks (not in combat)");

            _taskQueue.InterruptWith([new WaitAtEnd.WaitDelay()]);
            LogTasksAfterInterruption();
        }
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
            {
                isHandled = true;
            }
        }

        if (!isHandled)
        {
            if (_actionCanceledText.IsMatch(message.TextValue) &&
                !_condition[ConditionFlag.InFlight] &&
                _taskQueue.CurrentTaskExecutor?.ShouldInterruptOnDamage() == true)
                InterruptQueueWithCombat();
            else if (GameFunctions.GameStringEquals(_cantExecuteDueToStatusText, message.TextValue) ||
                     GameFunctions.GameStringEquals(_eventCanceledText, message.TextValue))
                InterruptWithoutCombat();
        }
    }

    protected virtual void HandleInterruption(object? sender, EventArgs e)
    {
        if (!_condition[ConditionFlag.InFlight] &&
            _taskQueue.CurrentTaskExecutor?.ShouldInterruptOnDamage() == true)
            InterruptQueueWithCombat();
    }

    public virtual void Dispose()
    {
        _interruptHandler.Interrupted -= HandleInterruption;
    }
}
