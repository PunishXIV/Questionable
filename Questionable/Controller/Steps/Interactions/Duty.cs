using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Interactions;

internal static class Duty
{
    [Flags]
    internal enum DutyMode
    {
        None = 0 << 0,
        Support = 1 << 0,
        Trust = 1 << 1,
        Squadron = 1 << 2,
        Regular = 1 << 3,
        Trial = 1 << 4,
        Raid = 1 << 5,
        Variant = 1 << 6,
        NoviceHall = 1 << 7
    }

    internal sealed class Factory(AutoDutyProvider autoDuty, TerritoryData territoryData, Configuration configuration) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty)
                yield break;

            ArgumentNullException.ThrowIfNull(step.DutyOptions);

            bool variant = quest.Id is QuestId { Value: 4646 or 4733 or 4789 or 5441 };
            if (!autoDuty.CanRunDuty(step.DutyOptions))
            {
                if (variant)
                    yield return new OpenVariantDFTask(step.DutyOptions.ContentFinderConditionId);
                else if (!step.DutyOptions.LowPriority)
                    yield return new OpenDutyFinderTask(step.DutyOptions.ContentFinderConditionId);
                yield break;
            }

            bool allowUnsync = step.DutyOptions.CanUnsync is not false;
            DutyMode dutyMode = DutyMode.Support;
            if (allowUnsync && configuration.Duties.RunUnsynced && quest.Id is QuestId { Value: >= 357 and <= 360 })
                dutyMode = DutyMode.Regular;
            else if (variant)
                dutyMode = DutyMode.Variant;
            else if (allowUnsync && configuration.Duties.RunUnsynced)
            {
                unsafe
                {
                    PlayerState* playerState = PlayerState.Instance();
                    if (playerState != null && territoryData.TryGetContentFinderCondition(step.DutyOptions.ContentFinderConditionId,
                                                                   out TerritoryData.ContentFinderConditionData? cfcData) &&
                            playerState->CurrentLevel - 20 >= cfcData.ClassJobLevelSync &&
                            !cfcData.ContentType.Equals(EContentType.Trials))
                        dutyMode = DutyMode.Regular;
                }
            }

            yield return new StartAutoDutyTask(step.DutyOptions.ContentFinderConditionId, dutyMode, allowUnsync,
                step.DutyOptions.Enabled);

            if (!QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
                yield return new WaitAtEnd.WaitNextStepOrSequence();
        }
    }

    internal sealed record StartAutoDutyTask
    (
        uint ContentFinderConditionId,
        DutyMode DutyMode,
        bool AllowUnsync = true,
        bool PathEnabled = true)
        : ITask
    {
        public override string ToString() => $"AutoDuty({ContentFinderConditionId}, {DutyMode})";
    }

    internal sealed class StartAutoDutyExecutor(
        GearStatsCalculator gearStatsCalculator,
        AutoDutyProvider autoDuty,
        TerritoryData territoryData,
        Configuration configuration,
        IChatGui chatGui,
        SendNotification.Executor sendNotificationExecutor) : TaskExecutor<StartAutoDutyTask>, IStoppableTaskExecutor
    {
        private Guid? _owner;

        private void Notify(string text)
        {
            if (!sendNotificationExecutor.Start(new SendNotification.Task(EInteractionType.Duty, text)))
                chatGui.PrintError(text, CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override ETaskResult Update()
        {
            var operation = autoDuty.Operation;
            if (_owner == null || operation.Id != _owner)
                throw new TaskException("AutoDuty is no longer running Questionable's duty.");
            if (operation.NeedsConfirmation || operation.IsAbandoned)
                throw new TaskException(operation.Message);
            if (!operation.IsFinished)
                return ETaskResult.StillRunning;

            bool completed = operation.Status == ExternalOperationStatus.Succeeded;
            string message = operation.Message;
            operation.TryClearFinished(_owner);
            if (!completed)
                throw new TaskException(message);
            return ETaskResult.TaskComplete;
        }

        public void StopNow()
        {
            if (_owner != null && autoDuty.Operation.Id == _owner)
                autoDuty.CancelRun();
        }

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            _owner = null;
            if (!territoryData.TryGetContentFinderCondition(Task.ContentFinderConditionId, out var cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            // Otherwise AutoDuty keeps queueing and the duty finder keeps refusing.
            if (GetPlayerLevel() is { } playerLevel && playerLevel > 0 && cfcData.RequiredLevel > playerLevel)
            {
                string errorText = _LF("Stopped: '{0}' needs level {1} (you're level {2}).",
                    cfcData.Name, cfcData.RequiredLevel, playerLevel);
                Notify(errorText);
                throw new TaskException(errorText);
            }

            DutyMode dutyMode = Task.DutyMode;
            short currentItemLevel = GetItemLevel(gearStatsCalculator) ?? throw new TaskException("Equipped items unavailable");
            if (cfcData.RequiredItemLevel > currentItemLevel)
            {
                string errorText =
                    $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}.";
                Notify(errorText);
                throw new TaskException(errorText);
            }
            if (dutyMode == DutyMode.Regular &&
                (!Task.AllowUnsync || !configuration.Duties.RunUnsynced))
                dutyMode = DutyMode.Support;
            if (Task.AllowUnsync && configuration.Duties.RunUnsynced &&
                dutyMode == DutyMode.Support && currentItemLevel - 200 >= cfcData.RequiredItemLevel &&
                !cfcData.ContentType.Equals(EContentType.Trials))
                dutyMode = DutyMode.Regular;

            Guid? previousOwner = autoDuty.Operation.Id;
            try
            {
                autoDuty.StartDuty(Task.ContentFinderConditionId, dutyMode, Task.PathEnabled);
            }
            finally
            {
                // A lost Run reply can still have started work, so keep ownership.
                if (previousOwner == null)
                    _owner = autoDuty.Operation.Id;
            }
            return true;
        }

        private static unsafe short? GetPlayerLevel()
        {
            PlayerState* playerState = PlayerState.Instance();
            return playerState == null ? null : playerState->CurrentLevel;
        }

        private static unsafe short? GetItemLevel(GearStatsCalculator gearStatsCalculator)
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return null;
            InventoryContainer* equippedItems = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            return equippedItems == null ? null : gearStatsCalculator.CalculateAverageItemLevel(equippedItems);
        }
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

    internal sealed record OpenVariantDFTask(uint ContentFinderConditionId) : ITask
    {
        public override string ToString() => $"OpenVariantDF({ContentFinderConditionId})";
    }

    internal sealed class OpenVariantDFExecutor
    (
        GameFunctions gameFunctions,
        ICondition condition) : TaskExecutor<OpenVariantDFTask>
    {
        protected override bool Start()
        {
            if (condition[ConditionFlag.InDutyQueue])
                return false;

            gameFunctions.OpenVariantDF();
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
