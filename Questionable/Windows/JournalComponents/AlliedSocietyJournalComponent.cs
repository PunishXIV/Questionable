using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Windows.QuestComponents;
namespace Questionable.Windows.JournalComponents;

internal sealed class AlliedSocietyJournalComponent
(
    QuestFunctions questFunctions,
    QuestController questController,
    AlliedSocietyQuestFunctions alliedSocietyQuestFunctions,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestJournalUtils questJournalUtils,
    QuestTooltipComponent questTooltipComponent,
    UiUtils uiUtils)
{
    private static readonly string[] RankNames =
        ["Neutral", "Recognized", "Friendly", "Trusted", "Respected", "Honored", "Sworn", "Allied"];
    private readonly AlliedSocietyQuestFunctions _alliedSocietyQuestFunctions = alliedSocietyQuestFunctions;
    private readonly QuestController _questController = questController;
    private readonly QuestData _questData = questData;

    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly QuestJournalUtils _questJournalUtils = questJournalUtils;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly QuestTooltipComponent _questTooltipComponent = questTooltipComponent;
    private readonly UiUtils _uiUtils = uiUtils;

    public void DrawAlliedSocietyQuests()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Allied Societies");
        if (!tab)
            return;
        bool addPending = false;
#if DEBUG
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add"))
            addPending = true;
        ImGui.SameLine();
#endif

        unsafe
        {
            uint allowances = QuestManager.Instance()->GetBeastTribeAllowance();
            ImGui.Text($"Remaining: {allowances}/12");
        }

        foreach (EAlliedSociety alliedSociety in Enum.GetValues<EAlliedSociety>().Where(x => x != EAlliedSociety.None))
        {
            List<IQuestInfo> quests = _alliedSocietyQuestFunctions.GetAvailableAlliedSocietyQuests(alliedSociety)
                .Select(x => _questData.GetQuestInfo(x))
                .ToList();
            //if (quests.Count == 0)
            //    continue;

            string label = $"{alliedSociety}###AlliedSociety{(int)alliedSociety}";
            bool isOpen;

            using (ImRaii.Disabled(quests.Count == 0))
            {
#if DEBUG
                if (quests.Any(x => !x.QuestId.Value.Equals(1569) && ( // Ixal "Deliverance"
                        !_questRegistry.TryGetQuest(x.QuestId, out Quest? quest) ||
                        (quest.Root.Disabled && !(quest.Root.Comment ?? "").Contains("FATE")) ||
                        (
                            (quest.Root.LastChecked.Date != null && quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 90) ||
                            (quest.Root.LastChecked.Date == null && !(quest.Root.Comment ?? "").Contains("FATE"))
                        )
                    )))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
                    {
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                }
                else
#endif
                if (quests.Any(x => !_questFunctions.IsQuestComplete(x.QuestId)))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    {
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                }
                else
                    isOpen = ImGui.CollapsingHeader(label);
            }

            _questJournalUtils.ShowQuestGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}", quests);

            if (!isOpen)
                continue;

            if (alliedSociety <= EAlliedSociety.Ixal)
            {
                for (byte i = 1; i <= 8; ++i)
                {
                    List<IQuestInfo> questsByRank = quests.Where(quest => ((QuestInfo)quest).AlliedSocietyRank == i && !quest.QuestId.Value.Equals(1569)).ToList();
                    if (questsByRank.Count == 0)
                        continue;

                    ImGui.Text(RankNames[i - 1]);
                    _questJournalUtils.ShowQuestGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}/{RankNames[i - 1]}", questsByRank);
                    foreach (IQuestInfo quest in questsByRank)
                        DrawQuest((QuestInfo)quest, addPending);
                }
            }
            else
            {
                foreach (IQuestInfo quest in quests)
                    DrawQuest((QuestInfo)quest, addPending);
            }
        }
    }

    private void DrawQuest(QuestInfo questInfo, bool addPending = false)
    {
        (Vector4 color, FontAwesomeIcon icon, string tooltipText) = _uiUtils.GetQuestStyle(questInfo.QuestId);
        Quest? quest;
        bool fate = false;
        string lastChecked = "";
        if (!_questRegistry.TryGetQuest(questInfo.QuestId, out quest))
            color = ImGuiColors.DalamudGrey;
        else
        {
            if (quest.Root.LastChecked.Date != null)
            {
                lastChecked = $"({quest.Root.LastChecked.Date})";
#if DEBUG
                if (quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 90)
                    color = ImGuiColors.DalamudRed;
#endif
            }
#if DEBUG
            else
                color = ImGuiColors.DPSRed;
#endif
            if (quest.Root.Disabled && (quest.Root.Comment ?? "").Contains("FATE"))
            {
                color = ImGuiColors.DalamudOrange;
                fate = true;
            }
        }

        string checklistItem = $"{questInfo.Name} ({tooltipText}) {lastChecked}";
        if (fate)
            checklistItem = "(FATE) " + checklistItem;
        if (_uiUtils.ChecklistItem(checklistItem, color, icon))
            _questTooltipComponent.Draw(questInfo);
        if (addPending && (color.Equals(ImGuiColors.DalamudRed) || color.Equals(ImGuiColors.DPSRed)))
            _questController.AddQuestPriority(questInfo.QuestId);

        _questJournalUtils.ShowContextMenu(questInfo, quest, nameof(AlliedSocietyJournalComponent));
    }
}
