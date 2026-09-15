using Questionable.Model.Questing;
using static Questionable.Controller.Steps.Common.NextQuest;

namespace Questionable.Controller.Steps.Shared;

internal static class CleanUpOtherQuests
{
    internal sealed class CleanUpOtherQuestsFactory(QuestFunctions questFunctions, QuestController questController, QuestRegistry questRegistry) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.CleanUpOtherQuests)
                return null;

            var priorityQuests = questFunctions.NextPriorityQuestsThatCanBeAccepted;
            if (priorityQuests.Count == 0)
                return null;

            ElementId? priorityQuestId = priorityQuests
                .Where(x => x.IsAvailable)
                .Select(x => x.QuestId)
                .FirstOrDefault();
            if (priorityQuestId == null)
                return null;

            // don't start a second priority quest until the first one is resolved
            if (questController.StartedQuest != null && priorityQuestId.Equals(quest.Id))
                return null;

            if (questRegistry.TryGetQuest(priorityQuestId, out var _))
                return new SetQuestTask(priorityQuestId, quest.Id);

            return null;
        }
    }
}
