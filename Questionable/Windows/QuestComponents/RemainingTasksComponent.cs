using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Questionable.Controller;
namespace Questionable.Windows.QuestComponents;

internal sealed class RemainingTasksComponent(QuestController questController, GatheringController gatheringController)
{
    private readonly GatheringController _gatheringController = gatheringController;
    private readonly QuestController _questController = questController;

    public void Draw()
    {
        IList<string> gatheringTasks = _gatheringController.GetRemainingTaskNames();
        if (gatheringTasks.Count > 0)
        {
            ImGui.Separator();
            using (ImRaii.Disabled())
            {
                foreach (string task in gatheringTasks)
                    ImGui.TextUnformatted($"G: {task}");
            }
        }
        else
        {
            IList<string> remainingTasks = _questController.GetRemainingTaskNames();
            if (remainingTasks.Count > 0)
            {
                ImGui.Separator();
                using (ImRaii.Disabled())
                {
                    foreach (string task in remainingTasks)
                        ImGui.TextUnformatted(task);
                }
            }
        }
    }
}
