using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Common.Math;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Utils;
using Questionable.Validation;
using Questionable.Windows.Common;
using Questionable.Windows.QuestComponents;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows;

internal sealed class QuestValidationWindow : LWindow
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestValidator _questValidator;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private string _filter = "";

    public QuestValidationWindow(QuestValidator questValidator, QuestData questData,
        QuestController questController, QuestTooltipComponent questTooltipComponent, IDalamudPluginInterface pluginInterface)
        : base(_L("Quest Validation") + "###QuestionableValidator")
    {
        _questValidator = questValidator;
        _questData = questData;
        _questController = questController;
        _pluginInterface = pluginInterface;
        _questTooltipComponent = questTooltipComponent;

        Size = new Vector2(600, 200);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 200)
        };
    }

    public override void DrawContent()
    {
        if (ImGuiComponentsLocal.IconButton("###QuestValidationCopy", FontAwesomeIcon.Copy))
            ImGui.SetClipboardText(JsonSerializer.Serialize(_questValidator.Issues, JsonOptions.Default));

        ImGui.SameLine();

        _ = ImGui.InputTextWithHint("Filter###QuestValidationFilter", _L("Filter quest validation results"), ref _filter, maxLength: 20);

        using ImRaii.TableDisposable table = ImRaii.Table("QuestSelection", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
        if (!table)
        {
            ImGui.Text("Not table");
            return;
        }

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn(_L("Quest"), ImGuiTableColumnFlags.WidthFixed, 125);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn(_L("Seq"), ImGuiTableColumnFlags.WidthFixed, 5);
        ImGui.TableSetupColumn(_L("Step"), ImGuiTableColumnFlags.WidthFixed, 5);
        ImGui.TableSetupColumn(_L("Issue"), ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableHeadersRow();

        foreach (ValidationIssue validationIssue in _questValidator.Issues.OrderBy(i => i.Description, StringComparer.OrdinalIgnoreCase))
        {
            if (!_filter.IsNullOrEmpty() &&
                validationIssue.ElementId != null &&
                !validationIssue.ElementId.Value.ToString(CultureInfo.InvariantCulture).Contains(_filter) &&
                !validationIssue.Description.Contains(_filter))
                continue;
            ImGui.TableNextRow();
            if (ImGui.TableNextColumn())
            {
                if (validationIssue.ElementId != null)
                {
                    IQuestInfo quest = _questData.GetQuestInfo(validationIssue.ElementId);
                    bool copy = ImGuiComponentsLocal.IconButton($"###ValidationWindowCopy{quest.QuestId.Value}", FontAwesomeIcon.Copy);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Copy as file name"));
                    if (copy)
                    {
                        string fileName = $"{quest.QuestId}_{quest.SimplifiedName}.json";
                        ImGui.SetClipboardText(fileName);
                    }

                    ImGui.SameLine();
                    bool sim = ImGuiComponentsLocal.IconButton($"###ValidationWindowSim{quest.QuestId.Value}", FontAwesomeIcon.Play);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Simulate quest"));
                    if (sim)
                        _questController.SimulateQuest(quest, validationIssue.Sequence ?? 0, 0);

                    ImGui.SameLine();
                    bool edit = ImGuiComponentsLocal.IconButton($"###ValidationWindowEdit{quest.QuestId.Value}", FontAwesomeIcon.Edit);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(QuestRegistry.OpenEditorDescription);
                    if (edit)
                        QuestRegistry.OpenEditor(quest);
                }
            }

            if (ImGui.TableNextColumn())
            {
                if (validationIssue.ElementId != null &&
                    _questData.GetQuestInfo(validationIssue.ElementId) is { QuestId.Value: var id, SimplifiedName: var name } qInfo)
                {
                    ImGui.TextUnformatted($"{id} {name}");
                    if (ImGui.IsItemHovered())
                        _questTooltipComponent.Draw(qInfo);
                }
            }

            if (ImGui.TableNextColumn())
            {
                if (validationIssue.Type is EIssueType.InvalidAcceptQuestTerritory && validationIssue.TerritoryId != null)
                    ImGui.TextUnformatted(TerritoryData.GetNameAndId(validationIssue.TerritoryId.Value));
                else if (validationIssue.AlliedSociety != Model.Common.EAlliedSociety.None)
                    ImGui.TextUnformatted(validationIssue.AlliedSociety.ToString());
            }

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(validationIssue.Step?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
            {
                using (_ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
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
