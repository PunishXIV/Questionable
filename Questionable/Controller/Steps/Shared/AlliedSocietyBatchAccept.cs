using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Interactions;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class AlliedSocietyBatchAccept
{
    internal sealed class Factory(
        QuestController questController,
        QuestFunctions questFunctions,
        ILogger<Factory> logger)
        : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (sequence.Sequence != 0 ||
                step.InteractionType != EInteractionType.AcceptQuest ||
                step.PickUpQuestId != null)
            {
                yield break;
            }

            if (!AlliedSocietyBatch.SupportsBatch(quest))
                yield break;

            if (!questController.PriorityManager.Contains(quest.Id))
                yield break;

            EAlliedSociety alliedSociety = quest.Info.AlliedSociety;
            if (!QuestFunctions.IsAlliedSocietyBatchModeActive(questController.PriorityManager.Quests, alliedSociety))
                yield break;

            int batchCount = 0;
            foreach (Quest siblingQuest in questController.PriorityManager.Quests
                         .Where(AlliedSocietyBatch.SupportsBatch)
                         .Where(q => q.Info.AlliedSociety == alliedSociety))
            {
                if (siblingQuest.Id == quest.Id || !questFunctions.IsReadyToAcceptQuest(siblingQuest.Id))
                    continue;

                QuestStep? acceptStep = FindAcceptQuestStep(siblingQuest);
                if (acceptStep?.DataId == null)
                    continue;

                batchCount++;
                logger.LogInformation("Batch-accepting allied society quest {QuestId} ({QuestName}) after {CurrentQuestId}",
                    siblingQuest.Id, siblingQuest.Info.Name, quest.Id);

                yield return new Interact.Task(
                    acceptStep.DataId.Value,
                    siblingQuest,
                    EInteractionType.AcceptQuest,
                    acceptStep.TargetTerritoryId != null);

                yield return new WaitAtEnd.WaitQuestAccepted(siblingQuest.Id);
                yield return new WaitAtEnd.WaitDelay();
            }

            if (batchCount > 0)
                logger.LogInformation("Queued {Count} allied society batch accept(s) for {Society}", batchCount, alliedSociety);
        }

        private static QuestStep? FindAcceptQuestStep(Quest quest) =>
            quest.FindSequence(0)?.Steps.FirstOrDefault(s => s.InteractionType == EInteractionType.AcceptQuest);
    }
}
