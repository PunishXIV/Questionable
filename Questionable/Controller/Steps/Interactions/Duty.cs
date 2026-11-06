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
    internal sealed class Factory(AutoDutyIpc autoDutyIpc, TerritoryData territoryData, Configuration configuration) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty)
                yield break;

            ArgumentNullException.ThrowIfNull(step.DutyOptions);

            bool variant = quest.Id is QuestId { Value: 4646 or 4733 or 4789 or 5441 };
            if (!autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions))
            {
                if (variant)
                    yield return new OpenVariantDFTask(step.DutyOptions.ContentFinderConditionId);
                else if (!step.DutyOptions.LowPriority)
                    yield return new OpenDutyFinderTask(step.DutyOptions.ContentFinderConditionId);
                yield break;
            }

            bool allowUnsync = step.DutyOptions.CanUnsync is not false;
            AutoDutyIpc.DutyMode dutyMode = AutoDutyIpc.DutyMode.Support;
            if (allowUnsync && configuration.Duties.RunUnsynced && quest.Id is QuestId { Value: >= 357 and <= 360 })
                dutyMode = AutoDutyIpc.DutyMode.Regular;
            else if (variant)
                dutyMode = AutoDutyIpc.DutyMode.Variant;
            else if (allowUnsync && configuration.Duties.RunUnsynced)
            {
                unsafe
                {
                    PlayerState* playerState = PlayerState.Instance();
                    if (playerState != null && territoryData.TryGetContentFinderCondition(step.DutyOptions.ContentFinderConditionId,
                                                                   out TerritoryData.ContentFinderConditionData? cfcData) &&
                            playerState->CurrentLevel - 20 >= cfcData.ClassJobLevelSync &&
                            !cfcData.ContentType.Equals(EContentType.Trials))
                        dutyMode = AutoDutyIpc.DutyMode.Regular;
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
        AutoDutyIpc.DutyMode DutyMode,
        bool AllowUnsync = true,
        bool PathEnabled = true)
        : ITask
    {
        public override string ToString() => $"AutoDuty({ContentFinderConditionId}, {DutyMode})";
    }

    internal sealed class StartAutoDutyExecutor : TaskExecutor<StartAutoDutyTask>, IStoppableTaskExecutor
    {
        private readonly AutoDutyIpc _autoDutyIpc;
        private readonly Func<uint, TerritoryData.ContentFinderConditionData?> _findContent;
        private readonly Configuration _configuration;
        private readonly Func<short?> _itemLevel;
        private readonly Action<string> _notify;
        private readonly Func<short?> _playerLevel;
        private Guid? _owner;

        public StartAutoDutyExecutor(GearStatsCalculator gearStatsCalculator, AutoDutyIpc autoDutyIpc,
            TerritoryData territoryData, Configuration configuration, IChatGui chatGui,
            SendNotification.Executor sendNotificationExecutor)
            : this(autoDutyIpc, id => territoryData.TryGetContentFinderCondition(id, out var data) ? data : null,
                configuration, () => GetItemLevel(gearStatsCalculator), text =>
                {
                    if (!sendNotificationExecutor.Start(new SendNotification.Task(EInteractionType.Duty, text)))
                        chatGui.PrintError(text, CommandHandler.MessageTag, CommandHandler.TagColor);
                }, GetPlayerLevel)
        {
        }

        internal StartAutoDutyExecutor(AutoDutyIpc autoDutyIpc,
            Func<uint, TerritoryData.ContentFinderConditionData?> findContent, Configuration configuration,
            Func<short?> itemLevel, Action<string> notify, Func<short?>? playerLevel = null)
        {
            _autoDutyIpc = autoDutyIpc;
            _findContent = findContent;
            _configuration = configuration;
            _itemLevel = itemLevel;
            _notify = notify;
            _playerLevel = playerLevel ?? (() => null);
        }

        public override ETaskResult Update()
        {
            var operation = _autoDutyIpc.Operation;
            if (_owner == null || operation.OperationId != _owner)
                throw new TaskException("AutoDuty operation ownership was lost.");
            if (operation.RequiresReconciliation || operation.IsAbandoned)
                throw new TaskException(operation.Message);
            if (!operation.IsTerminal)
                return ETaskResult.StillRunning;

            bool completed = operation.Status == ExternalOperationStatus.Succeeded;
            string message = operation.Message;
            operation.TryReleaseTerminal(_owner);
            if (!completed)
                throw new TaskException(message);
            return ETaskResult.TaskComplete;
        }

        public void StopNow()
        {
            if (_owner != null && _autoDutyIpc.Operation.OperationId == _owner)
                _autoDutyIpc.CancelOwned();
        }

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start()
        {
            _owner = null;
            var cfcData = _findContent(Task.ContentFinderConditionId) ??
                throw new TaskException("Failed to get territory ID for content finder condition");

            // Otherwise AutoDuty keeps queueing and the duty finder keeps refusing.
            if (_playerLevel() is { } playerLevel && playerLevel > 0 && cfcData.RequiredLevel > playerLevel)
            {
                string errorText = _LF("Stopped: '{0}' needs level {1} (you're level {2}).",
                    cfcData.Name, cfcData.RequiredLevel, playerLevel);
                _notify(errorText);
                throw new TaskException(errorText);
            }

            AutoDutyIpc.DutyMode dutyMode = Task.DutyMode;
            short currentItemLevel = _itemLevel() ?? throw new TaskException("Equipped items unavailable");
            if (cfcData.RequiredItemLevel > currentItemLevel)
            {
                string errorText =
                    $"Could not use AutoDuty to queue for {cfcData.Name}, required item level: {cfcData.RequiredItemLevel}, current item level: {currentItemLevel}.";
                _notify(errorText);
                throw new TaskException(errorText);
            }
            if (dutyMode == AutoDutyIpc.DutyMode.Regular &&
                (!Task.AllowUnsync || !_configuration.Duties.RunUnsynced))
                dutyMode = AutoDutyIpc.DutyMode.Support;
            if (Task.AllowUnsync && _configuration.Duties.RunUnsynced &&
                dutyMode == AutoDutyIpc.DutyMode.Support && currentItemLevel - 200 >= cfcData.RequiredItemLevel &&
                !cfcData.ContentType.Equals(EContentType.Trials))
                dutyMode = AutoDutyIpc.DutyMode.Regular;

            Guid? previousOwner = _autoDutyIpc.Operation.OperationId;
            try
            {
                _autoDutyIpc.StartInstance(Task.ContentFinderConditionId, dutyMode, Task.PathEnabled);
            }
            finally
            {
                // A lost Run reply can still have started work, so keep ownership.
                if (previousOwner == null)
                    _owner = _autoDutyIpc.Operation.OperationId;
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
