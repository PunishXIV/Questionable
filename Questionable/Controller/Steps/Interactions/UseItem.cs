using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using AethernetShortcut = Questionable.Controller.Steps.Shared.AethernetShortcut;

namespace Questionable.Controller.Steps.Interactions;

internal static class UseItem
{
    internal sealed class Factory(
        IClientState clientState,
        TerritoryData territoryData,
        ILogger<Factory> logger)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.SinglePlayerDuty or EInteractionType.CompleteQuest)
            {
                if (step.ItemId == null)
                    return [];
            }
            else if (step.InteractionType != EInteractionType.UseItem)
                return [];

            ArgumentNullException.ThrowIfNull(step.ItemId);

            if (step.ItemId == QuestStep.VesperBayAetheryteTicket)
            {
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetInventoryItemCount(step.ItemId.Value) == 0)
                        return CreateVesperBayFallbackTask();
                }

                var task = new UseOnSelf(quest.Id, step.ItemId.Value, step.CompletionQuestVariablesFlags);

                int currentStepIndex = sequence.Steps.IndexOf(step);
                QuestStep? nextStep = sequence.Steps.Skip(currentStepIndex + 1).FirstOrDefault();
                Vector3? nextPosition = (nextStep ?? step).Position;
                return
                [
                    task,
                    new WaitCondition.Task(() => clientState.TerritoryType == 140,
                        $"Wait(territory: {territoryData.GetNameAndId(140)})"),
                    new Mount.MountTask(140,
                        nextPosition != null ? Mount.EMountIf.AwayFromPosition : Mount.EMountIf.Always,
                        nextPosition),
                    new MoveTask(140, new(-408.92343f, 23.167036f, -351.16223f), null, 0.25f,
                        DataId: null, DisableNavmesh: true, Sprint: false, Fly: false,
                        InteractionType: EInteractionType.WalkTo)
                ];
            }

            var unmount = new Mount.UnmountTask();
            if (step.GroundTarget == true)
            {
                ITask task;
                if (step.DataId != null)
                    task = new UseOnGround(quest.Id, step.DataId.Value, step.ItemId.Value,
                        step.CompletionQuestVariablesFlags);
                else
                {
                    ArgumentNullException.ThrowIfNull(step.Position);
                    task = new UseOnPosition(quest.Id, step.Position.Value, step.ItemId.Value,
                        step.CompletionQuestVariablesFlags);
                }

                return [unmount, new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(0.5)), task];
            }
            else if (step.DataId != null)
            {
                var task = new UseOnObject(quest.Id, step.DataId.Value, step.ItemId.Value,
                    step.CompletionQuestVariablesFlags);
                return [unmount, task];
            }
            else
            {
                var task = new UseOnSelf(quest.Id, step.ItemId.Value, step.CompletionQuestVariablesFlags);
                return [unmount, task];
            }
        }

        private IEnumerable<ITask> CreateVesperBayFallbackTask()
        {
            logger.LogWarning("No vesper bay aetheryte tickets in inventory, navigating via ferry in Limsa instead");

            uint npcId = 1003540;
            ushort territoryId = 129;
            Vector3 destination = new(-360.9217f, 8f, 38.92566f);
            yield return new AetheryteShortcut.Task(null, null, EAetheryteLocation.Limsa, territoryId);
            yield return new AethernetShortcut.Task(EAetheryteLocation.Limsa, EAetheryteLocation.LimsaArcanist);
            yield return new WaitAtEnd.WaitDelay();
            yield return new MoveTask(territoryId, destination, DataId: npcId, Sprint: false,
                InteractionType: EInteractionType.WalkTo);
            yield return new Interact.Task(npcId, null, EInteractionType.None, true);
        }
    }

    internal interface IUseItemBase : ITask
    {
        ElementId? QuestId { get; }
        uint ItemId { get; }
        IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; }
        bool StartingCombat { get; }
    }

    internal abstract class UseItemExecutorBase<T>(
        QuestFunctions questFunctions,
        ICondition condition,
        ILogger logger) : TaskExecutor<T>
        where T : class, IUseItemBase
    {
        private bool _usedItem;
        private DateTime _continueAt;
        private int _itemCount;

        private ElementId? QuestId => Task.QuestId;
        protected uint ItemId => Task.ItemId;
        private IList<QuestWorkValue?> CompletionQuestVariablesFlags => Task.CompletionQuestVariablesFlags;
        private bool StartingCombat => Task.StartingCombat;

        protected abstract bool UseItem();

        protected override unsafe bool Start()
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                throw new TaskException("No InventoryManager");

            _itemCount = inventoryManager->GetInventoryItemCount(ItemId);
            if (_itemCount == 0)
                throw new TaskException($"Don't have any {ItemId} in inventory (checks NQ only)");

            ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() => _usedItem = UseItem());
            _continueAt = DateTime.Now.Add(GetRetryDelay());
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            if (QuestId is QuestId realQuestId && QuestWorkUtils.HasCompletionFlags(CompletionQuestVariablesFlags))
            {
                QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(realQuestId);
                if (questWork != null &&
                    QuestWorkUtils.MatchesQuestWork(CompletionQuestVariablesFlags, questWork))
                    return ETaskResult.TaskComplete;
            }

            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;

            if (StartingCombat && condition[ConditionFlag.InCombat])
                return ETaskResult.TaskComplete;

            if (ItemId == QuestStep.VesperBayAetheryteTicket && _usedItem)
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager == null)
                {
                    logger.LogWarning("InventoryManager is not available");
                    return ETaskResult.StillRunning;
                }

                int itemCount = inventoryManager->GetInventoryItemCount(ItemId);
                if (itemCount == _itemCount)
                {
                    // TODO Better handling for game-provided errors, i.e. reacting to the 'Could not use' messages. UseItem() is successful in this case (and returns 0)
                    logger.LogInformation(
                        "Attempted to use vesper bay aetheryte ticket, but it didn't consume an item - reattempting next frame");
                    _usedItem = false;
                    return ETaskResult.StillRunning;
                }
            }

            if (!_usedItem)
            {
                ProgressContext = InteractionProgressContext.FromActionUseOrDefault(() => _usedItem = UseItem());
                _continueAt = DateTime.Now.Add(GetRetryDelay());
                return ETaskResult.StillRunning;
            }

            return ETaskResult.TaskComplete;
        }

        private TimeSpan GetRetryDelay()
        {
            if (ItemId == QuestStep.VesperBayAetheryteTicket)
                return TimeSpan.FromSeconds(11);
            else
                return TimeSpan.FromSeconds(5);
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record UseOnGround(
        ElementId? QuestId,
        uint DataId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionQuestVariablesFlags,
        bool StartingCombat = false) : IUseItemBase
    {
        public override string ToString() => $"UseItem({ItemId} on ground at {DataId})";
    }

    internal sealed class UseOnGroundExecutor(
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        ICondition condition,
        ILogger<UseOnGroundExecutor> logger)
        : UseItemExecutorBase<UseOnGround>(questFunctions, condition, logger)
    {
        protected override bool UseItem() => gameFunctions.UseItemOnGround(Task.DataId, ItemId);
    }

    internal sealed record UseOnPosition(
        ElementId? QuestId,
        Vector3 Position,
        uint ItemId,
        IList<QuestWorkValue?> CompletionQuestVariablesFlags,
        bool StartingCombat = false) : IUseItemBase
    {
        public override string ToString() =>
            $"UseItem({ItemId} on ground at {Position.ToString("G", CultureInfo.InvariantCulture)})";
    }

    internal sealed class UseOnPositionExecutor(
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        ICondition condition,
        ILogger<UseOnPosition> logger)
        : UseItemExecutorBase<UseOnPosition>(questFunctions, condition, logger)
    {
        protected override bool UseItem() => gameFunctions.UseItemOnPosition(Task.Position, ItemId);
    }

    internal sealed record UseOnObject(
        ElementId? QuestId,
        uint DataId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionQuestVariablesFlags,
        bool StartingCombat = false) : IUseItemBase
    {
        public override string ToString() => $"UseItem({ItemId} on {DataId})";
    }

    internal sealed class UseOnObjectExecutor(
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<UseOnObject> logger)
        : UseItemExecutorBase<UseOnObject>(questFunctions, condition, logger)
    {
        protected override bool UseItem() => gameFunctions.UseItem(Task.DataId, ItemId);
    }

    internal sealed record UseOnSelf(
        ElementId? QuestId,
        uint ItemId,
        IList<QuestWorkValue?> CompletionQuestVariablesFlags,
        bool StartingCombat = false) : IUseItemBase
    {
        public override string ToString() => $"UseItem({ItemId})";
    }

    internal sealed class UseOnSelfExecutor(
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        ICondition condition,
        ILogger<UseOnSelf> logger)
        : UseItemExecutorBase<UseOnSelf>(questFunctions, condition, logger)
    {
        protected override bool UseItem() => gameFunctions.UseItem(ItemId);
    }
}
