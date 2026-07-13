using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Questionable.Controller;
namespace Questionable.Windows.QuestComponents;

internal sealed class RemainingTasksComponent(
    QuestController questController,
    GatheringController gatheringController,
    Configuration configuration)
{
    public void Draw()
    {
        if (configuration.General.HideRemainingTasks)
            return;
        IList<string> gatheringTasks = gatheringController.GetRemainingTaskNames();
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
            IList<string> remainingTasks = questController.GetRemainingTaskNames();
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
