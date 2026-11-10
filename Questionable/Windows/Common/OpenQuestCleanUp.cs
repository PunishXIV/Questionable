using Quest = Questionable.Domain.Quest;

namespace Questionable.Windows.Common;

[RegisterSingleton]
internal sealed class OpenQuestCleanUp(
    QuestController questController,
    QuestFunctions questFunctions,
    IObjectTable objectTable,
    IChatGui chatGui)
{
    public void DrawRailButton()
    {
        (bool enabled, string tooltip) = GetState();
        if (QstWidgets.RailButton(FontAwesomeIcon.Broom, _L("Clean Up"), tooltip, enabled: enabled))
            Run();
    }

    public void DrawButton()
    {
        (bool enabled, string tooltip) = GetState();
        using (ImRaii.Disabled(!enabled))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Broom, _L("Clean Up")))
                Run();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
    }

    public void Run()
    {
        IReadOnlyList<Quest> added = questController.PriorityManager.QueueOpenQuests(questFunctions.OpenQuests);
        if (added.Count == 0)
        {
            chatGui.Print(_L("No open quests to clean up."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        chatGui.Print(_LF("Added {0} open quest(s) to the priority list:", added.Count) + " " +
                      string.Join(", ", added.Select(x => x.Info.Name)),
            CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private (bool Enabled, string Tooltip) GetState()
    {
        List<ElementId> openQuests = questFunctions.OpenQuests;
        int queueable = openQuests.Count(x => !questController.PriorityManager.Contains(x));
        bool loggedIn = objectTable[0] != null;

        string tooltip;
        if (!loggedIn)
            tooltip = _L("Unavailable while not logged in.");
        else if (openQuests.Count == 0)
            tooltip = _L("No open quests in your journal that Questionable knows how to continue.");
        else if (queueable == 0)
            tooltip = _LF("All {0} open quest(s) are already in your priority list.", openQuests.Count);
        else
            tooltip = _LF("Queue the {0} quest(s) you already have open, finished ones first.", queueable) +
                      "\n" +
                      _L("Long or blocked quests are queued too - check the priority list afterwards.");

        return (loggedIn && queueable > 0, tooltip);
    }
}
