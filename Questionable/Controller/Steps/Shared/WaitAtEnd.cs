using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.Utils;
namespace Questionable.Controller.Steps.Shared;

internal static class WaitAtEnd
{
    internal sealed class Factory
    (
        IObjectTable objectTable,
        ICondition condition,
        AutoDutyIpc autoDutyIpc,
        BossModIpc bossModIpc,
        RedoUtil redoUtil,
        QuestData questData,
        IDataManager dataManager)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.CompletionQuestVariablesFlags.Count == 6 &&
                QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
            {
                WaitForCompletionFlags task = new((QuestId)quest.Id, step);
                WaitDelay delay = new();
                return [task, delay, Next(quest, sequence)];
            }

            switch (step.InteractionType)
            {
                case EInteractionType.Combat:
                    if (step.EnemySpawnType == EEnemySpawnType.FinishCombatIfAny)
                        return [Next(quest, sequence)];

                    WaitCondition.Task notInCombat = new(() => !condition[ConditionFlag.InCombat], "Wait(not in combat)");
                    return
                    [
                        new WaitDelay(),
                        notInCombat,
                        new WaitDelay(),
                        Next(quest, sequence)
                    ];

                case EInteractionType.WaitForManualProgress:
                case EInteractionType.Instruction:
                case EInteractionType.Snipe:
                    return [new WaitNextStepOrSequence()];

                case EInteractionType.Duty when !autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions):
                case EInteractionType.SinglePlayerDuty when !bossModIpc.IsConfiguredToRunSoloInstance(quest.Id, step.SinglePlayerDutyOptions):
                    return [new EndAutomation()];

                case EInteractionType.WalkTo:
                case EInteractionType.Jump:
                    // no need to wait if we're just moving around
                    return [Next(quest, sequence)];

                case EInteractionType.WaitForObjectAtPosition:
                    if (!step.DataId.HasValue)
                        throw new ArgumentNullException(nameof(step.DataId));
                    if (!step.Position.HasValue)
                        throw new ArgumentNullException(nameof(step.Position));

                    return
                    [
                        new WaitObjectAtPosition(step.DataId.Value, step.Position.Value, step.NpcWaitDistance ?? 0.5f),
                        new WaitDelay(),
                        Next(quest, sequence)
                    ];

                case EInteractionType.Interact when step.TargetTerritoryId != null:
                case EInteractionType.UseItem when step.TargetTerritoryId != null:
                    ITask waitInteraction;
                    if (step.TerritoryId != step.TargetTerritoryId)
                    {
                        // interaction moves to a different territory
                        waitInteraction = new WaitForTerritory(step.TargetTerritoryId.Value);
                    }
                    else
                    {
                        Vector3 lastPosition = step.Position ?? objectTable[0]?.Position ?? Vector3.Zero;
                        waitInteraction = new WaitCondition.Task(() =>
                            {
                                Vector3? currentPosition = objectTable[0]?.Position;
                                if (currentPosition == null)
                                    return false;

                                // interaction moved to elsewhere in the zone
                                // the 'closest' locations are probably
                                //   - waking sands' solar
                                //   - rising stones' solar + dawn's respite
                                return (lastPosition - currentPosition.Value).Length() > 2;
                            }, $"Wait(tp away from {lastPosition.ToString("G", CultureInfo.InvariantCulture)})");
                    }

                    return
                    [
                        waitInteraction,
                        new WaitDelay(),
                        Next(quest, sequence)
                    ];

                case EInteractionType.AcceptQuest:
                    {
                        WaitQuestAccepted accept = new(step.PickUpQuestId ?? quest.Id);
                        WaitDelay delay = new();
                        if (step.PickUpQuestId != null)
                        {
                            if (redoUtil.IsRedoActive()) // Can't accept other quests during NG+
                                return [delay, Next(quest, sequence)];
                            return [accept, delay, Next(quest, sequence)];
                        }
                        else
                            return [accept, delay];
                    }

                case EInteractionType.CompleteQuest:
                    {
                        WaitQuestCompleted complete = new(step.TurnInQuestId ?? quest.Id);
                        WaitDelay delay = new();
                        List<ITask> tasks = [complete, delay, ..RedeemRewardItems.CreateRedeemTasks(questData, dataManager)];
                        if (step.TurnInQuestId != null)
                            tasks.Add(Next(quest, sequence));
                        return tasks;
                    }

