using Microsoft.Extensions.Logging;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Common;

internal static class NextQuest
{
    internal sealed class Factory(QuestFunctions questFunctions) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.CompleteQuest)
                return null;

            if (step.NextQuestId == null)
                return null;

            if (step.NextQuestId == quest.Id)
                return null;

            // probably irrelevant, since pick up is handled elsewhere (and, in particular, checks for aetherytes and stuff)
            if (questFunctions.GetPriorityQuests(onlyClassAndRoleQuests: true).Contains(step.NextQuestId))
                return null;

            return new SetQuestTask(step.NextQuestId, quest.Id);
        }
    }

    internal sealed record SetQuestTask(ElementId NextQuestId, ElementId CurrentQuestId) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"SetNextQuest({NextQuestId})";
    }

    internal sealed class NextQuestExecutor
    (
        QuestRegistry questRegistry,
        QuestController questController,
        QuestFunctions questFunctions,
        ILogger<NextQuestExecutor> logger) : TaskExecutor<SetQuestTask>
    {
        protected override bool Start()
        {
            var isUnobtainable = questFunctions.IsQuestUnobtainable(Task.NextQuestId, Task.CurrentQuestId);
            (var isLocked, string[]? reasons) = questFunctions.IsQuestLocked(Task.NextQuestId, Task.CurrentQuestId);
            if (questController.AutomationType is QuestController.EAutomationType.SingleQuestA or QuestController.EAutomationType.SingleQuestB)
            {
                logger.LogInformation("Won't set next quest to {QuestId}, automation type is CurrentQuestOnly", Task.NextQuestId);
                questController.SetNextQuest(quest: null);
            }
            else if (isLocked)
            {
                logger.LogInformation("Can't set next quest to {QuestId}, quest is locked. {Reasons}", Task.NextQuestId,
                    (reasons != null ? string.Join(',',reasons) : ""));
                questController.SetNextQuest(quest: null);
            }
            else if (isUnobtainable)
            {
                logger.LogInformation("Can't set next quest to {QuestId}, quest is unobtainable.", Task.NextQuestId);
                questController.SetNextQuest(quest: null);
            }
            else if (questRegistry.TryGetQuest(Task.NextQuestId, out Quest? quest))
            {
                logger.LogInformation("Setting next quest to {QuestId}: '{QuestName}'", Task.NextQuestId, quest.Info.Name);
                questController.SetNextQuest(quest);
            }
            else
            {
                logger.LogInformation("Next quest with id {QuestId} not found", Task.NextQuestId);
                questController.SetNextQuest(quest: null);
            }

            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
