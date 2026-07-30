using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common.Ui;
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
        bool isGathering = gatheringTasks.Count > 0;
        IList<string> tasks = isGathering ? gatheringTasks : questController.GetRemainingTaskNames();

        if (!QstWidgets.SectionHeader(_L("Remaining Tasks"), "RemainingTasks", count: tasks.Count))
            return;

        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                string task = isGathering ? $"G: {tasks[i]}" : tasks[i];
                if (i == 0 && questController.IsRunning)
                    ImGui.TextColored(QstTheme.Accent, Truncate(task));
                else
                {
                    using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                    ImGui.TextUnformatted(Truncate(task));
                }

                if (task.Length > MaxTaskLength && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(task);
            }
        }
    }

    private const int MaxTaskLength = 44;

    private static string Truncate(string text)
    {
        if (text.Length <= MaxTaskLength)
            return text;

        return text[..(MaxTaskLength - 3)].TrimEnd() + "...";
    }
}
