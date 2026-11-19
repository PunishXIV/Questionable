using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Questing;
using Questionable.Windows.Common;
using Questionable.Windows.Common.Ui;
using Questionable.Windows.PathEditorComponents;

namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class PathEditorWindow : LWindow
{
    private readonly PathEditorSession _session;
    private readonly StepFormComponent _stepFormComponent;
    private readonly StepCaptureComponent _stepCaptureComponent;
    private readonly QuestController _questController;
    private readonly QuestValidator _questValidator;
    private readonly QuestSelector _questSelector;
    private readonly IChatGui _chatGui;

    private ElementId? _pendingQuestId;

    public PathEditorWindow(PathEditorSession session, StepFormComponent stepFormComponent,
        StepCaptureComponent stepCaptureComponent, QuestController questController, QuestValidator questValidator,
        QuestSelector questSelector, IChatGui chatGui)
        : base(_L("Path Editor") + "###QuestionablePathEditor")
    {
        _session = session;
        _stepFormComponent = stepFormComponent;
        _stepCaptureComponent = stepCaptureComponent;
        _questController = questController;
        _questValidator = questValidator;
        _questSelector = questSelector;
        _chatGui = chatGui;

        Size = new Vector2(760, 480);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 360)
        };

        _questSelector.QuestSelected = quest =>
        {
            if (_session.Dirty && _session.QuestId != null && _session.QuestId != quest.Id)
                _pendingQuestId = quest.Id;
            else
                _session.Load(quest.Id);
        };
    }

    public void Open(ElementId questId)
    {
        if (_session.QuestId != questId)
            _session.Load(questId);
        IsOpenAndUncollapsed = true;
        BringToFront();
    }

    public override void DrawContent()
    {
        DrawToolbar();
        DrawUnsavedChangesGuard();

        using (ImRaii.ChildDisposable tree = ImRaii.Child("PathEditorTree",
                   new Vector2(240 * ImGuiHelpers.GlobalScale, 0), border: true))
        {
            if (tree)
                DrawSequenceTree();
        }

        ImGui.SameLine();

        using ImRaii.ChildDisposable detail = ImRaii.Child("PathEditorDetail", Vector2.Zero, border: false);
        if (detail)
        {
            _stepCaptureComponent.Draw(_session);
            if (QstWidgets.SectionHeader(_L("Step"), "PathEditorStep"))
                _stepFormComponent.Draw(_session);
            DrawValidationFooter();
        }
    }

    private void DrawToolbar()
    {
        _questSelector.DrawSelection();

        if (_session.Info is { } info)
        {
            ImGui.TextUnformatted(QuestRegistry.GetFilename(info));
            if (_session.Dirty)
            {
                ImGui.SameLine();
                QstWidgets.Chip(_L("unsaved"), QstTheme.Accent);
            }
        }
        else
        {
            using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
            ImGui.TextUnformatted(_L("Select a quest to edit."));
        }

        using (ImRaii.Disabled(!_session.Dirty || _session.Info == null || _session.WorkingRoot == null))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Save, _L("Save"), QstTheme.Success))
                Save();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_L("Writes the path to your user quest directory and reloads all quest data.\nSaving reformats the file (property order, omitted defaults)."));

        ImGui.SameLine();
        using (ImRaii.Disabled(!_session.Dirty))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Undo, _L("Discard")))
                _session.Discard();
        }

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.FolderOpen))
            QuestRegistry.OpenFolder();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Open Quests folder"));

        if (_session.Info is { } editableInfo)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExternalLinkAlt))
                QuestRegistry.OpenEditor(editableInfo);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Open this quest in your default .json text editor"));
        }
    }

    private void DrawUnsavedChangesGuard()
    {
        if (_pendingQuestId is not { } pendingQuestId)
            return;

        ImGui.TextColored(QstTheme.Accent, _L("You have unsaved changes."));
        ImGui.SameLine();
        if (ImGui.SmallButton(_L("Save first")))
        {
            Save();
            _session.Load(pendingQuestId);
            _pendingQuestId = null;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(_L("Discard changes")))
        {
            _session.Load(pendingQuestId);
            _pendingQuestId = null;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(_L("Cancel")))
            _pendingQuestId = null;
    }

    private void DrawSequenceTree()
    {
        QuestRoot? root = _session.WorkingRoot;
        if (root == null)
        {
            using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
            ImGui.TextWrapped(_L("Select a quest above, or open the editor from the Journal's right-click menu."));
            return;
        }

        for (int sequenceIndex = 0; sequenceIndex < root.QuestSequence.Count; sequenceIndex++)
        {
            QuestSequence sequence = root.QuestSequence[sequenceIndex];
            using ImRaii.IdDisposable id = ImRaii.PushId(sequenceIndex);
            using ImRaii.TreeNodeDisposable node = ImRaii.TreeNode(
                _LF("Sequence {0}", sequence.Sequence) + $"###Sequence{sequenceIndex}",
                ImGuiTreeNodeFlags.DefaultOpen);
            if (!node)
                continue;

            for (int stepIndex = 0; stepIndex < sequence.Steps.Count; stepIndex++)
            {
                QuestStep step = sequence.Steps[stepIndex];
                bool selected = _session.SelectedSequenceIndex == sequenceIndex
                                && _session.SelectedStepIndex == stepIndex;
                if (ImGui.Selectable($"{stepIndex}: {step.InteractionType}###Step{stepIndex}", selected))
                {
                    _session.SelectedSequenceIndex = sequenceIndex;
                    _session.SelectedStepIndex = stepIndex;
                }
            }

            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Plus))
            {
                uint territoryId = sequence.Steps.LastOrDefault()?.TerritoryId
                                   ?? _session.Info?.IssuerLocation.Territory.RowId ?? 0;
                sequence.Steps.Add(new QuestStep(EInteractionType.Interact, null, null, territoryId));
                _session.SelectedSequenceIndex = sequenceIndex;
                _session.SelectedStepIndex = sequence.Steps.Count - 1;
                _session.Dirty = true;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Add a step to this sequence"));

            if (sequence.Steps.Count == 0)
            {
                ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Trash, QstTheme.Danger))
                {
                    root.QuestSequence.RemoveAt(sequenceIndex);
                    _session.SelectedSequenceIndex = 0;
                    _session.SelectedStepIndex = 0;
                    _session.Dirty = true;
                    return;
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Remove this empty sequence"));
            }
        }

        ImGui.Spacing();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Add Sequence")))
        {
            byte nextSequence = root.QuestSequence
                .Select(x => x.Sequence)
                .Where(x => x < 255)
                .DefaultIfEmpty((byte)0)
                .Max();
            QuestSequence newSequence = new()
            {
                Sequence = (byte)Math.Min(nextSequence + 1, 254),
                Steps = []
            };
            int insertIndex = root.QuestSequence.FindIndex(x => x.Sequence == 255);
            if (insertIndex < 0)
                insertIndex = root.QuestSequence.Count;
            root.QuestSequence.Insert(insertIndex, newSequence);
            _session.Dirty = true;
        }
    }

    private void DrawValidationFooter()
    {
        if (_session.QuestId == null)
            return;

        List<ValidationIssue> issues = _questValidator.GetIssues(_session.QuestId);
        if (issues.Count == 0)
            return;

        if (!QstWidgets.SectionHeader(_L("Validation"), "PathEditorValidation", count: issues.Count))
            return;

        foreach (ValidationIssue issue in issues)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (issue.Severity == EIssueSeverity.Error)
                    ImGui.TextColored(QstTheme.Danger, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                else
                    ImGui.TextColored(QstTheme.Info, FontAwesomeIcon.InfoCircle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextWrapped($"{issue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "-"} / " +
                              $"{issue.Step?.ToString(CultureInfo.InvariantCulture) ?? "-"}: {issue.Description}");
        }
    }

    private void Save()
    {
        if (_session.Info == null || _session.WorkingRoot == null || _session.QuestId is not { } questId)
            return;

        (bool success, string message) = QuestRegistry.SaveUserPath(_session.Info, _session.WorkingRoot);
        if (success)
        {
            _chatGui.Print(_LF("Saved quest path to '{0}'", message), CommandHandler.MessageTag,
                CommandHandler.TagColor);
            _questController.Reload();
            _session.Load(questId);
        }
        else
            _chatGui.PrintError(message, CommandHandler.MessageTag, CommandHandler.TagColor);
    }
}
