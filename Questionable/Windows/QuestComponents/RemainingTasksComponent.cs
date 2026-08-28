using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class RemainingTasksComponent(
    QuestController questController,
    GatheringController gatheringController)
{
    private bool isGathering;
    internal IList<string> Tasks
    {
        get
        {
            IList<string> gatheringTasks = gatheringController.GetRemainingTaskNames();
            isGathering = gatheringTasks.Count > 0;
            return isGathering ? gatheringTasks : questController.GetRemainingTaskNames();
        }
    }

    public void Draw()
    {
        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            for (int i = 0; i < Tasks.Count; i++)
            {
                string task = isGathering ? $"G: {Tasks[i]}" : Tasks[i];
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