                case EInteractionType.Interact:
                default:
                    return [new WaitDelay(), Next(quest, sequence)];
            }
        }

        private static NextStep Next(Quest quest, QuestSequence sequence) => new(quest.Id, sequence.Sequence);
    }

    internal sealed record WaitDelay(TimeSpan Delay, string? Message) : ITask
    {
        public WaitDelay()
            : this(TimeSpan.FromSeconds(1), null)
        {
        }
        public WaitDelay(TimeSpan Delay) : this(Delay, null)
        {
        }

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"Wait(seconds: {Delay.TotalSeconds}{(Message != null ? $", message: {Message}" : "")})";
    }

    internal sealed class WaitDelayExecutor : AbstractDelayedTaskExecutor<WaitDelay>
    {
        protected override bool StartInternal()
        {
            Delay = Task.Delay;
            return true;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed class WaitNextStepOrSequence : ITask
    {
        public override string ToString() => "Wait(next step or sequence)";
    }

    internal sealed class WaitNextStepOrSequenceExecutor : TaskExecutor<WaitNextStepOrSequence>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitForCompletionFlags(QuestId Quest, QuestStep Step) : ITask
    {
        public override string ToString() => $"Wait(QW: {string.Join(", ", Step.CompletionQuestVariablesFlags.Select(x => x?.ToString() ?? "-"))})";
    }

    internal sealed class WaitForCompletionFlagsExecutor()
        : TaskExecutor<WaitForCompletionFlags>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(Task.Quest);
            return questWork != null &&
                   QuestWorkUtils.MatchesQuestWork(Task.Step.CompletionQuestVariablesFlags, questWork)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitObjectAtPosition
    (
        uint DataId,
        Vector3 Destination,
        float Distance) : ITask
    {
        public override string ToString() => $"WaitObj({DataId} at {Destination.ToString("G", CultureInfo.InvariantCulture)} < {Distance})";
    }

    internal sealed class WaitObjectAtPositionExecutor(GameFunctions gameFunctions) : TaskExecutor<WaitObjectAtPosition>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            return gameFunctions.IsObjectAtPosition(Task.DataId, Task.Destination, Task.Distance)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitQuestAccepted(ElementId ElementId) : ITask
    {
        public override string ToString() => $"WaitQuestAccepted({ElementId})";
    }

    internal sealed class WaitQuestAcceptedExecutor(QuestFunctions questFunctions, QuestController questController)
        : TaskExecutor<WaitQuestAccepted>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            if (!questFunctions.IsQuestAccepted(Task.ElementId))
                return ETaskResult.StillRunning;

            questController.TryStopOnQuestAccepted(Task.ElementId);
            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitQuestCompleted(ElementId ElementId) : ITask
    {
        public override string ToString() => $"WaitQuestComplete({ElementId})";
    }

    internal sealed class WaitQuestCompletedExecutor(QuestFunctions questFunctions) : TaskExecutor<WaitQuestCompleted>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => questFunctions.IsQuestComplete(Task.ElementId) ? ETaskResult.TaskComplete : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitForTerritory
    (
        ushort TerritoryId) : ITask
    {
        public override string ToString() => $"WaitForTerritory({TerritoryId})";
    }

    internal sealed class WaitForTerritoryExecutor(IClientState clientState) : TaskExecutor<WaitForTerritory>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            return clientState.TerritoryType == Task.TerritoryId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record NextStep(ElementId ElementId, int Sequence) : ILastTask
    {
        public override string ToString() => "NextStep";
    }

    internal sealed class NextStepExecutor : TaskExecutor<NextStep>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => ETaskResult.NextStep;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed class EndAutomation : ILastTask
    {
        public ElementId ElementId => throw new InvalidOperationException();
        public int Sequence => throw new InvalidOperationException();

        public override string ToString() => "EndAutomation";
    }

    internal sealed class EndAutomationExecutor : TaskExecutor<EndAutomation>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => ETaskResult.End;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
