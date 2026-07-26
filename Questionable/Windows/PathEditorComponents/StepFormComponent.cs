using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;

namespace Questionable.Windows.PathEditorComponents;

internal sealed class StepFormComponent
{
    private static readonly EInteractionType[] InteractionTypes = Enum.GetValues<EInteractionType>();
    private static readonly string[] InteractionTypeLabels =
        InteractionTypes.Select(x => x.ToString()).ToArray();

    private static readonly EAetheryteLocation?[] AetheryteValues =
        new EAetheryteLocation?[] { null }
            .Concat(Enum.GetValues<EAetheryteLocation>().Cast<EAetheryteLocation?>())
            .ToArray();
    private static readonly string[] AetheryteLabels =
        new[] { "-" }.Concat(Enum.GetValues<EAetheryteLocation>().Select(x => x.ToString())).ToArray();

    private string _interactionTypeSearch = string.Empty;
    private string _aetheryteSearch = string.Empty;

    public void Draw(PathEditorSession session)
    {
        QuestStep? step = session.SelectedStep;
        if (step == null)
        {
            using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
            ImGui.TextUnformatted(_L("Select a step to edit."));
            return;
        }

        EInteractionType selectedType = step.InteractionType;
        ImGuiComponentsLocal.DrawSearchableCombo(_L("Interaction Type"), InteractionTypes, InteractionTypeLabels,
            step.InteractionType, ref _interactionTypeSearch, ref selectedType);
        if (selectedType != step.InteractionType)
        {
            step.InteractionType = selectedType;
            session.Dirty = true;
        }

        float inputWidth = ImGui.GetWindowContentRegionMax().X / 2;

        string dataId = step.DataId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputText(_L("Data ID"), ref dataId, 12, ImGuiInputTextFlags.CharsDecimal))
        {
            step.DataId = uint.TryParse(dataId, out uint parsedDataId) ? parsedDataId : null;
            session.Dirty = true;
        }

        bool hasPosition = step.Position != null;
        if (ImGui.Checkbox($"{_L("Position")}###HasPosition", ref hasPosition))
        {
            step.Position = hasPosition ? Vector3.Zero : null;
            session.Dirty = true;
        }

        if (step.Position is { } position)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            Vector3 editablePosition = position;
            if (ImGui.InputFloat3("###PositionValue", ref editablePosition))
            {
                step.Position = editablePosition;
                session.Dirty = true;
            }
        }

        string territoryId = step.TerritoryId.ToString(CultureInfo.InvariantCulture);
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputText(_L("Territory ID"), ref territoryId, 8, ImGuiInputTextFlags.CharsDecimal)
            && uint.TryParse(territoryId, out uint parsedTerritoryId))
        {
            step.TerritoryId = parsedTerritoryId;
            session.Dirty = true;
        }

        float stopDistance = step.StopDistance ?? 0f;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputFloat(_L("Stop Distance"), ref stopDistance, 0.1f, 1f, "%.2f"))
        {
            step.StopDistance = stopDistance > 0f ? stopDistance : null;
            session.Dirty = true;
        }

        EAetheryteLocation? selectedAetheryte = step.AetheryteShortcut;
        ImGuiComponentsLocal.DrawSearchableCombo(_L("Aetheryte Shortcut"), AetheryteValues, AetheryteLabels,
            step.AetheryteShortcut, ref _aetheryteSearch, ref selectedAetheryte);
        if (selectedAetheryte != step.AetheryteShortcut)
        {
            step.AetheryteShortcut = selectedAetheryte;
            session.Dirty = true;
        }

        string comment = step.Comment ?? string.Empty;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint(_L("Comment"), _L("Shown in the debug overlay"), ref comment, 256))
        {
            step.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment;
            session.Dirty = true;
        }

        bool fly = step.Fly == true;
        if (ImGui.Checkbox(_L("Fly"), ref fly))
        {
            step.Fly = fly ? true : null;
            session.Dirty = true;
        }

        DrawValidationHints(step);
        DrawStepActions(session);
        DrawJsonPreview(step);
    }

    private static void DrawValidationHints(QuestStep step)
    {
        if (step.DataId == null
            && step.InteractionType is EInteractionType.Interact or EInteractionType.AcceptQuest
                or EInteractionType.CompleteQuest or EInteractionType.AttuneAetheryte)
            Hint(QstTheme.Amber, _L("This interaction type usually needs a Data ID."));

        if (step.Position == null
            && step.InteractionType is EInteractionType.WalkTo or EInteractionType.Jump or EInteractionType.Dive)
            Hint(QstTheme.Amber, _L("This interaction type usually needs a Position."));

        if (step.TerritoryId == 0)
            Hint(QstTheme.Danger, _L("Territory ID is not set."));
    }

    private static void Hint(Vector4 color, string text)
    {
        ImGui.TextColored(color, text);
    }

    private static void DrawStepActions(PathEditorSession session)
    {
        QuestSequence? sequence = session.SelectedSequence;
        QuestStep? step = session.SelectedStep;
        if (sequence == null || step == null)
            return;

        ImGui.Spacing();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Copy, _L("Duplicate")))
        {
            sequence.Steps.Insert(session.SelectedStepIndex + 1, CloneStep(step));
            session.SelectedStepIndex++;
            session.Dirty = true;
        }

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Delete Step"), QstTheme.Danger))
        {
            sequence.Steps.RemoveAt(session.SelectedStepIndex);
            session.SelectedStepIndex = Math.Max(0, session.SelectedStepIndex - 1);
            session.Dirty = true;
        }
    }

    private static void DrawJsonPreview(QuestStep step)
    {
        if (!QstWidgets.SectionHeader(_L("Step JSON"), "StepJson"))
            return;

        string json = JsonSerializer.Serialize(step, JsonOptions.Default);
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy))
            ImGui.SetClipboardText(json);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Copy this step as JSON"));

        using ImRaii.ColorDisposable textColor = ImRaii.PushColor(ImGuiCol.Text, QstTheme.TextMuted);
        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            ImGui.TextUnformatted(json);
        }
    }

    private static QuestStep CloneStep(QuestStep step)
    {
        return JsonSerializer.SerializeToNode(step, JsonOptions.Default)!.Deserialize<QuestStep>()!;
    }
}
