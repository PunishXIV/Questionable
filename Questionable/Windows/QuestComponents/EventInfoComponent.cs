using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Humanizer;
using Humanizer.Localisation;
using Questionable.Model.Questing;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
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
    internal static readonly List<EventQuest> EventQuests =
    [
        // Add seasonal events here. If a quest has additional required quests (e.g Make It Rain > Gold Saucer), add a relation in QuestData#L220
        // Comment out inactive events so they can be reused in future, don't delete blocks
        new(_L("Limited Time Items"), [new UnlockLinkId(568)], DateTime.MaxValue),
        // Yokai
        new(
            $"{_T<Lumina.Excel.Sheets.BannerBg>(233)} {DateTime.UtcNow.Year}",
            [new QuestId(434), new QuestId(2141)],
            AtDailyReset(2026, 10, 5)
        ),
        // FFXV
        new(
            $"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(70)} {DateTime.UtcNow.Year}",
            [new QuestId(3158), new QuestId(3159), new QuestId(3160)],
            AtDailyReset(2026, 10, 13)
        ),
        // Fall Guys
        new(
            $"{_T<Lumina.Excel.Sheets.CabinetSubCategory>(74)} {DateTime.UtcNow.Year}",
            [new QuestId(434), new QuestId(4801)],
            EndsAtUtc: AtDailyReset(2026, 10, 27),
            StartsAtUtc: AtDailyReset(2026, 10, 7),
            staysActive: true
        ),
    ];

    public bool ShouldDraw => configuration.General.ShowIncompleteSeasonalEvents && EventQuests.Any(IsIncomplete);

    private static DateTime AtDailyReset(int year, int month, int day) => new(new(year, month, day), new(14, 59), DateTimeKind.Utc);

    public void Draw()
    {
        foreach (EventQuest eventQuest in EventQuests.Where(x =>
            x.EndsAtUtc >= DateTime.UtcNow &&
            (x.QuestIds.All(ShouldShowQuest) ||
            x.StartsAtUtc < DateTime.UtcNow)))
        {
            DrawEventQuest(eventQuest);
        }
    }

    private void DrawEventQuest(EventQuest eventQuest)
    {
        ImGui.Text(eventQuest.Name);
        if (eventQuest.StartsAtUtc != null && eventQuest.StartsAtUtc.Value >= DateTime.UtcNow)
        {
            string time = (eventQuest.StartsAtUtc.Value - DateTime.UtcNow).Humanize(
                1,
                CultureInfo.InvariantCulture,
                minUnit: TimeUnit.Minute,
                maxUnit: TimeUnit.Day);
            ImGui.SameLine();
            ImGui.Text(_LF("starts in {0}", time));
        }
        else if (eventQuest.EndsAtUtc != DateTime.MaxValue)
        {
            string time = (eventQuest.EndsAtUtc - DateTime.UtcNow).Humanize(
                1,
                CultureInfo.InvariantCulture,
                minUnit: TimeUnit.Minute,
                maxUnit: TimeUnit.Day);
            ImGui.SameLine();
            ImGui.Text(_LF("ends in {0}", time));
        }

        List<ElementId> startableQuests = eventQuest.QuestIds.Where(x =>
                questRegistry.IsKnownQuest(x) &&
                questFunctions.IsReadyToAcceptQuest(x) &&
                x != questController.StartedQuest?.Quest.Id &&
                x != questController.NextQuest?.Quest.Id)
            .ToList();
        foreach (ElementId questId in eventQuest.QuestIds)
        {
            if (!configuration.General.ShowCompleteSeasonalEvents && questFunctions.IsQuestComplete(questId))
                continue;

            using (ImRaii.PushId($"##EventQuestSelection{questId}"))
            {
                string questName = questData.GetQuestInfo(questId).Name;

                bool priority = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Add to priority quests"));
                if (priority)
                    questController.PriorityManager.Add(questId);
                ImGui.SameLine();
                if (startableQuests.Contains(questId) &&
                    questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    if (ImGuiComponentsLocal.QuestNotice(questController, quest))
                        questTooltipComponent.Draw(quest.Info);
                }
                else
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX());

                    (Vector4 Color, FontAwesomeIcon Icon, string Status) = uiUtils.GetQuestStyle(questId);
                    if (uiUtils.ChecklistItem(questName, Color, Icon, ImGui.GetStyle().FramePadding.X))
                        questTooltipComponent.Draw(questData.GetQuestInfo(questId));
                }
            }
        }
    }

    private bool IsIncomplete(EventQuest eventQuest)
    {
        // if event end time is in the past, event is complete
        if (eventQuest.EndsAtUtc <= DateTime.UtcNow)
            return false;
        // if event start time is in the future, event is complete
        if (eventQuest.StartsAtUtc > DateTime.UtcNow)
            return false;
        if (eventQuest.staysActive)
            return true;

        return eventQuest.QuestIds.Any(ShouldShowQuest);
    }

    public IEnumerable<ElementId> GetCurrentlyActiveEventQuests()
    {
        return EventQuests
            .Where(x => x.StartsAtUtc < DateTime.UtcNow && x.EndsAtUtc >= DateTime.UtcNow && x.QuestIds.All(ShouldShowQuest))
            .SelectMany(x => x.QuestIds);
    }

    private bool ShouldShowQuest(ElementId elementId)
    {
        return !questFunctions.IsQuestUnobtainable(elementId) &&
               (configuration.General.ShowCompleteSeasonalEvents || !questFunctions.IsQuestComplete(elementId));
    }

    internal sealed record EventQuest(string Name, List<ElementId> QuestIds, DateTime EndsAtUtc, DateTime? StartsAtUtc = null, bool staysActive = false);
}
