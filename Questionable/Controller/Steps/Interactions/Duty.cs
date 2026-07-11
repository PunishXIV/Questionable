using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Gear;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Interactions;

internal static class Duty
{
    internal sealed class Factory(AutoDutyIpc autoDutyIpc, TerritoryData territoryData, Configuration configuration) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty)
                yield break;

            ArgumentNullException.ThrowIfNull(step.DutyOptions);

            bool allowUnsync = step.DutyOptions.CanUnsync is not false;
            AutoDutyIpc.DutyMode dutyMode = AutoDutyIpc.DutyMode.Support;
            if (allowUnsync && quest.Id is QuestId { Value: >= 357 and <= 360 })
                dutyMode = AutoDutyIpc.DutyMode.Regular;
            else if (quest.Id is QuestId { Value: 4646 or 4733 or 4789 or 5441})
                dutyMode = AutoDutyIpc.DutyMode.Variant;
            else if (allowUnsync && configuration.Duties.RunUnsynced)
            {
                unsafe
                {
                    if (territoryData.TryGetContentFinderCondition(step.DutyOptions.ContentFinderConditionId,
                                                                   out TerritoryData.ContentFinderConditionData? cfcData) &&
                            PlayerState.Instance()->CurrentLevel - 20 >= cfcData.ClassJobLevelSync &&
                            !cfcData.ContentType.Equals(EContentType.Trials))
                        dutyMode = AutoDutyIpc.DutyMode.Regular;
                }
            }

            if (!configuration.Duties.RunInstancedContentWithAutoDuty ||
                !autoDutyIpc.HasPath(step.DutyOptions.ContentFinderConditionId) ||
               (!autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions) && dutyMode is AutoDutyIpc.DutyMode.Support))
            {
                if (!step.DutyOptions.LowPriority)
                    yield return new OpenDutyFinderTask(step.DutyOptions.ContentFinderConditionId);
                yield break;
            }
            yield return new StartAutoDutyTask(step.DutyOptions.ContentFinderConditionId, dutyMode, allowUnsync);
            yield return new WaitAutoDutyTask(step.DutyOptions.ContentFinderConditionId);

            if (!QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
                yield return new WaitAtEnd.WaitNextStepOrSequence();
        }
    }

    internal sealed record StartAutoDutyTask
    (
        uint ContentFinderConditionId,
        AutoDutyIpc.DutyMode DutyMode,
        bool AllowUnsync = true)
        : ITask
    {
        public override string ToString() => $"StartAutoDuty({ContentFinderConditionId}, {DutyMode})";
    }

    internal sealed class StartAutoDutyExecutor
    (
        GearStatsCalculator gearStatsCalculator,
        AutoDutyIpc autoDutyIpc,
        TerritoryData territoryData,
        Configuration configuration,
        IClientState clientState,
        IChatGui chatGui,
        SendNotification.Executor sendNotificationExecutor) : TaskExecutor<StartAutoDutyTask>, IStoppableTaskExecutor
    {
        public override ETaskResult Update()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                out TerritoryData.ContentFinderConditionData? cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            return clientState.TerritoryType == cfcData.TerritoryId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => autoDutyIpc.Stop();

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                out TerritoryData.ContentFinderConditionData? cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            AutoDutyIpc.DutyMode dutyMode = Task.DutyMode;
            unsafe
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager == null)
                    throw new TaskException("Inventory unavailable");

                InventoryContainer* equippedItems = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
                if (equippedItems == null)
                    throw new TaskException("Equipped items unavailable");

                short currentItemLevel = gearStatsCalculator.CalculateAverageItemLevel(equippedItems);
                if (cfcData.RequiredItemLevel > currentItemLevel)
                {
                    string errorText =
                        $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}.";
                    if (!sendNotificationExecutor.Start(new SendNotification.Task(EInteractionType.Duty, errorText)))
                        chatGui.PrintError(errorText, CommandHandler.MessageTag, CommandHandler.TagColor);

                    return false;
                }
                if (Task.AllowUnsync &&
                    configuration.Duties.RunUnsynced &&
                    Task.DutyMode is AutoDutyIpc.DutyMode.Support &&
                    currentItemLevel - 200 >= cfcData.RequiredItemLevel &&
                    !cfcData.ContentType.Equals(EContentType.Trials))
                {
                    dutyMode = AutoDutyIpc.DutyMode.Regular;
                }
            }

            autoDutyIpc.StartInstance(Task.ContentFinderConditionId, dutyMode);
            return true;
        }
    }

    internal sealed record WaitAutoDutyTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"Wait(AutoDuty, left instance {ContentFinderConditionId})";
    }

    internal sealed class WaitAutoDutyExecutor
    (
        AutoDutyIpc autoDutyIpc,
        TerritoryData territoryData,
        IClientState clientState) : TaskExecutor<WaitAutoDutyTask>, IStoppableTaskExecutor
    {
        public override ETaskResult Update()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                out TerritoryData.ContentFinderConditionData? cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            return clientState.TerritoryType != cfcData.TerritoryId && autoDutyIpc.IsStopped()
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => autoDutyIpc.Stop();

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start() => true;
    }

    internal sealed record OpenDutyFinderTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"OpenDutyFinder({ContentFinderConditionId})";
    }

    internal sealed class OpenDutyFinderExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition) : TaskExecutor<OpenDutyFinderTask>
    {
        protected override bool Start()
        {
            if (condition[ConditionFlag.InDutyQueue])
                return false;

            gameFunctions.OpenDutyFinder(Task.ContentFinderConditionId);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
