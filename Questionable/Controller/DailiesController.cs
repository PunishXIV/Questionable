using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class DailiesController(
    Configuration configuration,
    QuestFunctions questFunctions,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestController questController,
    QuestStarter questStarter,
    IClientState clientState,
    IQuestGameState gameState,
    IObjectTable objectTable,
    BossModIpc bossMod,
    IChatGui chatGui,
    ILogger<DailiesController> logger)
    : IDailiesRun
{
    // Ixal "Deliverance" isn't a daily.
    private const ushort IxalDelivery = 1569;
    private DailiesPlan? _plan;
    private EAlliedSociety[] _societies = [];

    public string? StopReason => _plan?.StopReason;

    public bool CanStart => clientState.IsLoggedIn && !questController.IsRunning;

    public void Start()
    {
        if (!CanStart)
        {
            chatGui.Print(_L("Log in and stop the current run before starting dailies."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        List<EAlliedSociety> societies = configuration.Dailies.Societies;
        if (societies.Count == 0)
        {
            chatGui.PrintError(_L("Tick the societies for Do dailies in the journal's Allied Societies tab first."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (!questStarter.CanStartQuest(_L("Do dailies")))
            return;

        _societies = [.. societies.Distinct()];
        _plan = new(_societies);
        if (!questController.BeginDailies(this))
        {
            _plan = null;
            return;
        }
        chatGui.Print(_L("Do dailies: checking rank-ups, then running selected societies in travel order."),
            CommandHandler.MessageTag, CommandHandler.TagColor);
        questStarter.Start(_L("Do dailies"));
        if (questController.IsDailiesRun && questController.AutomationType != QuestController.EAutomationType.Automatic)
            questController.Stop("Dailies start blocked");
    }

    public Quest? Choose(QuestController.QuestProgress? current)
    {
        if (_plan == null)
            return null;
        List<DailiesPlan.Entry> entries = [];
        foreach (EAlliedSociety society in _societies)
        {
            if (questFunctions.GetRankAndReputation(society).Item1 == EAlliedSocietyRank.None)
                continue; // Unlock quests belong to a later slice.
            foreach (QuestInfo info in questData.GetAllByAlliedSociety(society))
            {
                QuestId id = (QuestId)info.QuestId;
                if (id.Value == IxalDelivery ||
                    AlliedSocietyQuestFunctions.UnlockQuests.GetValueOrDefault(society) == id.Value)
                    continue;
                bool accepted = questFunctions.IsQuestAccepted(id);
                bool ready = questFunctions.IsReadyToAcceptQuest(id, ignoreLevel: true) &&
                             questFunctions.HasClassJobAtQuestLevel(id);
                if (!accepted && !ready)
                    continue;
                questRegistry.TryGetQuest(id, out Quest? quest);
                byte sequence = accepted ? gameState.GetQuestSequence(id.Value) : (byte)0;
                QuestSequence? stage = quest?.FindSequence(sequence);
                QuestStep? target = stage?.Steps.FirstOrDefault(x => x.Position != null) ?? stage?.Steps.FirstOrDefault();
                bool craftReady = accepted && sequence == 255 && stage?.Steps.Any(x =>
                    x.InteractionType == EInteractionType.Craft && x.ItemId is { } itemId &&
                    questFunctions.HasCraftMaterials(itemId)) == true;
                string? blocked = quest == null ? _LF("No quest path for {0}.", info.Name) : null;
                if (!info.IsRepeatable && quest != null && quest.AllSteps().Any(x =>
                    x.Sequence.Sequence >= sequence && x.Step.InteractionType == EInteractionType.SinglePlayerDuty &&
                    !bossMod.CanRunQuestBattle(id, x.Step.SinglePlayerDutyOptions)))
                    blocked = _LF("Do dailies stopped: the rank-up quest {0} has a quest battle that cannot run automatically. Complete it manually, then start dailies again.", info.Name);
                entries.Add(new(id, society, info.IsRepeatable, accepted, ready, info.AlliedSocietyRank,
                    sequence, target?.TerritoryId ?? 0, target?.Position, craftReady, blocked));
            }
        }
        DailiesPlan.Entry? next = _plan.Choose(entries, (int)questFunctions.SocietyAllowances,
            gameState.TerritoryType, objectTable.LocalPlayer?.Position, current?.Quest.Id);
        if (next == null && _plan.StopReason is { } reason)
            chatGui.Print(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
        return next != null && questRegistry.TryGetQuest(next.Id, out Quest? chosen) ? chosen : null;
    }

    public void Accepted(ElementId id) => _plan?.Accepted(id);
    public void Completed(ElementId id) => _plan?.Complete(id);

    public EDailiesFailure Fail(ElementId id, string reason)
    {
        EDailiesFailure result = _plan?.Fail(id, reason) ?? EDailiesFailure.Stop;
        string name = questData.GetQuestInfo(id).Name;
        if (result == EDailiesFailure.Retry)
            chatGui.Print(_LF("Do dailies: retrying {0} once. {1}", name, reason), CommandHandler.MessageTag, CommandHandler.TagColor);
        else if (result == EDailiesFailure.Skip)
            chatGui.Print(_LF("Do dailies: leaving {0} unfinished and continuing. {1}", name, reason), CommandHandler.MessageTag, CommandHandler.TagColor);
        return result;
    }

    public void Finish(bool completed)
    {
        DailiesPlan? plan = _plan;
        _plan = null;
        if (plan == null)
            return;
        string status = completed ? _L("finished") : _L("stopped");
        chatGui.Print(_LF("Do dailies {0}: {1} dailies and {2} rank-up quests completed; {3} left unfinished.",
            status, plan.FinishedDailies, plan.FinishedStories, plan.Unfinished), CommandHandler.MessageTag, CommandHandler.TagColor);
        foreach ((QuestId id, string reason) in plan.Skipped)
            chatGui.Print(_LF("{0}: {1}", questData.GetQuestInfo(id).Name, reason), CommandHandler.MessageTag, CommandHandler.TagColor);
        logger.LogInformation("Dailies {Status}: {Dailies} dailies, {Stories} rank-ups, {Skipped} skipped",
            status, plan.FinishedDailies, plan.FinishedStories, plan.Skipped.Count);
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
                if (!picked.Contains(id) && canAccept(id))
                    picked.Add(id);
            }
        }

        return picked;
    }

}
