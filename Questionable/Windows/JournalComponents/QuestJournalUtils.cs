using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.QuestComponents;
namespace Questionable.Windows.JournalComponents;

internal sealed class QuestJournalUtils
(
    QuestController questController,
    QuestFunctions questFunctions,
    ICommandManager commandManager,
    Configuration configuration,
    IDalamudPluginInterface pluginInterface)
{
    public void ShowContextMenu(IQuestInfo questInfo, Quest? quest, string label)
    {
        List<IQuestInfo> prereqs = [];
        if (questFunctions.prereqCache.TryGetValue(questInfo.QuestId.Value, out HashSet<IQuestInfo>? value))
            prereqs = value.Where(q => !questFunctions.IsQuestComplete(q.QuestId)).ToList();
        prereqs.Sort(Comparer<IQuestInfo>.Create((x, y) =>
        {
            var xCount = questFunctions.prereqCache.TryGetValue(x.QuestId.Value, out var xList) ? xList.Count : 0;
            var yCount = questFunctions.prereqCache.TryGetValue(y.QuestId.Value, out var yList) ? yList.Count : 0;
            return xCount.CompareTo(yCount);
        }));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestPopup{questInfo.QuestId}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestPopup{questInfo.QuestId}");
        if (!popup)
            return;

        using (ImRaii.Disabled(quest == null))
        {
            if (ImGui.MenuItem("Add to Priority Quests") && quest != null)
                questController.PriorityManager.Add(quest.Id);
        }
        using (ImRaii.Disabled(prereqs.Count == 0 || quest == null))
        {
            if (ImGui.MenuItem("Add all to Priority Quests") && quest != null)
            {
                foreach (var qInfo in prereqs)
                    questController.PriorityManager.Add(qInfo.QuestId);
                questController.PriorityManager.Add(quest.Id);
            }
        }

        using (ImRaii.Disabled(!questFunctions.IsReadyToAcceptQuest(questInfo.QuestId)))
        {
            if (ImGui.MenuItem("Start as next quest"))
            {
                questController.SetNextQuest(quest);
                questController.Start(label);
            }

            if (ImGui.MenuItem("Set as next quest"))
                questController.SetNextQuest(quest);
        }

        bool openInQuestMap = commandManager.Commands.ContainsKey("/questinfo");
        using (ImRaii.Disabled(questInfo.QuestId is not QuestId || !openInQuestMap))
        {
            if (ImGui.MenuItem("View in Quest Map"))
                commandManager.ProcessCommand($"/questinfo {questInfo.QuestId}");
        }

        if (ImGui.MenuItem("Add to Stop condition (on complete)"))
        {
            configuration.Stop.QuestsToStopAfter.Add(questInfo.QuestId);
            pluginInterface.SavePluginConfig(configuration);
        }

        if (ImGui.MenuItem("Add to Stop condition (on accept)"))
        {
            configuration.Stop.QuestsToStopWhenAccepted.Add(questInfo.QuestId);
            pluginInterface.SavePluginConfig(configuration);
        }
#if DEBUG
        if (ImGui.MenuItem("Edit quest path"))
            (bool success, string filename) = QuestRegistry.OpenEditor(questInfo);
        if (ImGui.MenuItem("Sim quest"))
            questController.SimulateQuest(questInfo, 0, 0);
#endif
    }

    internal static void ShowFilterContextMenu(QuestJournalComponent journalUi)
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Filter, "Filter"))
            ImGui.OpenPopup("##QuestFilters");

        using ImRaii.PopupDisposable popup = ImRaii.Popup("##QuestFilters");
        if (!popup)
            return;

        if (ImGui.Checkbox("Show only Available Quests", ref journalUi.Filter.AvailableOnly) ||
            ImGui.Checkbox("Hide Quests Without Path", ref journalUi.Filter.HideNoPaths))
        {
            journalUi.UpdateFilter();
        }
    }

    public void ShowQuestGroupContextMenu(string note, List<IQuestInfo> quests)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{note}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestGroupPopup{note}");
        if (!popup)
            return;

        if (ImGui.MenuItem("Add all to Priority Quests"))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Add(quest.QuestId);
        }

        if (ImGui.MenuItem("Remove all from Priority Quests"))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Remove(quest.QuestId);
        }

        if (ImGui.MenuItem("Sim first quest"))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);
    }
}
