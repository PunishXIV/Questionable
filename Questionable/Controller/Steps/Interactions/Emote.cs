using System;
using System.Collections.Generic;
using Questionable.Controller.Steps.Common;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Interactions;

internal static class Emote
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest
                or EInteractionType.SinglePlayerDuty)
            {
                if (step.Emote == null)
                    return [];
            }
            else if (step.InteractionType != EInteractionType.Emote)
                return [];

            ArgumentNullException.ThrowIfNull(step.Emote);

            var unmount = new Mount.UnmountTask();
            if (step.DataId != null)
            {
                var task = new UseOnObject(step.Emote.Value, step.DataId.Value);
                return [unmount, task];
            }
            else
            {
                var task = new UseOnSelf(step.Emote.Value);
                return [unmount, task];
            }
        }
    }

    internal sealed record UseOnObject(EEmote Emote, uint DataId) : ITask
    {
        public override string ToString() => $"Emote({Emote} on {DataId})";
    }

    internal sealed class UseOnObjectExecutor(ChatFunctions chatFunctions)
        : AbstractDelayedTaskExecutor<UseOnObject>
    {
        protected override bool StartInternal()
        {
            chatFunctions.UseEmote(Task.DataId, Task.Emote);
            return true;
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
