using System;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Common.Math;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Model;
using Questionable.Utils;
using Questionable.Validation;
using Questionable.Windows.Common;
namespace Questionable.Windows;

internal sealed class QuestValidationWindow : LWindow
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestValidator _questValidator;

    public QuestValidationWindow(QuestValidator questValidator, QuestData questData,
        QuestController questController, IDalamudPluginInterface pluginInterface)
        : base("Quest Validation###QuestionableValidator")
    {
        _questValidator = questValidator;
        _questData = questData;
        _questController = questController;
        _pluginInterface = pluginInterface;

        Size = new Vector2(600, 200);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 200)
        };
    }

    public override void DrawContent()
    {
        using ImRaii.TableDisposable table = ImRaii.Table("QuestSelection", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
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
            {
                ImGui.TextUnformatted(validationIssue.ElementId?.ToString() ?? string.Empty);

                if (validationIssue.ElementId != null)
                {
                    IQuestInfo quest = _questData.GetQuestInfo(validationIssue.ElementId);
                    bool copy = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Copy as file name");
                    if (copy)
                    {
                        string fileName = $"{quest.QuestId}_{quest.SimplifiedName}.json";
                        ImGui.SetClipboardText(fileName);
                    }

                    ImGui.SameLine();
                    bool sim = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play, new Vector2(16));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Simulate quest");
                    if (sim)
                        _questController.SimulateQuest(quest, validationIssue.Sequence ?? 0, 0);
                }
            }

            if (ImGui.TableNextColumn())
            {
                ImGui.TextUnformatted(validationIssue.ElementId != null
                    ? _questData.GetQuestInfo(validationIssue.ElementId).Name
                    : validationIssue.AlliedSociety.ToString());
            }

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Step?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
            {
                // ReSharper disable once UnusedVariable
                using (IDisposable font = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    if (validationIssue.Severity == EIssueSeverity.Error)
                    {
                        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                    }
                    else
                    {
                        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedBlue);
                        ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
                    }
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(validationIssue.Description);
            }
        }
    }
}
