using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Windows.QuestComponents;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Windows.JournalComponents;

internal sealed class AlliedSocietyJournalComponent
{
    private static readonly string[] RankNames =
        ["Neutral", "Recognized", "Friendly", "Trusted", "Respected", "Honored", "Sworn", "Allied"];

#if DEBUG
    private readonly QuestFunctions _questFunctions;
#endif
    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuestFunctions;
    private readonly QuestData _questData;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestJournalUtils _questJournalUtils;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;

    public AlliedSocietyJournalComponent(
#if DEBUG
        QuestFunctions questFunctions,
#endif
        AlliedSocietyQuestFunctions alliedSocietyQuestFunctions,
        QuestData questData,
        QuestRegistry questRegistry,
        QuestJournalUtils questJournalUtils,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils)
    {
#if DEBUG
        _questFunctions = questFunctions;
#endif
        _alliedSocietyQuestFunctions = alliedSocietyQuestFunctions;
        _questData = questData;
        _questRegistry = questRegistry;
        _questJournalUtils = questJournalUtils;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
    }

    public void DrawAlliedSocietyQuests()
    {
        using var tab = ImRaii.TabItem("Allied Societies");
        if (!tab)
            return;

        foreach (EAlliedSociety alliedSociety in Enum.GetValues<EAlliedSociety>().Where(x => x != EAlliedSociety.None))
        {
            List<QuestInfo> quests = _alliedSocietyQuestFunctions.GetAvailableAlliedSocietyQuests(alliedSociety)
                .Select(x => (QuestInfo)_questData.GetQuestInfo(x))
                .ToList();
            if (quests.Count == 0)
                continue;

            string label = $"{alliedSociety}###AlliedSociety{(int)alliedSociety}";
#if DEBUG
            bool isOpen;
            if (quests.Any(x => !_questRegistry.TryGetQuest(x.QuestId, out var quest) || quest.Root.Disabled))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                    isOpen = ImGui.CollapsingHeader(label);
            }
            else if (quests.Any(x => !_questFunctions.IsQuestComplete(x.QuestId)))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    isOpen = ImGui.CollapsingHeader(label);
            }
            else
                isOpen = ImGui.CollapsingHeader(label);
#else
            bool isOpen = ImGui.CollapsingHeader(label);
#endif

            if (!isOpen)
                continue;

            if (alliedSociety <= EAlliedSociety.Ixal)
            {
                for (byte i = 1; i <= 8; ++i)
                {
                    var questsByRank = quests.Where(x => x.AlliedSocietyRank == i).ToList();
                    if (questsByRank.Count == 0)
                        continue;

                    ImGui.Text(RankNames[i - 1]);
                    foreach (var quest in questsByRank)
                        DrawQuest(quest);
                }
            }
            else
            {
                foreach (var quest in quests)
                    DrawQuest(quest);
            }
        }
    }

    private void DrawQuest(QuestInfo questInfo)
    {
        var (color, icon, tooltipText) = _uiUtils.GetQuestStyle(questInfo.QuestId);
        if (!_questRegistry.TryGetQuest(questInfo.QuestId, out var quest) || quest.Root.Disabled)
            color = ImGuiColors.DalamudGrey;

        if (_uiUtils.ChecklistItem($"{questInfo.Name} ({tooltipText})", color, icon))
            _questTooltipComponent.Draw(questInfo);

        _questJournalUtils.ShowContextMenu(questInfo, quest, nameof(AlliedSocietyJournalComponent));
    }
}
