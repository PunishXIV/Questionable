using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Common.Math;
using LLib.ImGui;
using Questionable.Data;
using Questionable.Validation;

namespace Questionable.Windows;

internal sealed class QuestValidationWindow : LWindow
{
    private readonly QuestValidator _questValidator;
    private readonly QuestData _questData;
    private readonly IDalamudPluginInterface _pluginInterface;

    public QuestValidationWindow(QuestValidator questValidator, QuestData questData,
        IDalamudPluginInterface pluginInterface)
        : base("Quest Validation###QuestionableValidator")
    {
        _questValidator = questValidator;
        _questData = questData;
        _pluginInterface = pluginInterface;

        Size = new Vector2(600, 200);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 200),
        };
    }

    public override void DrawContent()
    {
        using var table = ImRaii.Table("QuestSelection", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
        if (!table)
        {
            ImGui.Text("Not table");
            return;
        }

        ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn("Seq", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("Issue", ImGuiTableColumnFlags.None, 200);
        ImGui.TableHeadersRow();

        foreach (ValidationIssue validationIssue in _questValidator.Issues)
        {
            ImGui.TableNextRow();

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.ElementId?.ToString() ?? string.Empty);

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.ElementId != null
                    ? _questData.GetQuestInfo(validationIssue.ElementId).Name
                    : validationIssue.AlliedSociety.ToString());

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Step?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
            {
                // ReSharper disable once UnusedVariable
                using (var font = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    if (validationIssue.Severity == EIssueSeverity.Error)
                    {
                        using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                    }
                    else
                    {
                        using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedBlue);
                        ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
                    }
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(validationIssue.Description);
            }
        }
    }
}
