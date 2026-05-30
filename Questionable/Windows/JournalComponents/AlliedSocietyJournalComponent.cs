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
#if DEBUG
    Configuration configuration,
#endif
    IDalamudPluginInterface pluginInterface,
    UiUtils uiUtils)
{
    public void DrawAlliedSocietyQuests()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Allied Societies");
        if (!tab)
            return;
        bool addPending = false;
#if DEBUG
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add"))
            addPending = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add unchecked quests (from expanded categories) to prio");
        ImGui.SameLine();

        bool preventQuestCompletion = configuration.Advanced.PreventQuestCompletion;
        bool abandonQuestBeforeCompletion = configuration.Advanced.AbandonQuestBeforeCompletion;
        bool removeFromPriorityWhenAbandoned = configuration.Advanced.RemoveFromPriorityWhenAbandoned;
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop, preventQuestCompletion ? ImGuiColors.DalamudOrange : null))
        {
            configuration.Advanced.PreventQuestCompletion = !preventQuestCompletion;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Prevent quest completion");
        if (preventQuestCompletion)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Ban, abandonQuestBeforeCompletion ? ImGuiColors.DalamudOrange : null))
            {
                configuration.Advanced.AbandonQuestBeforeCompletion = !abandonQuestBeforeCompletion;
                pluginInterface.SavePluginConfig(configuration);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Abandon quest before completion");
            if (abandonQuestBeforeCompletion)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, removeFromPriorityWhenAbandoned ? ImGuiColors.DalamudOrange : null))
                {
                    configuration.Advanced.RemoveFromPriorityWhenAbandoned = !removeFromPriorityWhenAbandoned;
                    pluginInterface.SavePluginConfig(configuration);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Remove from priority when abandoned");
            }
            else if (removeFromPriorityWhenAbandoned)
            {
                configuration.Advanced.RemoveFromPriorityWhenAbandoned = false;
                pluginInterface.SavePluginConfig(configuration);
            }
        }
        else if (abandonQuestBeforeCompletion)
        {
            configuration.Advanced.AbandonQuestBeforeCompletion = false;
            pluginInterface.SavePluginConfig(configuration);
        }
        ImGui.SameLine();
#endif

        unsafe
        {
            uint allowances = QuestManager.Instance()->GetBeastTribeAllowance();
            ImGui.Text($"Remaining: {allowances}/12");
        }

        foreach (EAlliedSociety alliedSociety in Enum.GetValues<EAlliedSociety>().Where(x => x != EAlliedSociety.None))
        {
            List<IQuestInfo> quests = alliedSocietyQuestFunctions.GetAvailableAlliedSocietyQuests(alliedSociety)
                .Select(x => questData.GetQuestInfo(x))
                .ToList();

            string label = $"{alliedSociety}###AlliedSociety{(int)alliedSociety}";
            bool isOpen;

            using (ImRaii.Disabled(quests.Count == 0))
            {
#if DEBUG
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
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                }
                else
#endif
                if (quests.Any(x => !questFunctions.IsQuestComplete(x.QuestId))) // if the character has not completed a quest in this category
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    {
                        isOpen = ImGui.CollapsingHeader(label);
                    }
                }
                else
                    isOpen = ImGui.CollapsingHeader(label);
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
                ImGui.SetTooltip("This quest is in Priority Quests.");
        }
    }
}
