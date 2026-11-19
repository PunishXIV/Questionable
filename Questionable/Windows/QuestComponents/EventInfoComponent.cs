using static Questionable.Utils.CacheUtils;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class EventInfoComponent
(
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    UiUtils uiUtils,
    QuestController questController,
    QuestStarter questStarter,
    QuestTooltipComponent questTooltipComponent,
    Configuration configuration)
{
    private CachedValue<List<Event>> _events = new(ttlSeconds: 1);

    private List<Event> Events => _events.Get(() => GetEvents(questData.SeasonalQuests, questFunctions.ActiveFestivals,
        x => questData.TryGetQuestInfo(x, out IQuestInfo? info) ? info : null, questFunctions.IsPreviousQuestMet));

    public bool ShouldDraw => configuration.General.ShowIncompleteSeasonalEvents && Events.Any(x => x.QuestIds.Any(ShouldShowQuest));

    public void Draw()
    {
        foreach (Event gameEvent in Events.Where(x => x.QuestIds.Any(ShouldShowQuest)))
            DrawEvent(gameEvent);
    }

    private void DrawEvent(Event gameEvent)
    {
        ImGui.Text(gameEvent.Name);

        List<ElementId> startableQuests = gameEvent.QuestIds.Where(x =>
                questRegistry.IsKnownQuest(x) &&
                questFunctions.IsReadyToAcceptQuest(x) &&
                x != questController.StartedQuest?.Quest.Id &&
                x != questController.NextQuest?.Quest.Id)
            .ToList();
        foreach (ElementId questId in gameEvent.QuestIds)
        {
            if (!configuration.General.ShowCompleteSeasonalEvents && questFunctions.IsQuestComplete(questId))
                continue;

            using (ImRaii.PushId($"##EventQuestSelection{questId}"))
            {
                IQuestInfo questInfo = questData.GetQuestInfo(questId);
                string questName = questInfo.Name;

                bool addPriority = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.AngleDoubleUp);
                if (ImGui.IsItemHovered())
                    QstWidgets.Tooltip(_L("Add to Priority Quests"));
                if (addPriority)
                    questController.PriorityManager.Add(questId);
                ImGui.SameLine();
                if (startableQuests.Contains(questId) &&
                    questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    if (ImGuiComponentsLocal.QuestNotice(questController, questStarter, quest))
                        questTooltipComponent.Draw(quest.Info);
                }
                else
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX());

                    (Vector4 color, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(questId);
                    if (uiUtils.ChecklistItem(questName, color, icon, ImGui.GetStyle().FramePadding.X))
                        questTooltipComponent.Draw(questInfo);
                }

                if (questInfo is QuestInfo { IsSeasonalEvent: false })
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(_L("(Prerequisite)"));
                }
            }
        }
    }

    public IEnumerable<ElementId> GetActiveQuests() => GetActiveQuests(Events,
        questFunctions.IsQuestAccepted, questFunctions.IsQuestComplete, x => questFunctions.IsQuestUnobtainable(x));

    internal static IEnumerable<ElementId> GetActiveQuests(IEnumerable<Event> events,
        Func<ElementId, bool> isAccepted, Func<ElementId, bool> isComplete, Func<ElementId, bool> isUnobtainable) =>
        events
            .SelectMany(x => x.QuestIds)
            .Where(x => isAccepted(x) || (!isComplete(x) && !isUnobtainable(x)))
            .Distinct();

    internal static List<Event> GetEvents(IEnumerable<QuestInfo> seasonal, IReadOnlyList<(ushort Id, ushort Phase)> festivals,
        Func<QuestId, IQuestInfo?> info, Func<PreviousQuestInfo, bool> met) =>
    [
        .. seasonal
            .Where(x => x.IsRunning(festivals))
            .GroupBy(x => x.FestivalId)
            .Select(x => new Event(x.First().Name, [
                .. x.SelectMany(y => QuestChain.Before((QuestId)y.QuestId, info, met).Chain)
                    .Distinct()
                    .Cast<ElementId>()
            ]))
    ];

    private bool ShouldShowQuest(ElementId questId) =>
        !questFunctions.IsQuestUnobtainable(questId) &&
        (configuration.General.ShowCompleteSeasonalEvents || !questFunctions.IsQuestComplete(questId));

    internal sealed record Event(string Name, List<ElementId> QuestIds);
}
