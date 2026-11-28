namespace Questionable.Controller.Steps.Common;

internal static class NextQuest
{
    internal sealed class Factory(QuestFunctions questFunctions, QuestController questController) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!EndsQuest(quest, sequence, step))
                return null;

            if (step.NextQuestId == null)
                return null;

            if (step.NextQuestId == quest.Id)
                return null;

            if (questFunctions.GetPriorityQuests(onlyClassAndRoleQuests: true).Contains(step.NextQuestId))
                return null;

            if (questController.PriorityManager.Quests.Any(q => questFunctions.IsReadyToAcceptQuest(q.Id)))
                return null;

            return new SetQuestTask(step.NextQuestId, quest.Id);
        }
    }

    // Some quests end on their last talk and have no hand-in step; that step carries the link (4996, 5000).
    internal static bool EndsQuest(Quest quest, QuestSequence sequence, QuestStep step) =>
        step.InteractionType == EInteractionType.CompleteQuest ||
        quest.FindSequence(255) is not { Steps.Count: > 0 } &&
        sequence == quest.Root.QuestSequence.Where(x => x.Steps.Count > 0).MaxBy(x => x.Sequence) &&
        step == sequence.Steps[^1];

    internal sealed record SetQuestTask(ElementId NextQuestId, ElementId CurrentQuestId) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"SetNextQuest({NextQuestId})";
    }

    internal sealed class SetQuestExecutor
    (
        QuestRegistry questRegistry,
        QuestController questController,
        QuestFunctions questFunctions,
        ILogger<SetQuestExecutor> logger) : TaskExecutor<SetQuestTask>
    {
        protected override bool Start()
        {
            var isUnobtainable = questFunctions.IsQuestUnobtainable(Task.NextQuestId, Task.CurrentQuestId);
            (var isLocked, string[]? reasons) = questFunctions.IsQuestLocked(Task.NextQuestId, Task.CurrentQuestId);
            Quest? nextQuest = null;
            if (questController.AutomationType is QuestController.EAutomationType.SingleQuestA or QuestController.EAutomationType.SingleQuestB)
                logger.LogInformation("Won't set next quest to {QuestId}, automation type is CurrentQuestOnly", Task.NextQuestId);

            else if (isLocked)
                logger.LogInformation("Can't set next quest to {QuestId}, quest is locked. {Reasons}", Task.NextQuestId,
                    (reasons != null ? string.Join(',', reasons) : ""));

            else if (isUnobtainable)
                logger.LogInformation("Can't set next quest to {QuestId}, quest is unobtainable.", Task.NextQuestId);

            else if (questRegistry.TryGetQuest(Task.NextQuestId, out nextQuest))
                logger.LogInformation("Setting next quest to {QuestId}: '{QuestName}'", Task.NextQuestId, nextQuest.Info.Name);

            else
                logger.LogInformation("Next quest with id {QuestId} not found", Task.NextQuestId);

            if (nextQuest != null)
                questController.SetNextQuest(quest: nextQuest);

            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
