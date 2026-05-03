using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
namespace Questionable.Windows.QuestComponents;

internal sealed class ReportWarningComponent(Configuration configuration)
{
    private readonly Configuration _configuration = configuration;

    public void Draw()
    {
        DrawReportWarning();
    }

    private void DrawReportWarning()
    {
        ImGui.TextColored(ImGuiColors.DPSRed, "Future message");
        ImGui.TextWrapped("As of version xxxx, QST includes a feature where you can click the " +
                          "! button next to the quest progress buttons to report an issue with the current quest. " +
                          "This message is to notify you that if you choose to make use of this new feature and submit a " +
                          "bug report, QST will automatically capture and upload the following information:");
        ImGui.BulletText("List of all enabled plugins and their version numbers");
        ImGui.BulletText("The last ten actions taken by QST");
        ImGui.BulletText("The quest/sequence/step you are on when clicking the button");
        ImGui.BulletText("Your list of priority quests");
        ImGui.BulletText("A short configurable message from Settings, if set");
        ImGui.TextWrapped("This feature will never send any information to the bug report service unless you click " +
                          "the ! button highlighted in red below. If you would like to opt out of seeing this button, click the " +
                          "orange \"Opt Out\" button below. Otherwise, click the green \"Dismiss\" button to hide this warning.");
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExclamationTriangle, "Opt Out", ImGuiColors.DalamudOrange))
        {
            _configuration.General.DismissedReportWarning = true;
            _configuration.General.ReportsDisabled = true;
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExclamationTriangle, "Dismiss", ImGuiColors.ParsedGreen))
        {
            _configuration.General.DismissedReportWarning = true;
        }
    }
}
