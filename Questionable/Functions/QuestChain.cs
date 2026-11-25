namespace Questionable.Functions;

internal static class QuestChain
{
    internal static (List<QuestId> Chain, QuestId? Story) Before(QuestId target,
        Func<QuestId, IQuestInfo?> info, Func<PreviousQuestInfo, bool> met)
    {
        List<QuestId> chain = [];
        HashSet<QuestId> seen = [];
        QuestId? story = null;
        Visit(target);
        return (chain, story);

        void Visit(QuestId id)
        {
            if (story != null || !seen.Add(id) || info(id) is not { } quest)
                return;
            if (quest.IsMainScenarioQuest && id != target)
            {
                story = id;
                return;
            }

            List<PreviousQuestInfo> missing = [.. quest.PreviousQuests.Where(x => !met(x))];
            bool anyOne = quest.PreviousQuestJoin == EQuestJoin.AtLeastOne;
            if (!anyOne || missing.Count == quest.PreviousQuests.Count)
                foreach (PreviousQuestInfo previous in anyOne
                             ? missing.OrderBy(x => info(x.QuestId)?.IsMainScenarioQuest == true).Take(1)
                             : missing)
                    Visit(previous.QuestId);
            chain.Add(id);
        }
    }
}
