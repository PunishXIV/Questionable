using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Humanizer;
using Humanizer.Localisation;
using Questionable.Model.Questing;
namespace Questionable.Windows.QuestComponents;

internal sealed class EventInfoComponent
(
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    UiUtils uiUtils,
    QuestController questController,
    QuestTooltipComponent questTooltipComponent,
    Configuration configuration)
{
    private readonly Configuration _configuration = configuration;
    internal static readonly List<EventQuest> EventQuests =
    [
        // Add seasonal events here. If a quest has additional required quests (e.g Make It Rain > Gold Saucer), add a relation in QuestData#L220
        new(_L("Limited Time Items"), [new UnlockLinkId(568)], DateTime.MaxValue),
        new(_T<Lumina.Excel.Sheets.BannerBg>(233), [new QuestId(2141)], AtDailyReset(2026, 10, 5)),
        new(_T<Lumina.Excel.Sheets.CabinetSubCategory>(124).Replace("2025", "2026"), [new QuestId(5457)], AtDailyReset(2026, 8, 26)),
    ];
    private readonly QuestController _questController = questController;

    private readonly QuestData _questData = questData;
    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly QuestTooltipComponent _questTooltipComponent = questTooltipComponent;
    private readonly UiUtils _uiUtils = uiUtils;

    public bool ShouldDraw => _configuration.General.ShowIncompleteSeasonalEvents && EventQuests.Any(IsIncomplete);

    private static DateTime AtDailyReset(int year, int month, int day) => new(new(year, month, day), new(14, 59), DateTimeKind.Utc);

    public void Draw()
    {
        foreach (EventQuest eventQuest in EventQuests.Where(x => x.EndsAtUtc >= DateTime.UtcNow && x.QuestIds.All(ShouldShowQuest)))
        {
            DrawEventQuest(eventQuest);
        }
    }

    private void DrawEventQuest(EventQuest eventQuest)
    {
        if (eventQuest.EndsAtUtc != DateTime.MaxValue)
        {
            string time = (eventQuest.EndsAtUtc - DateTime.UtcNow).Humanize(
                1,
                CultureInfo.InvariantCulture,
                minUnit: TimeUnit.Minute,
                maxUnit: TimeUnit.Day);
            ImGui.Text($"{eventQuest.Name} ({time})");
        }
        else
            ImGui.Text(eventQuest.Name);

        List<ElementId> startableQuests = eventQuest.QuestIds.Where(x =>
                _questRegistry.IsKnownQuest(x) &&
                _questFunctions.IsReadyToAcceptQuest(x) &&
                x != _questController.StartedQuest?.Quest.Id &&
                x != _questController.NextQuest?.Quest.Id)
            .ToList();
        foreach (ElementId questId in eventQuest.QuestIds)
        {
            if (_questFunctions.IsQuestComplete(questId))
                continue;

            using (ImRaii.PushId($"##EventQuestSelection{questId}"))
            {
                string questName = _questData.GetQuestInfo(questId).Name;

                bool priority = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Add to priority quests"));
                if (priority)
                    _questController.PriorityManager.Add(questId);
                ImGui.SameLine();
                if (startableQuests.Contains(questId) &&
                    _questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    if (ImGuiComponentsLocal.QuestNotice(_questController, quest))
                        _questTooltipComponent.Draw(quest.Info);
                }
                else
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX());

                    (Vector4 Color, FontAwesomeIcon Icon, string Status) = _uiUtils.GetQuestStyle(questId);
                    if (_uiUtils.ChecklistItem(questName, Color, Icon, ImGui.GetStyle().FramePadding.X))
                        _questTooltipComponent.Draw(_questData.GetQuestInfo(questId));
                }
            }
        }
    }

    private bool IsIncomplete(EventQuest eventQuest)
    {
        if (eventQuest.EndsAtUtc <= DateTime.UtcNow)
            return false;

        return eventQuest.QuestIds.Any(ShouldShowQuest);
    }

    public IEnumerable<ElementId> GetCurrentlyActiveEventQuests()
    {
        return EventQuests
            .Where(x => x.EndsAtUtc >= DateTime.UtcNow && x.QuestIds.All(ShouldShowQuest))
            .SelectMany(x => x.QuestIds);
    }

    private bool ShouldShowQuest(ElementId elementId)
    {
        return !_questFunctions.IsQuestComplete(elementId) &&
               !_questFunctions.IsQuestUnobtainable(elementId);
    }

    internal sealed record EventQuest(string Name, List<ElementId> QuestIds, DateTime EndsAtUtc);
}
