using Humanizer;
using Humanizer.Localisation;
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
    internal static readonly Dictionary<ushort, EventDetails> Details = new()
    {
        [39] = new($"{_T<Lumina.Excel.Sheets.BannerBg>(233)} {DateTime.UtcNow.Year}"), // Yokai Watch
        [84] = new($"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(70)} {DateTime.UtcNow.Year}"), // FFXV
        [147] = new($"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(71)} {DateTime.UtcNow.Year}", AtDailyReset(2026, 12, 9)), // FFXVI
        [148] = new($"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(74)} {DateTime.UtcNow.Year}"), // Fall Guys
        [176] = new($"{_T<Lumina.Excel.Sheets.FittingShopCategory>(109)} {DateTime.UtcNow.Year}"), // All Saints' Wake
        [257] = new($"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(200)} {DateTime.UtcNow.Year}", AtDailyReset(2027, 2, 23)), // FFVII Keybound Brawlers
    };

    private static DateTime AtDailyReset(int year, int month, int day) => new(new(year, month, day), new(14, 59), DateTimeKind.Utc);

    private CachedValue<List<Event>> _events = new(ttlSeconds: 1);

    private List<Event> Events => _events.Get(() => GetEvents(questData.SeasonalQuests, questFunctions.ActiveFestivals,
        x => questData.TryGetQuestInfo(x, out IQuestInfo? info) ? info : null, questFunctions.IsPreviousQuestMet,
        x => Details.GetValueOrDefault(x)));

    public bool ShouldDraw => configuration.General.ShowIncompleteSeasonalEvents && Events.Any(x => x.QuestIds.Any(ShouldShowQuest));

    public void Draw()
    {
        foreach (Event gameEvent in Events.Where(x => x.QuestIds.Any(ShouldShowQuest)))
            DrawEvent(gameEvent);
    }

    private void DrawEvent(Event gameEvent)
    {
        ImGui.Text(gameEvent.Name);
        if (gameEvent.EndsAtUtc is { } endsAt && endsAt > DateTime.UtcNow)
        {
            ImGui.SameLine();
            ImGui.Text(_LF("ends in {0}", (endsAt - DateTime.UtcNow).Humanize(1, CultureInfo.InvariantCulture,
                minUnit: TimeUnit.Minute, maxUnit: TimeUnit.Day)));
        }

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
        Func<QuestId, IQuestInfo?> info, Func<PreviousQuestInfo, bool> met, Func<ushort, EventDetails?> details) =>
    [
        .. seasonal
            .Where(x => x.IsRunning(festivals))
            .GroupBy(x => x.FestivalId)
            .Select(x => new Event(
                details(x.Key)?.Name ?? x.First().Name,
                [
                    .. x.SelectMany(y => QuestChain.Before((QuestId)y.QuestId, info, met).Chain)
                        .Distinct()
                        .Cast<ElementId>()
                ],
                details(x.Key)?.EndsAtUtc))
    ];

    private bool ShouldShowQuest(ElementId questId) =>
        !questFunctions.IsQuestUnobtainable(questId) &&
        (configuration.General.ShowCompleteSeasonalEvents || !questFunctions.IsQuestComplete(questId));

    internal sealed record Event(string Name, List<ElementId> QuestIds, DateTime? EndsAtUtc = null);

    internal sealed record EventDetails(string Name, DateTime? EndsAtUtc = null);
}
