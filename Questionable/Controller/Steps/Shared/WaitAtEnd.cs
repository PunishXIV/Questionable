using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class WaitAtEnd
{
    internal sealed class Factory
    (
        IObjectTable objectTable,
        ICondition condition,
        AutoDutyProvider autoDuty,
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
                CompletionFlags task = new((QuestId)quest.Id, step);
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
                        new WaitDelay(TimeSpan.FromSeconds(1)),
                        notInCombat,
                        new WaitDelay(TimeSpan.FromSeconds(1)),
                        Next(quest, sequence)
                    ];

                case EInteractionType.WaitForManualProgress:
                case EInteractionType.Instruction:
                case EInteractionType.Snipe:
                    return [new NextStepOrSequence()];

                case EInteractionType.Duty when !autoDuty.CanRunDuty(step.DutyOptions):
                case EInteractionType.SinglePlayerDuty when !bossModIpc.CanRunQuestBattle(quest.Id, step.SinglePlayerDutyOptions):
                    return [new EndAutomation()];

                case EInteractionType.WalkTo:
                case EInteractionType.Jump:
                    return [Next(quest, sequence)];

                case EInteractionType.WaitForObjectAtPosition:
                case EInteractionType.WaitForNpcAtPosition:
                    if (!step.DataId.HasValue)
                        throw new ArgumentNullException(nameof(step.DataId));
                    if (!step.Position.HasValue)
                        throw new ArgumentNullException(nameof(step.Position));

                    return
                    [
                        new ObjectAtPosition(step.DataId.Value, step.Position.Value, step.NpcWaitDistance ?? 0.5f),
                        new WaitDelay(TimeSpan.FromSeconds(1)),
                        Next(quest, sequence)
                    ];

                case EInteractionType.Interact when step.TargetTerritoryId != null:
                case EInteractionType.UseItem when step.TargetTerritoryId != null:
                    return
                    [
                        WaitUntilMoved(step, step.TargetTerritoryId.Value),
                        new WaitDelay(),
                        Next(quest, sequence)
                    ];

                case EInteractionType.AcceptQuest:
                    {
                        QuestAccepted accept = new(step.PickUpQuestId ?? quest.Id);
                        WaitDelay delay = new(TimeSpan.FromSeconds(1));
                        if (step.PickUpQuestId != null)
                        {
                            if (redoUtil.IsRedoActive()) // Can't accept other quests during NG+
                                return [delay, Next(quest, sequence)];
                            return [accept, delay, Next(quest, sequence)];
                        }

                        return [accept, delay];
                    }

                case EInteractionType.CompleteQuest:
                    {
                        QuestCompleted complete = new(step.TurnInQuestId ?? quest.Id);
                        WaitDelay delay = new(TimeSpan.FromSeconds(1));
                        List<ITask> tasks = [complete, delay, .. RedeemRewardItems.CreateTasks(questData, dataManager)];
                        if (step.TurnInQuestId != null)
                            tasks.Add(Next(quest, sequence));
                        return tasks;
                    }

                case EInteractionType.Interact:
                    return [new WaitDelay { InterruptOnDamage = true }, Next(quest, sequence)];

                default:
                    return [new WaitDelay(), Next(quest, sequence)];
            }
        }

        private ITask WaitUntilMoved(QuestStep step, ushort targetTerritoryId)
        {
            if (step.TerritoryId != targetTerritoryId)
                return new TerritoryEntered(targetTerritoryId);

            Vector3 lastPosition = step.Position ?? objectTable[0]?.Position ?? Vector3.Zero;
            return new WaitCondition.Task(() =>
            {
                Vector3? currentPosition = objectTable[0]?.Position;
                if (currentPosition == null)
                    return false;

                return (lastPosition - currentPosition.Value).Length() > 2;
            }, $"Wait(tp away from {lastPosition.ToString("G5", CultureInfo.InvariantCulture)})");
        }

        private static NextStep Next(Quest quest, QuestSequence sequence) => new(quest.Id, sequence.Sequence);
    }

    internal sealed record WaitDelay(TimeSpan Delay, string? Message,
                   [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) : ITask
    {
        public bool InterruptOnDamage { get; init; }

        public WaitDelay([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            : this(TimeSpan.FromMilliseconds(750), Message: null, file: file, line: line)
        {
        }
        public WaitDelay(TimeSpan Delay, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            : this(Delay, Message: null, file: file, line: line)
        {
        }

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() =>
            $"Wait(seconds: {Delay.TotalSeconds}{(Message != null ? $", message: {Message}" : "")}, {Path.GetFileNameWithoutExtension(file)}:L{line})";
    }

    internal sealed class WaitDelayExecutor : DelayedTaskExecutor<WaitDelay>
    {
        protected override bool StartInternal()
        {
            Delay = Task.Delay;
            return true;
        }

        public override bool ShouldInterruptOnDamage() => Task.InterruptOnDamage;
    }

    internal sealed class NextStepOrSequence : ITask
    {
        public override string ToString() => "Wait(next step or sequence)";
    }

    internal sealed class NextStepOrSequenceExecutor : TaskExecutor<NextStepOrSequence>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record CompletionFlags(QuestId Quest, QuestStep Step) : ITask
    {
        public override string ToString() => $"Wait(QW: {string.Join(", ", Step.CompletionQuestVariablesFlags.Select(x => x?.ToString() ?? "-"))})";
    }

    internal sealed class CompletionFlagsExecutor(IQuestGameState gameState)
        : TaskExecutor<CompletionFlags>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            QuestProgressInfo? questWork = gameState.GetQuestProgressInfo(Task.Quest);
            return questWork != null &&
                   QuestWorkUtils.Matches(Task.Step.CompletionQuestVariablesFlags, questWork)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record ObjectAtPosition
    (
        uint DataId,
        Vector3 Destination,
        float Distance) : ITask
    {
        public override string ToString() => $"WaitObj({DataId} at {Destination.ToString("G5", CultureInfo.InvariantCulture)} < {Distance})";
    }

    internal sealed class ObjectAtPositionExecutor(GameFunctions gameFunctions) : TaskExecutor<ObjectAtPosition>
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

    internal sealed record QuestAccepted(ElementId ElementId) : ITask
    {
        public override string ToString() => $"WaitQuestAccepted({ElementId})";
    }

    internal sealed class QuestAcceptedExecutor(QuestFunctions questFunctions, QuestController questController)
        : TaskExecutor<QuestAccepted>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            if (!questFunctions.IsQuestAccepted(Task.ElementId))
                return ETaskResult.StillRunning;

            questController.TryStopOnQueuedAccept(Task.ElementId);
            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record QuestCompleted(ElementId ElementId) : ITask
    {
        public override string ToString() => $"WaitQuestComplete({ElementId})";
    }

    internal sealed class QuestCompletedExecutor(QuestFunctions questFunctions) : TaskExecutor<QuestCompleted>
    {
        protected override bool Start() => true;

        public override ETaskResult Update() => questFunctions.IsQuestComplete(Task.ElementId) ? ETaskResult.TaskComplete : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record TerritoryEntered
    (
        ushort TerritoryId) : ITask
    {
        public override string ToString() => $"WaitForTerritory({TerritoryId})";
    }

    internal sealed class TerritoryEnteredExecutor(IClientState clientState) : TaskExecutor<TerritoryEntered>
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

    internal sealed record ConditionActive
    (
        ConditionFlag Condition, uint TerritoryType, string? FriendlyText = null) : ITask
    {
        public override string ToString() =>
            $"WaitForConditionActive({FriendlyText ?? $"{Condition.ToFormattedText()}"})";
    }

    internal sealed class ConditionActiveExecutor(ICondition conditions, IClientState clientState) : TaskExecutor<ConditionActive>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            return (conditions[Task.Condition] || clientState.TerritoryType != Task.TerritoryType)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record ConditionCleared
    (
        ConditionFlag Condition, uint TerritoryType, string? FriendlyText = null) : ITask
    {
        public override string ToString() =>
            $"WaitForConditionCleared({FriendlyText ?? $"{Condition.ToFormattedText()}"})";
    }

    internal sealed class ConditionClearedExecutor(ICondition conditions, IClientState clientState) : TaskExecutor<ConditionCleared>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            return (!conditions[Task.Condition] || clientState.TerritoryType != Task.TerritoryType)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
