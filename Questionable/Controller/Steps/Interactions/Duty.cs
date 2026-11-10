using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Shared;
using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Interactions;

internal static class Duty
{
    [Flags]
    internal enum Mode
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

    // AutoDuty would wait forever on a Duty Support queue the duty doesn't have.
    internal static bool NeedsDutyFinder(Mode mode, bool hasSupport) => mode == Mode.Support && !hasSupport;

    // Solo unsynced trials can fail on their mechanics, so they keep Duty Support where they have it.
    internal static bool CanUnsync(EContentType? contentType, bool hasSupport) =>
        contentType != EContentType.Trials || !hasSupport;

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
            Mode dutyMode = Mode.Support;
            if (allowUnsync && configuration.Duties.RunUnsynced && quest.Id is QuestId { Value: >= 357 and <= 360 })
                dutyMode = Mode.Regular;
            else if (variant)
                dutyMode = Mode.Variant;
            else if (allowUnsync && configuration.Duties.RunUnsynced)
            {
                unsafe
                {
                    PlayerState* playerState = PlayerState.Instance();
                    if (playerState != null && territoryData.TryGetCfc(step.DutyOptions.ContentFinderConditionId,
                                                                   out TerritoryData.CfcData? cfcData) &&
                            playerState->CurrentLevel - 20 >= cfcData.ClassJobLevelSync &&
                            CanUnsync(cfcData.ContentType, territoryData.HasDutySupport(cfcData.Id)))
                        dutyMode = Mode.Regular;
                }
            }

            yield return new StartAutoDutyTask(step.DutyOptions.ContentFinderConditionId, dutyMode, allowUnsync,
                step.DutyOptions.Enabled);

            if (!QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
                yield return new WaitAtEnd.NextStepOrSequence();
        }
    }

    internal sealed record StartAutoDutyTask
    (
        uint CfcId,
        Mode Mode,
        bool AllowUnsync = true,
        bool PathEnabled = true)
        : ITask
    {
        public override string ToString() => $"AutoDuty({CfcId}, {Mode})";
    }

    internal sealed class StartAutoDutyExecutor(
        GearStatsCalculator gearStatsCalculator,
        AutoDutyProvider autoDuty,
        TerritoryData territoryData,
        Configuration configuration,
        GameFunctions gameFunctions) : TaskExecutor<StartAutoDutyTask>, IStoppable
    {
        private Guid? _operationId;

        public override ETaskResult Update()
        {
            var operation = autoDuty.Operation;
            if (_operationId == null || operation.Id != _operationId)
                throw new TaskException("AutoDuty is no longer running Questionable's duty.");
            if (operation.NeedsConfirmation || operation.IsAbandoned)
                throw new TaskException(operation.Message);
            if (!operation.IsFinished)
                return ETaskResult.StillRunning;

            bool completed = operation.Status == ExternalOperationStatus.Succeeded;
            string message = operation.Message;
            operation.TryClearFinished(_operationId);
            if (!completed)
                throw new TaskException(message);
            return ETaskResult.TaskComplete;
        }

        public void StopNow()
        {
            if (_operationId != null && autoDuty.Operation.Id == _operationId)
                autoDuty.CancelRun();
        }

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            _operationId = null;
            if (!territoryData.TryGetCfc(Task.CfcId, out var cfcData))
                throw new TaskException("Failed to get territory ID for content finder condition");

            // Otherwise AutoDuty keeps queueing and the duty finder keeps refusing.
            if (GetPlayerLevel() is { } playerLevel && playerLevel > 0 && cfcData.RequiredLevel > playerLevel)
                throw new NeedsPlayerException(_LF("Stopped: '{0}' needs level {1} (you're level {2}).",
                    cfcData.Name, cfcData.RequiredLevel, playerLevel));

            Mode dutyMode = Task.Mode;
            short currentItemLevel = GetItemLevel(gearStatsCalculator) ?? throw new TaskException("Equipped items unavailable");
            if (cfcData.RequiredItemLevel > currentItemLevel)
                throw new NeedsPlayerException(
                    $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}.");
            if (dutyMode == Mode.Regular &&
                (!Task.AllowUnsync || !configuration.Duties.RunUnsynced))
                dutyMode = Mode.Support;
            if (Task.AllowUnsync && configuration.Duties.RunUnsynced &&
                dutyMode == Mode.Support && currentItemLevel - 200 >= cfcData.RequiredItemLevel &&
                CanUnsync(cfcData.ContentType, territoryData.HasDutySupport(cfcData.Id)))
                dutyMode = Mode.Regular;

            if (NeedsDutyFinder(dutyMode, territoryData.HasDutySupport(Task.CfcId)))
            {
                gameFunctions.OpenDutyFinder(Task.CfcId);
                throw new NeedsPlayerException(_LF("Stopped: '{0}' has no Duty Support. Queue for it in the Duty Finder, then start again.",
                    cfcData.Name));
            }

            Guid? previousId = autoDuty.Operation.Id;
            try
            {
                autoDuty.StartDuty(Task.CfcId, dutyMode, Task.PathEnabled);
            }
            finally
            {
                // A lost Run reply can still have started work, so keep ownership.
                if (previousId == null)
                    _operationId = autoDuty.Operation.Id;
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

    internal sealed record OpenDutyFinderTask(uint CfcId) : ITask
    {
        public override string ToString() => $"OpenDutyFinder({CfcId})";
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

            gameFunctions.OpenDutyFinder(Task.CfcId);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record OpenVariantDFTask(uint CfcId) : ITask
    {
        public override string ToString() => $"OpenVariantDF({CfcId})";
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
