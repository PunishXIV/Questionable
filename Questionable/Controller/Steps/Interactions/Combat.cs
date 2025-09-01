using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Controller.Utils;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Controller.Steps.Interactions;

internal static class Combat
{
    internal sealed class Factory(GameFunctions gameFunctions) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Combat)
                yield break;

            ArgumentNullException.ThrowIfNull(step.EnemySpawnType);

            if (gameFunctions.GetMountId() != Mount128Module.MountId &&
                gameFunctions.GetMountId() != Mount147Module.MountId)
                yield return new Mount.UnmountTask();

            if (step.CombatDelaySecondsAtStart != null)
            {
                yield return new WaitAtStart.WaitDelay(TimeSpan.FromSeconds(step.CombatDelaySecondsAtStart.Value));
            }

            switch (step.EnemySpawnType)
            {
                case EEnemySpawnType.AfterInteraction:
                    ArgumentNullException.ThrowIfNull(step.DataId);

                    yield return new Interact.Task(step.DataId.Value, quest, EInteractionType.None, true);
                    yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));
                    yield return CreateTask(quest, sequence, step);
                    break;

                case EEnemySpawnType.AfterItemUse:
                    ArgumentNullException.ThrowIfNull(step.ItemId);

                    if (step.GroundTarget == true)
                    {
                        if (step.DataId != null)
                            yield return new UseItem.UseOnGround(quest.Id, step.DataId.Value, step.ItemId.Value,
                                step.CompletionQuestVariablesFlags, true);
                        else
                        {
                            ArgumentNullException.ThrowIfNull(step.Position);
                            yield return new UseItem.UseOnPosition(quest.Id, step.Position.Value, step.ItemId.Value,
                                step.CompletionQuestVariablesFlags, true);
                        }
                    }
                    else if (step.DataId != null)
                    {
                        yield return new UseItem.UseOnObject(quest.Id, step.DataId.Value, step.ItemId.Value,
                            step.CompletionQuestVariablesFlags, true);
                    }
                    else
                    {
                        yield return new UseItem.UseOnSelf(quest.Id, step.ItemId.Value,
                            step.CompletionQuestVariablesFlags, true);
                    }

                    yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));
                    yield return CreateTask(quest, sequence, step);
                    break;

                case EEnemySpawnType.AfterAction:
                    ArgumentNullException.ThrowIfNull(step.DataId);
                    ArgumentNullException.ThrowIfNull(step.Action);

                    if (!step.Action.Value.RequiresMount())
                        yield return new Mount.UnmountTask();
                    yield return new Action.UseOnObject(step.DataId.Value, null, step.Action.Value, null);
                    yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));
                    yield return CreateTask(quest, sequence, step);
                    break;

                case EEnemySpawnType.AfterEmote:
                    ArgumentNullException.ThrowIfNull(step.Emote);

                    yield return new Mount.UnmountTask();
                    if (step.DataId != null)
                        yield return new Emote.UseOnObject(step.Emote.Value, step.DataId.Value);
                    else
                        yield return new Emote.UseOnSelf(step.Emote.Value);
                    yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));
                    yield return CreateTask(quest, sequence, step);
                    break;

                case EEnemySpawnType.AutoOnEnterArea:
                    if (step.CombatDelaySecondsAtStart == null)
                        yield return new WaitAtEnd.WaitDelay(TimeSpan.FromSeconds(1));

                    // automatically triggered when entering area, i.e. only unmount
                    yield return CreateTask(quest, sequence, step);
                    break;

                case EEnemySpawnType.OverworldEnemies:
                case EEnemySpawnType.FateEnemies:
                case EEnemySpawnType.FinishCombatIfAny:
                    yield return CreateTask(quest, sequence, step);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(step), $"Unknown spawn type {step.EnemySpawnType}");
            }
        }

        private static Task CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            ArgumentNullException.ThrowIfNull(step.EnemySpawnType);

            bool isLastStep = sequence.Steps.Last() == step;
            return CreateTask(quest.Id,
                sequence.Sequence,
                isLastStep,
                step.EnemySpawnType.Value,
                step.KillEnemyDataIds,
                step.CompletionQuestVariablesFlags,
                step.ComplexCombatData,
                step.CombatItemUse);
        }

        internal static Task CreateTask(ElementId? elementId,
            int sequence,
            bool isLastStep,
            EEnemySpawnType enemySpawnType,
            IList<uint> killEnemyDataIds,
            IList<QuestWorkValue?> completionQuestVariablesFlags,
            IList<ComplexCombatData> complexCombatData,
            CombatItemUse? combatItemUse)
        {
            return new Task(new CombatController.CombatData
            {
                ElementId = elementId,
                Sequence = sequence,
                CompletionQuestVariablesFlags = completionQuestVariablesFlags,
                SpawnType = enemySpawnType,
                KillEnemyDataIds = killEnemyDataIds.ToList(),
                ComplexCombatDatas = complexCombatData.ToList(),
                CombatItemUse = combatItemUse,
            }, completionQuestVariablesFlags, isLastStep);
        }
    }

    internal sealed record Task(
        CombatController.CombatData CombatData,
        IList<QuestWorkValue?> CompletionQuestVariableFlags,
        bool IsLastStep) : ITask
    {
        public override string ToString()
        {
            if (CombatData.SpawnType == EEnemySpawnType.FinishCombatIfAny)
                return "HandleCombat(wait: not in combat, optional)";
            if (QuestWorkUtils.HasCompletionFlags(CompletionQuestVariableFlags))
                return "HandleCombat(wait: QW flags)";
            else if (IsLastStep)
                return "HandleCombat(wait: next sequence)";
            else
                return "HandleCombat(wait: not in combat)";
        }
    }

    internal sealed class HandleCombat(
        CombatController combatController,
        QuestFunctions questFunctions) : TaskExecutor<Task>
    {
        private CombatController.EStatus _status = CombatController.EStatus.NotStarted;

        protected override bool Start() => combatController.Start(Task.CombatData);

        public override ETaskResult Update()
        {
            _status = combatController.Update();
            if (_status != CombatController.EStatus.Complete)
                return ETaskResult.StillRunning;

            // if our quest step has any completion flags, we need to check if they are set
            if (QuestWorkUtils.HasCompletionFlags(Task.CompletionQuestVariableFlags) &&
                Task.CombatData.ElementId is QuestId questId)
            {
                var questWork = questFunctions.GetQuestProgressInfo(questId);
                if (questWork == null)
                    return ETaskResult.StillRunning;

                if (QuestWorkUtils.MatchesQuestWork(Task.CompletionQuestVariableFlags, questWork))
                    return ETaskResult.TaskComplete;
                else
                    return ETaskResult.StillRunning;
            }

            // the last step, by definition, can only be progressed by the game recognizing we're in a new sequence,
            // so this is an indefinite wait
            if (Task.IsLastStep)
                return ETaskResult.StillRunning;
            else
            {
                combatController.Stop("Combat task complete");
                return ETaskResult.TaskComplete;
            }
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
