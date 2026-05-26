using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Interactions;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class AlliedSocietyBatchComplete
{
    internal sealed class Factory(
        QuestController questController,
        QuestFunctions questFunctions,
        Configuration configuration,
        ILogger<Factory> logger)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (sequence.Sequence != 255 ||
                step.InteractionType != EInteractionType.CompleteQuest ||
                step.TurnInQuestId != null ||
                configuration.Advanced.PreventQuestCompletion)
            {
                yield break;
            }

            if (!AlliedSocietyBatch.SupportsBatch(quest))
                yield break;

            EAlliedSociety alliedSociety = quest.Info.AlliedSociety;
            if (!questFunctions.IsAlliedSocietyBatchCoordinationActive(questController.PriorityManager.Quests, alliedSociety))
                yield break;

            int batchCount = 0;
            foreach ((Quest siblingQuest, byte siblingSequence) in questFunctions.GetActiveAlliedSocietyBatch(
                         questController.PriorityManager.Quests, alliedSociety))
            {
                if (siblingQuest.Id == quest.Id ||
                    siblingSequence != 255 ||
                    !questFunctions.IsAlliedSocietyDailyReadyToTurnIn(siblingQuest))
                {
                    continue;
                }

                QuestStep? completeStep = FindCompleteQuestStep(siblingQuest);
                if (completeStep?.DataId == null)
                    continue;

                batchCount++;
                logger.LogInformation("Batch-completing allied society quest {QuestId} ({QuestName}) after {CurrentQuestId}",
                    siblingQuest.Id, siblingQuest.Info.Name, quest.Id);

                yield return new Interact.Task(
                    completeStep.DataId.Value,
                    siblingQuest,
                    EInteractionType.CompleteQuest,
                    completeStep.TargetTerritoryId != null);

                yield return new WaitAtEnd.WaitQuestCompleted(siblingQuest.Id);
                yield return new WaitAtEnd.WaitDelay();
            }

            if (batchCount > 0)
                logger.LogInformation("Queued {Count} allied society batch complete(s) for {Society}", batchCount, alliedSociety);
        }

        private static QuestStep? FindCompleteQuestStep(Quest quest) =>
            quest.FindSequence(255)?.Steps.FirstOrDefault(s => s.InteractionType == EInteractionType.CompleteQuest);
    }
}
