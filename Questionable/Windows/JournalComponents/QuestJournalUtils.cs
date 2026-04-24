using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Windows.JournalComponents;

internal sealed class QuestJournalUtils(QuestController questController, QuestFunctions questFunctions,
    ICommandManager commandManager, Configuration configuration, IDalamudPluginInterface pluginInterface)
{
    private readonly QuestController _questController = questController;
    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly ICommandManager _commandManager = commandManager;
    private readonly Configuration _configuration = configuration;
    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;

    public void ShowContextMenu(IQuestInfo questInfo, Quest? quest, string label)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestPopup{questInfo.QuestId}");

        using var popup = ImRaii.Popup($"##QuestPopup{questInfo.QuestId}");
        if (!popup)
            return;

        using (ImRaii.Disabled(quest == null))
        {
            if (ImGui.MenuItem("Add to Priority Quests") && quest != null)
            {
                _questController.AddQuestPriority(quest.Id);
            }
        }

        using (ImRaii.Disabled(!_questFunctions.IsReadyToAcceptQuest(questInfo.QuestId)))
        {
            if (ImGui.MenuItem("Start as next quest"))
            {
                _questController.SetNextQuest(quest);
                _questController.Start(label);
            }

            if (ImGui.MenuItem("Set as next quest"))
                _questController.SetNextQuest(quest);
        }

        bool openInQuestMap = _commandManager.Commands.ContainsKey("/questinfo");
        using (ImRaii.Disabled(!(questInfo.QuestId is QuestId) || !openInQuestMap))
        {
            if (ImGui.MenuItem("View in Quest Map"))
            {
                _commandManager.ProcessCommand($"/questinfo {questInfo.QuestId}");
            }
        }

        if (ImGui.MenuItem("Add to Stop condition"))
        {
            _configuration.Stop.QuestsToStopAfter.Add(questInfo.QuestId);
            _pluginInterface.SavePluginConfig(_configuration);
        }
    }

    internal static void ShowFilterContextMenu(QuestJournalComponent journalUi)
    {
        if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Filter, "Filter"))
            ImGui.OpenPopup("##QuestFilters");

        using var popup = ImRaii.Popup("##QuestFilters");
        if (!popup)
            return;

        if (ImGui.Checkbox("Show only Available Quests", ref journalUi.Filter.AvailableOnly) ||
            ImGui.Checkbox("Hide Quests Without Path", ref journalUi.Filter.HideNoPaths))
            journalUi.UpdateFilter();
    }

    public void ShowQuestGroupContextMenu(string note, List<IQuestInfo> quests)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{note}");

        using var popup = ImRaii.Popup($"##QuestGroupPopup{note}");
        if (!popup)
            return;

        if (ImGui.MenuItem("Add all to Priority Quests"))
        {
            foreach (var quest in quests)
            {
                _questController.AddQuestPriority(quest.QuestId);
            }
        }

        if (ImGui.MenuItem("Remove all from Priority Quests"))
        {
            foreach (var quest in quests)
            {
                _questController.RemoveQuestPriority(quest.QuestId);
            }
        }
    }
}
