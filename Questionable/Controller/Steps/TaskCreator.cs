using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Questionable.Controller.Steps;

internal sealed class TaskCreator
(
    IServiceProvider serviceProvider,
    TerritoryData territoryData,
    IClientState clientState,
    IChatGui chatGui,
    ILogger<TaskCreator> logger)
{
    private readonly IChatGui _chatGui = chatGui;
    private readonly IClientState _clientState = clientState;
    private readonly ILogger<TaskCreator> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly TerritoryData _territoryData = territoryData;

    public IReadOnlyList<ITask> CreateTasks(Quest quest, byte sequenceNumber, QuestSequence? sequence, QuestStep? step)
    {
        List<ITask> newTasks;
# if !DEBUG
        if (quest.Root.Disabled && sequenceNumber.InRange(1, 2))
        {
            var reason = (quest.Root.Comment ?? "<no reason specified>").Split('\n', 2)[0];
            _chatGui.PrintError($"The quest '{quest.Info.Name}' has been marked as Disabled for the following reason: {reason}",
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _chatGui.PrintError("We recommend you complete this quest manually, as the provided path may not run successfully.",
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _chatGui.PrintError("Thank you for your patience as we expand QST's support to include this quest in a future update.",
                CommandHandler.MessageTag, CommandHandler.TagColor);
        }
# endif
        if (sequence == null)
        {
            if (!quest.Root.Disabled)
            {
                _chatGui.PrintError(
                    $"Path for quest '{quest.Info.Name}' ({quest.Id}) does not contain sequence {sequenceNumber}, please report this: https://github.com/PunishXIV/Questionable/discussions/20",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
            newTasks = [new WaitAtEnd.WaitNextStepOrSequence()];
        }
        else if (step == null)
        {
            newTasks = [new WaitAtEnd.WaitNextStepOrSequence()];
        }
        else
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            newTasks = scope.ServiceProvider.GetRequiredService<IEnumerable<ITaskFactory>>()
                .SelectMany(x =>
                {
                    List<ITask> tasks = x.CreateAllTasks(quest, sequence, step).ToList();

                    if (tasks.Count > 0 && _logger.IsEnabled(LogLevel.Trace))
                    {
                        string factoryName = x.GetType().FullName ?? x.GetType().Name;
                        if (factoryName.Contains('.', StringComparison.Ordinal))
                        {
                            factoryName = factoryName[(factoryName.LastIndexOf('.') + 1)..];
                        }

                        _logger.LogTrace("Factory {FactoryName} created Task {TaskNames}",
                            factoryName, string.Join(", ", tasks.Select(y => y.ToString())));
                    }

                    return tasks;
                })
                .ToList();

            SinglePlayerDuty.StartSinglePlayerDuty? singlePlayerDutyTask = newTasks
                .Where(y => y is SinglePlayerDuty.StartSinglePlayerDuty)
                .Cast<SinglePlayerDuty.StartSinglePlayerDuty>()
                .FirstOrDefault();
            if (singlePlayerDutyTask != null &&
                _territoryData.TryGetContentFinderCondition(singlePlayerDutyTask.ContentFinderConditionId,
                    out TerritoryData.ContentFinderConditionData? cfcData))
            {
                // if we have a single player duty in queue, we check if we're in the matching territory
                // if yes, skip all steps before (e.g. teleporting, waiting for navmesh, moving, interacting)
                if (_clientState.TerritoryType == cfcData.TerritoryId)
                {
                    int index = newTasks.IndexOf(singlePlayerDutyTask);
                    _logger.LogWarning(
                        "Skipping {SkippedTaskCount} out of {TotalCount} tasks, questionable was started while in single player duty",
                        index + 1, newTasks.Count);

                    newTasks.RemoveRange(0, index + 1);
                    _logger.LogInformation("Next actual task: {NextTask}, total tasks left: {RemainingTaskCount}",
                        newTasks.FirstOrDefault(),
                        newTasks.Count);
                }
            }
        }

        if (newTasks.Count == 0)
        {
            _logger.LogInformation("Nothing to execute for step?");
        }
        else
        {
            _logger.LogInformation("Tasks for {QuestId}, {Sequence}, {Step}: {Tasks}",
                quest.Id, sequenceNumber, step != null ? sequence?.Steps.IndexOf(step) : null,
                string.Join(", ", newTasks.Select(x => x.ToString())));
        }

        return newTasks;
    }
}
