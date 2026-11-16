namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class DailiesController(
    Configuration configuration,
    AlliedSocietyQuestFunctions alliedSocietyFunctions,
    QuestFunctions questFunctions,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestController questController,
    QuestStarter questStarter,
    IChatGui chatGui,
    ILogger<DailiesController> logger)
{
    // Ixal "Deliverance" isn't a daily.
    private const ushort IxalDelivery = 1569;

    public void Start()
    {
        List<EAlliedSociety> societies = configuration.Dailies.Societies;
        if (societies.Count == 0)
        {
            chatGui.PrintError(_L("Tick the societies for Do dailies in the journal's Allied Societies tab first."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        List<QuestId> quests = Plan(societies, GetDailies, CanAccept, (int)questFunctions.SocietyAllowances);
        foreach (QuestId questId in quests)
            questController.PriorityManager.MarkAcceptOnly(questId);

        logger.LogInformation("Do dailies: queued {Count} to accept: {QuestIds}", quests.Count, string.Join(", ", quests));
        chatGui.Print(_LF("Do dailies: accepting {0} dailies, then doing all accepted society dailies.", quests.Count),
            CommandHandler.MessageTag, CommandHandler.TagColor);
        questStarter.Start(_L("Do dailies"));
    }

    // List order decides who gets the allowances; within a society the highest rank pays the most.
    internal static List<QuestId> Plan(IReadOnlyList<EAlliedSociety> societies,
        Func<EAlliedSociety, IEnumerable<(QuestId Id, EAlliedSocietyRank Rank)>> dailies,
        Func<QuestId, bool> canAccept, int allowances)
    {
        List<QuestId> picked = [];
        foreach (EAlliedSociety society in societies.Distinct())
        {
            foreach ((QuestId id, _) in dailies(society).OrderByDescending(x => x.Rank))
            {
                if (picked.Count >= allowances)
                    return picked;
                if (canAccept(id))
                    picked.Add(id);
            }
        }

        return picked;
    }

    private IEnumerable<(QuestId Id, EAlliedSocietyRank Rank)> GetDailies(EAlliedSociety society) =>
        alliedSocietyFunctions.GetAvailableQuests(society)
            .Where(x => x.Value != IxalDelivery && questFunctions.IsDailyAlliedSocietyQuest(x))
            .Select(x => (x, ((QuestInfo)questData.GetQuestInfo(x)).AlliedSocietyRank));

    // Same rule as Accept All: another class may have the level, the accept step switches to it.
    private bool CanAccept(QuestId questId) =>
        questRegistry.TryGetQuest(questId, out _) &&
        questFunctions.IsReadyToAcceptQuest(questId, ignoreLevel: true) &&
        questFunctions.HasClassJobAtQuestLevel(questId);
}
