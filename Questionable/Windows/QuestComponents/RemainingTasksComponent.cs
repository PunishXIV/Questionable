using Dalamud.Bindings.ImGui;
using Questionable.Controller;
using System.Collections.Generic;

namespace Questionable.Windows.QuestComponents;

internal sealed class RemainingTasksComponent
{
    private readonly QuestController _questController;
    private readonly GatheringController _gatheringController;

    public RemainingTasksComponent(QuestController questController, GatheringController gatheringController)
    {
        _questController = questController;
        _gatheringController = gatheringController;
    }

    public void Draw()
    {
        IList<string> gatheringTasks = _gatheringController.GetRemainingTaskNames();
        if (gatheringTasks.Count > 0)
        {
            ImGui.Separator();
            ImGui.BeginDisabled();
            foreach (var task in gatheringTasks)
                ImGui.TextUnformatted($"G: {task}");
            ImGui.EndDisabled();
        }
        else
        {
            var remainingTasks = _questController.GetRemainingTaskNames();
            if (remainingTasks.Count > 0)
            {
                ImGui.Separator();
                ImGui.BeginDisabled();
                foreach (var task in remainingTasks)
                    ImGui.TextUnformatted(task);
                ImGui.EndDisabled();
            }
        }
    }
}
