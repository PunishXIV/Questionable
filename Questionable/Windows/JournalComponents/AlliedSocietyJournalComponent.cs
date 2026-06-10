using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Utils;
using Questionable.Windows.QuestComponents;
using static Questionable.Utils.LocalizeShortcut;
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
    Configuration configuration,
    IDalamudPluginInterface pluginInterface,
    UiUtils uiUtils)
{
    uint _unchecked;
    uint _incomplete;
    public void DrawAlliedSocietyQuests()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Allied Societies"));
        if (!tab)
            return;
        bool addPending = false;

        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Add")))
            addPending = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Add unchecked quests (from expanded categories) to prio"));
        ImGui.SameLine();

        bool preventQuestCompletion = configuration.Advanced.PreventQuestCompletion;
        bool abandonQuestBeforeCompletion = configuration.Advanced.AbandonQuestBeforeCompletion;
        bool removeFromPriorityWhenAbandoned = configuration.Advanced.RemoveFromPriorityWhenAbandoned;
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Stop, preventQuestCompletion ? ImGuiColors.DalamudOrange : null))
        {
            configuration.Advanced.PreventQuestCompletion = !preventQuestCompletion;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Prevent quest completion"));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Ban, abandonQuestBeforeCompletion ? ImGuiColors.DalamudOrange : null))
        {
            configuration.Advanced.AbandonQuestBeforeCompletion = !abandonQuestBeforeCompletion;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Abandon quest before completion"));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Trash, removeFromPriorityWhenAbandoned ? ImGuiColors.DalamudOrange : null))
        {
            configuration.Advanced.RemoveFromPriorityWhenAbandoned = !removeFromPriorityWhenAbandoned;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Remove from priority when abandoned"));

        ImGui.SameLine();

        unsafe
        {
            uint allowances = QuestManager.Instance()->GetBeastTribeAllowance();
            ImGui.Text(_LF("Allowances: {0}/12", allowances));
        }

        if (_incomplete > 0)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_LF("Quests marked with yellow have not been completed once yet on this character."),
                                       FontAwesomeIcon.InfoCircle, ImGuiColors.DalamudYellow);
        }

        if (_unchecked > 0)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_L("Quests marked with orange need to be reported as working\n" +
                                       "or not via the LastChecked system. Ask Aly for more details!"),
                                       FontAwesomeIcon.InfoCircle, ImGuiColors.DalamudOrange);
            ImGui.SameLine();
            ImGui.Text(_LF("Unchecked: {0}", _unchecked));
        }

        _unchecked = 0;
        _incomplete = 0;

        foreach (EAlliedSociety alliedSociety in Enum.GetValues<EAlliedSociety>().Where(x => x != EAlliedSociety.None))
        {
            List<IQuestInfo> quests = alliedSocietyQuestFunctions.GetAvailableAlliedSocietyQuests(alliedSociety)
                .Select(x => questData.GetQuestInfo(x))
                .ToList();
            (EAlliedSocietyRank rank, ushort currentRep, ushort neededRep) = questFunctions.GetAlliedSocietyRankAndRep(alliedSociety);

            string rep = neededRep != 0 ? $"({rank} {currentRep}/{neededRep}) " : "";
            string label = $"{rep}{alliedSociety}###AlliedSociety{(int)alliedSociety}";
            bool isOpen;

            using (ImRaii.Disabled(quests.Count == 0))
            {
                // If, of the quests in this category, any quest...
                if (quests.Any(x => !x.QuestId.Value.Equals(1569) && ( // is not the Ixal delivery quest "Deliverance", and
                        !questRegistry.TryGetQuest(x.QuestId, out Quest? quest) || // is not a valid quest in the registry, or
                        (quest.Root.Disabled && quest.Root.Comment == null) || // is disabled without a comment explaining why, or
                        (quest.Root.LastChecked.Date != null && (quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 90 || // has not been reported checked in more than 90 days, or
                                                                 (quest.Root.Comment ?? "").Contains("FATE")) // is a FATE quest where we don't care that much
                        )
                    )
                ))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange)) // highlight the category orange
                    {
                        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                    _unchecked += 1;
                }
                else if (quests.Any(x => !questFunctions.IsQuestComplete(x.QuestId))) // if the character has not completed a quest in this category
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    {
                        //ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                }
                else
                {
                    if (_unchecked > 0 || _incomplete > 0)
                        ImGui.SetNextItemOpen(false, ImGuiCond.Always);
                    isOpen = ImGui.CollapsingHeader(label);
                }
            }

            questJournalUtils.ShowQuestGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}", quests);

            if (!isOpen)
                continue;

            if (alliedSociety <= EAlliedSociety.Ixal)
            {
                for (byte i = 1; i <= 8; ++i)
                {
                    List<IQuestInfo> questsByRank = quests.Where(quest => (byte)((QuestInfo)quest).AlliedSocietyRank == i && !quest.QuestId.Value.Equals(1569)).ToList();
                    if (questsByRank.Count == 0)
                        continue;

                    ImGui.Text($"{(EAlliedSocietyRank)i}");
                    questJournalUtils.ShowQuestGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}/{(EAlliedSocietyRank)i}", questsByRank);
                    foreach (IQuestInfo quest in questsByRank)
                        DrawQuest((QuestInfo)quest, addPending, neededRep != 0);
                }
            }
            else
            {
                foreach (IQuestInfo quest in quests)
                    DrawQuest((QuestInfo)quest, addPending, neededRep != 0);
            }
        }
    }

    private void DrawQuest(QuestInfo questInfo, bool addPending = false, bool showRepValue = false)
    {
        (Vector4 color, FontAwesomeIcon icon, string tooltipText) = uiUtils.GetQuestStyle(questInfo.QuestId);
        bool fate = false;
        string lastChecked = "";
        if (!questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
            color = ImGuiColors.DalamudGrey;
        else
        {
            if (quest.Root.LastChecked.Date != null)
            {
                lastChecked = $"({quest.Root.LastChecked.Date})";
                if (quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 90)
                    color = ImGuiColors.DalamudRed;
            }
            else
                color = ImGuiColors.DPSRed;
            if (quest.Root.Disabled && (quest.Root.Comment ?? "").Contains("FATE"))
            {
                color = ImGuiColors.DalamudOrange;
                fate = true;
            }
        }

        string checklistItem = $"{questInfo.Name} ({tooltipText}) {lastChecked}";
        if (fate)
            checklistItem = "(FATE) " + checklistItem;
        if (showRepValue)
            checklistItem = $"[+{questInfo.SocietyRepValue}] " + checklistItem;
        if (uiUtils.ChecklistItem(checklistItem, color, icon))
            questTooltipComponent.Draw(questInfo);
        if (addPending && (color.Equals(ImGuiColors.DalamudRed) || color.Equals(ImGuiColors.DPSRed)))
            questController.PriorityManager.Add(questInfo.QuestId);

        questJournalUtils.ShowContextMenu(questInfo, quest, nameof(AlliedSocietyJournalComponent));

        if (quest != null && questController.PriorityManager.Contains(quest))
        {
            ImGui.SameLine();
            using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationCircle.ToIconString());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("This quest is in Priority Quests."));
        }
    }
}
