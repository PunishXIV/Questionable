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

    internal const int MinLevelGap = 10;
    internal const int MaxLevelGap = 30;

    internal static bool MeetsLevelGap(int playerLevel, int syncLevel, int gap) =>
        playerLevel - Math.Clamp(gap, MinLevelGap, MaxLevelGap) >= syncLevel;

    internal sealed class Factory(AutoDutyProvider autoDuty, TerritoryData territoryData, Configuration configuration,
        StoryCatchUp storyCatchUp) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty)
                yield break;

            if (step.DutyOptions == null)
                throw new ArgumentException($"{nameof(step.DutyOptions)} is missing", nameof(step));

            bool variant = quest.Id is QuestId { Value: 4646 or 4733 or 4789 or 5441 };
            if (!autoDuty.CanRunDuty(step.DutyOptions))
            {
                if (variant)
                    yield return new OpenVariantDFTask(step.DutyOptions.ContentFinderConditionId);
                else if (!step.DutyOptions.LowPriority)
                    yield return new OpenDutyFinderTask(step.DutyOptions.ContentFinderConditionId);
                if (configuration.Duties.SetAsideManualDuties)
                    yield return new SetAsideTask(quest.Id, sequence.Sequence, step.DutyOptions.ContentFinderConditionId);
                yield break;
            }

            if (storyCatchUp.Enabled && territoryData.TryGetCfc(step.DutyOptions.ContentFinderConditionId, out TerritoryData.CfcData? duty) &&
                StartAutoDutyExecutor.GetPlayerLevel() is { } level and > 0 && CatchUp(duty, level) is { } catchUp)
            {
                foreach (ITask task in catchUp)
                    yield return task;
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
                            MeetsLevelGap(playerState->CurrentLevel, cfcData.ClassJobLevelSync,
                                configuration.Duties.UnsyncLevelGap))
                        dutyMode = Mode.Regular;
                }
            }

            yield return new StartAutoDutyTask(step.DutyOptions.ContentFinderConditionId, dutyMode, allowUnsync,
                step.DutyOptions.Enabled);

            if (!QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
                yield return new WaitAtEnd.NextStepOrSequence();
        }

        private List<ITask>? CatchUp(TerritoryData.CfcData duty, int level)
        {
            if (duty.RequiredLevel > level)
                return storyCatchUp.LevelRun(_LF("'{0}' needs level {1} (you're level {2})", duty.Name, duty.RequiredLevel, level),
                    level) is { } run
                    ? [run, new StoryCatchUp.Replan()]
                    : null;

            return storyCatchUp.ItemLevel() is { } itemLevel && duty.RequiredItemLevel > itemLevel
                ? storyCatchUp.GearUp(duty, level, itemLevel)
                : null;
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
        GameFunctions gameFunctions,
        ICondition condition) : TaskExecutor<StartAutoDutyTask>, IStoppable
    {
        private Guid? _operationId;

        public override ETaskResult Update()
        {
            if (_operationId == null)
            {
                if (condition[ConditionFlag.InCombat])
                    return ETaskResult.StillRunning;
                StartDuty();
            }

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

        public override bool ShouldInterruptOnDamage() => _operationId == null;

        public override bool WasInterrupted() => _operationId == null &&
            (condition[ConditionFlag.InCombat] || base.WasInterrupted());

        protected override bool Start()
        {
            _operationId = null;
            // Keep QST's combat and death handling until AutoDuty can take over.
            if (!condition[ConditionFlag.InCombat])
                StartDuty();
            return true;
        }

        private void StartDuty()
        {
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
                    string.Create(CultureInfo.InvariantCulture, $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}."));
            if (dutyMode == Mode.Regular &&
                (!Task.AllowUnsync || !configuration.Duties.RunUnsynced))
                dutyMode = Mode.Support;
            if (Task.AllowUnsync && configuration.Duties.RunUnsynced &&
                dutyMode == Mode.Support && currentItemLevel - 200 >= cfcData.RequiredItemLevel)
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
        }

        internal static unsafe short? GetPlayerLevel()
        {
            PlayerState* playerState = PlayerState.Instance();
            return playerState == null ? null : playerState->CurrentLevel;
        }

        internal static unsafe short? GetItemLevel(GearStatsCalculator gearStatsCalculator)
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

    internal sealed record SetAsideTask(ElementId QuestId, byte Sequence, uint CfcId) : ITask
    {
        public override string ToString() => $"SetAside({QuestId}, {CfcId})";
    }

    internal sealed class SetAsideExecutor(TerritoryData territoryData, IChatGui chatGui, ILogger<SetAsideExecutor> logger)
        : TaskExecutor<SetAsideTask>
    {
        protected override bool Start()
        {
            SetAsideQuests.Add(Task.QuestId, Task.Sequence);
            string duty = territoryData.GetCfc(Task.CfcId)?.Name ?? Task.CfcId.ToString(CultureInfo.InvariantCulture);
            logger.LogInformation("Setting {QuestId} aside, it waits on {Duty}", Task.QuestId, duty);
            chatGui.Print(_LF("Setting this quest aside: run '{0}' yourself. Questionable carries on with other quests and comes back once it's done.", duty),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
