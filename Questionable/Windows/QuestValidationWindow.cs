using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class QuestValidationWindow : LWindow
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestValidator _questValidator;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly RedoUtil _redoUtil;
    private readonly PathEditorWindow _pathEditorWindow;
    private string _filter = "";

    public QuestValidationWindow(
        QuestValidator questValidator,
        QuestData questData,
        QuestController questController,
        QuestTooltipComponent questTooltipComponent,
        RedoUtil redoUtil,
        PathEditorWindow pathEditorWindow,
        IDalamudPluginInterface pluginInterface)
        : base(_L("Quest Validation") + "###QuestionableValidator")
    {
        _questValidator = questValidator;
        _questData = questData;
        _questController = questController;
        _pluginInterface = pluginInterface;
        _questTooltipComponent = questTooltipComponent;
        _redoUtil = redoUtil;
        _pathEditorWindow = pathEditorWindow;

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

        foreach (ValidationIssue issue in _questValidator.Issues.OrderBy(i => i.Description, StringComparer.OrdinalIgnoreCase))
        {
            if (!_filter.IsNullOrEmpty() &&
                issue.ElementId != null &&
                !issue.ElementId.Value.ToString(CultureInfo.InvariantCulture).Contains(_filter) &&
                !issue.Description.Contains(_filter))
                continue;
            ImGui.TableNextRow();
            if (ImGui.TableNextColumn())
            {
                if (issue.ElementId != null)
                {
                    IQuestInfo quest = _questData.GetQuestInfo(issue.ElementId);
                    bool copy = ImGuiComponentsLocal.IconButton($"###ValidationWindowCopy{quest.QuestId.Value}", FontAwesomeIcon.Copy);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Copy as file name"));
                    if (copy)
                    {
                        string fileName = $"{quest.QuestId}_{quest.SimplifiedName}.json";
                        ImGui.SetClipboardText(fileName);
                    }

                    ImGui.SameLine();
                    bool edit = ImGuiComponentsLocal.IconButton($"###ValidationWindowEdit{quest.QuestId.Value}", FontAwesomeIcon.Edit);
                    bool editExternal = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Left click: Open in Path Editor\nRight click: Open in your default .json text editor"));
                    if (edit)
                        _pathEditorWindow.Open(quest.QuestId);
                    else if (editExternal)
                        QuestRegistry.OpenEditor(quest);

                    RedoIndex redoIndex = _redoUtil.GetChapter(quest.QuestId.Value);
                    if (redoIndex.Index != -1)
                    {
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"#{redoIndex.SimplifiedIndex}");
                    }
                }
            }

            if (ImGui.TableNextColumn())
            {
                if (issue.ElementId != null &&
                    _questData.GetQuestInfo(issue.ElementId) is { QuestId.Value: var id, SimplifiedName: var name } info)
                {
                    ImGui.TextUnformatted($"{id} {name}");
                    if (ImGui.IsItemHovered())
                        _questTooltipComponent.Draw(info);
                }
            }

            if (ImGui.TableNextColumn())
            {
                if (issue.Type is EIssueType.InvalidAcceptQuestTerritory && issue.TerritoryId != null)
                    ImGui.TextUnformatted(TerritoryData.GetNameAndId(issue.TerritoryId.Value));
                else if (issue.AlliedSociety != Model.Common.EAlliedSociety.None)
                    ImGui.TextUnformatted(issue.AlliedSociety.ToString());
            }

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(issue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(issue.Step?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            if (ImGui.TableNextColumn())
            {
                using (_ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    if (issue.Severity == EIssueSeverity.Error)
                    {
                        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Danger);
                        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                    }
                    else
                    {
                        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Info);
                        ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
                    }
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(issue.Description);
            }
        }
    }
}
