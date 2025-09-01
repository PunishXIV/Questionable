using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Questionable.Controller.Steps.Shared;

internal static class WaitAtEnd
{
    internal sealed class Factory(
        IClientState clientState,
        ICondition condition,
        TerritoryData territoryData,
        AutoDutyIpc autoDutyIpc,
        BossModIpc bossModIpc)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.CompletionQuestVariablesFlags.Count == 6 &&
                QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags))
            {
                var task = new WaitForCompletionFlags((QuestId)quest.Id, step);
                var delay = new WaitDelay();
                return [task, delay, Next(quest, sequence)];
            }

            switch (step.InteractionType)
            {
                case EInteractionType.Combat:
                    if (step.EnemySpawnType == EEnemySpawnType.FinishCombatIfAny)
                        return [Next(quest, sequence)];

                    var notInCombat =
                        new WaitCondition.Task(() => !condition[ConditionFlag.InCombat], "Wait(not in combat)");
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
                    ArgumentNullException.ThrowIfNull(step.DataId);
                    ArgumentNullException.ThrowIfNull(step.Position);

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
                        waitInteraction = new WaitCondition.Task(
                            () => clientState.TerritoryType == step.TargetTerritoryId,
                            $"Wait(tp to territory: {territoryData.GetNameAndId(step.TargetTerritoryId.Value)})");
                    }
                    else
                    {
                        Vector3 lastPosition = step.Position ?? clientState.LocalPlayer?.Position ?? Vector3.Zero;
                        waitInteraction = new WaitCondition.Task(() =>
                            {
                                Vector3? currentPosition = clientState.LocalPlayer?.Position;
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
                        var accept = new WaitQuestAccepted(step.PickUpQuestId ?? quest.Id);
                        var delay = new WaitDelay();
                        if (step.PickUpQuestId != null)
                            return [accept, delay, Next(quest, sequence)];
                        else
                            return [accept, delay];
                    }

                case EInteractionType.CompleteQuest:
                    {
                        var complete = new WaitQuestCompleted(step.TurnInQuestId ?? quest.Id);
                        var delay = new WaitDelay();
                        if (step.TurnInQuestId != null)
                            return [complete, delay, Next(quest, sequence)];
                        else
                            return [complete, delay];
                    }

                case EInteractionType.Interact:
                default:
                    return [new WaitDelay(), Next(quest, sequence)];
            }
        }

        private static NextStep Next(Quest quest, QuestSequence sequence)
        {
            return new NextStep(quest.Id, sequence.Sequence);
        }
    }

    internal sealed record WaitDelay(TimeSpan Delay) : ITask
    {
        public WaitDelay()
            : this(TimeSpan.FromSeconds(1))
        {
        }

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"Wait(seconds: {Delay.TotalSeconds})";
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
        public override string ToString() =>
            $"Wait(QW: {string.Join(", ", Step.CompletionQuestVariablesFlags.Select(x => x?.ToString() ?? "-"))})";
    }

    internal sealed class WaitForCompletionFlagsExecutor(QuestFunctions questFunctions)
        : TaskExecutor<WaitForCompletionFlags>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(Task.Quest);
            return questWork != null &&
                   QuestWorkUtils.MatchesQuestWork(Task.Step.CompletionQuestVariablesFlags, questWork)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitObjectAtPosition(
        uint DataId,
        Vector3 Destination,
        float Distance) : ITask
    {
        public override string ToString() =>
            $"WaitObj({DataId} at {Destination.ToString("G", CultureInfo.InvariantCulture)} < {Distance})";
    }

    internal sealed class WaitObjectAtPositionExecutor(GameFunctions gameFunctions) : TaskExecutor<WaitObjectAtPosition>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() =>
            gameFunctions.IsObjectAtPosition(Task.DataId, Task.Destination, Task.Distance)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitQuestAccepted(ElementId ElementId) : ITask
    {
        public override string ToString() => $"WaitQuestAccepted({ElementId})";
    }

    internal sealed class WaitQuestAcceptedExecutor(QuestFunctions questFunctions) : TaskExecutor<WaitQuestAccepted>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            return questFunctions.IsQuestAccepted(Task.ElementId)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
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

        public override ETaskResult Update()
        {
            return questFunctions.IsQuestComplete(Task.ElementId) ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
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
