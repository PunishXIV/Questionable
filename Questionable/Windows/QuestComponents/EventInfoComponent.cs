using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Humanizer;
using Humanizer.Localisation;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
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
    private readonly List<EventQuest> _eventQuests =
    [
        new("Limited Time Items", [new UnlockLinkId(568)], DateTime.MaxValue),
        new("Make It Rain 2026", [new QuestId(5443)], AtDailyReset(new(2026,6,24))) // May 25, 2026 at 3pm (GMT)
        //new("Valentione's Day 2026", [new QuestId(5325)], AtDailyReset(new(2026, 2, 16))) // January 15, 2026 at 6:59 a.m. (PST) 
    ];
    private readonly QuestController _questController = questController;

    private readonly QuestData _questData = questData;
    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly QuestTooltipComponent _questTooltipComponent = questTooltipComponent;
    private readonly UiUtils _uiUtils = uiUtils;

    public bool ShouldDraw => _configuration.General.ShowIncompleteSeasonalEvents && _eventQuests.Any(IsIncomplete);

    private static DateTime AtDailyReset(DateOnly date) => new(date, new(14, 59), DateTimeKind.Utc);

    public void Draw()
    {
        foreach (EventQuest eventQuest in _eventQuests)
        {
            if (IsIncomplete(eventQuest))
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
                if (startableQuests.Contains(questId) &&
                    _questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play))
                    {
                        _questController.SetNextQuest(quest);
                        _questController.Start("SeasonalEventSelection");
                    }

                    bool hovered = ImGui.IsItemHovered();

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(questName);
                    hovered |= ImGui.IsItemHovered();

                    if (hovered)
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
        return _eventQuests
            .Where(x => x.EndsAtUtc >= DateTime.UtcNow)
            .SelectMany(x => x.QuestIds)
            .Where(ShouldShowQuest);
    }

    private bool ShouldShowQuest(ElementId elementId)
    {
        return !_questFunctions.IsQuestComplete(elementId) &&
               !_questFunctions.IsQuestUnobtainable(elementId);
    }

    private sealed record EventQuest(string Name, List<ElementId> QuestIds, DateTime EndsAtUtc);
}
