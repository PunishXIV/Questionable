using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Windows.JournalComponents;

internal sealed class QuestJournalUtils
{
    private readonly QuestController _questController;
    private readonly QuestFunctions _questFunctions;
    private readonly ICommandManager _commandManager;

    public QuestJournalUtils(QuestController questController, QuestFunctions questFunctions,
        ICommandManager commandManager)
    {
        _questController = questController;
        _questFunctions = questFunctions;
        _commandManager = commandManager;
    }

    public void ShowContextMenu(IQuestInfo questInfo, Quest? quest, string label)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestPopup{questInfo.QuestId}");

        using var popup = ImRaii.Popup($"##QuestPopup{questInfo.QuestId}");
        if (!popup)
            return;

        using (ImRaii.Disabled(!_questFunctions.IsReadyToAcceptQuest(questInfo.QuestId)))
        {
            if (ImGui.MenuItem("Start as next quest"))
            {
                _questController.SetNextQuest(quest);
                _questController.Start(label);
            }
        }

        bool openInQuestMap = _commandManager.Commands.ContainsKey("/questinfo");
        using (ImRaii.Disabled(!(questInfo.QuestId is QuestId) || !openInQuestMap))
        {
            if (ImGui.MenuItem("View in Quest Map"))
            {
                _commandManager.ProcessCommand($"/questinfo {questInfo.QuestId}");
            }
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
}
