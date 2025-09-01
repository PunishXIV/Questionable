using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LLib.Gear;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;

namespace Questionable.Controller.Steps.Interactions;

internal static class Duty
{
    internal sealed class Factory(AutoDutyIpc autoDutyIpc) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty)
                yield break;

            ArgumentNullException.ThrowIfNull(step.DutyOptions);

            if (autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions))
            {
                yield return new StartAutoDutyTask(step.DutyOptions.ContentFinderConditionId);
                yield return new WaitAutoDutyTask(step.DutyOptions.ContentFinderConditionId);
                yield return new WaitAtEnd.WaitNextStepOrSequence();
            }
            else
            {
                if (!step.DutyOptions.LowPriority)
                    yield return new OpenDutyFinderTask(step.DutyOptions.ContentFinderConditionId);
            }
        }
    }

    internal sealed record StartAutoDutyTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"StartAutoDuty({ContentFinderConditionId})";
    }

    internal sealed class StartAutoDutyExecutor(
        GearStatsCalculator gearStatsCalculator,
        AutoDutyIpc autoDutyIpc,
        TerritoryData territoryData,
        IClientState clientState,
        IChatGui chatGui,
        SendNotification.Executor sendNotificationExecutor) : TaskExecutor<StartAutoDutyTask>, IStoppableTaskExecutor
    {
        protected override bool Start()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                    out var cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            unsafe
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager == null)
                    throw new TaskException("Inventory unavailable");

                var equippedItems = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
                if (equippedItems == null)
                    throw new TaskException("Equipped items unavailable");

                var currentItemLevel = gearStatsCalculator.CalculateAverageItemLevel(equippedItems);
                if (cfcData.RequiredItemLevel > currentItemLevel)
                {
                    string errorText =
                        $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}.";
                    if (!sendNotificationExecutor.Start(new SendNotification.Task(EInteractionType.Duty, errorText)))
                        chatGui.PrintError(errorText, CommandHandler.MessageTag, CommandHandler.TagColor);

                    return false;
                }
            }

            autoDutyIpc.StartInstance(Task.ContentFinderConditionId);
            return true;
        }

        public override ETaskResult Update()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                    out var cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            return clientState.TerritoryType == cfcData.TerritoryId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => autoDutyIpc.Stop();

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitAutoDutyTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"Wait(AutoDuty, left instance {ContentFinderConditionId})";
    }

    internal sealed class WaitAutoDutyExecutor(
        AutoDutyIpc autoDutyIpc,
        TerritoryData territoryData,
        IClientState clientState) : TaskExecutor<WaitAutoDutyTask>, IStoppableTaskExecutor
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId,
                    out var cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            return clientState.TerritoryType != cfcData.TerritoryId && autoDutyIpc.IsStopped()
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => autoDutyIpc.Stop();

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record OpenDutyFinderTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"OpenDutyFinder({ContentFinderConditionId})";
    }

    internal sealed class OpenDutyFinderExecutor(
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
