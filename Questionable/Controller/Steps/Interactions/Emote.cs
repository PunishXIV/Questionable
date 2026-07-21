using System;
using System.Collections.Generic;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Shared;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Interactions;

internal static class Emote
{
    internal sealed class Factory(Configuration configuration) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
                or EInteractionType.SinglePlayerDuty)
            {
                if (step.Emote == null)
                    yield break;
                if (step.InteractionType is EInteractionType.CompleteQuest)
                {
                    yield return new LogQuestCompletion.Task(quest);
                    if (configuration.Advanced.PreventQuestCompletion)
                    {
                        if (configuration.Advanced.AbandonQuestBeforeCompletion)
                            yield return new AbandonQuest.Task(quest);
                        yield break;
                    }
                }
            }
            else if (step.InteractionType != EInteractionType.Emote)
                yield break;
            if (!step.Emote.HasValue)
                throw new ArgumentNullException(nameof(step.Emote));

            yield return new Mount.UnmountTask();
            if (step.DataId != null)
                yield return new UseOnObject(step.Emote.Value, step.DataId.Value);
            else
                yield return new UseOnSelf(step.Emote.Value);
        }
    }

    internal sealed record UseOnObject(EEmote Emote, uint DataId) : ITask
    {
        public override string ToString() => $"Emote({Emote} on {DataId})";
    }

    internal sealed class UseOnObjectExecutor(ChatFunctions chatFunctions)
        : AbstractDelayedTaskExecutor<UseOnObject>
    {
        private bool _emoteFired;

        protected override bool StartInternal()
        {
            _emoteFired = chatFunctions.UseEmote(Task.DataId, Task.Emote);
            return true;
        }

        protected override ETaskResult UpdateInternal()
        {
            if (!_emoteFired)
                return ETaskResult.RetryStep;

            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record UseOnSelf(EEmote Emote) : ITask
    {
        public override string ToString() => $"Emote({Emote})";
    }

    internal sealed class UseOnSelfExecutor(ChatFunctions chatFunctions) : AbstractDelayedTaskExecutor<UseOnSelf>
    {
        protected override bool StartInternal()
        {
            chatFunctions.UseEmote(Task.Emote);
            return true;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
