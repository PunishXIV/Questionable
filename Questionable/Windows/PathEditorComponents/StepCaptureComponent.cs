using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;

namespace Questionable.Windows.PathEditorComponents;

internal sealed class StepCaptureComponent(
    ITargetManager targetManager,
    IClientState clientState,
    IObjectTable objectTable)
{
    public void Draw(PathEditorSession session)
    {
        QuestSequence? sequence = session.SelectedSequence;
        if (!QstWidgets.SectionHeader(_L("Capture"), "PathEditorCapture"))
            return;

        IGameObject? target = targetManager.Target;
        if (target != null)
        {
            using (QstWidgets.Card(QstTheme.WithAlpha(QstTheme.Accent, 0.45f)))
            {
                EInteractionType guessedType = QuestStepCapture.GuessInteractionType(target);
                ImGui.TextUnformatted(target.Name.ToString());
                ImGui.SameLine();
                QstWidgets.Chip(GameFunctions.GetBaseID(target).ToString(CultureInfo.InvariantCulture), QstTheme.Info);
                ImGui.SameLine();
                if (QstWidgets.Chip(guessedType.ToString(), QstTheme.Accent))
                    ImGui.SetTooltip(_L("Interaction type detected from the target's nameplate icon."));

                using (ImRaii.Disabled(sequence == null))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus,
                            _LF("Append step from target to Seq {0}", sequence?.Sequence ?? 0))
                        && sequence != null)
                    {
                        sequence.Steps.Add(QuestStepCapture.CreateStepFromTarget(target, clientState.TerritoryType));
                        session.SelectedStepIndex = sequence.Steps.Count - 1;
                        session.Dirty = true;
                    }
                }
            }
        }
        else
        {
            using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
            ImGui.TextUnformatted(_L("No target selected."));
        }

        if (objectTable[0] is { } player)
        {
            using (ImRaii.Disabled(sequence == null))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.MapMarkerAlt,
                        _L("Append step at my position"))
                    && sequence != null)
                {
                    sequence.Steps.Add(QuestStepCapture.CreatePositionStep(player.Position, clientState.TerritoryType));
                    session.SelectedStepIndex = sequence.Steps.Count - 1;
                    session.Dirty = true;
                }
            }
        }
    }
}
