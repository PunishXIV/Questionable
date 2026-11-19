using Dalamud.Interface.Utility;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class QueueComponent
{
    // Quest journal menus compare against this to tell the queue's own rows apart.
    internal const string Source = "PriorityWindow";

    private readonly IChatGui _chatGui;

    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;

    private readonly QuestController _questController;
    private readonly QuestFunctions _questFunctions;
    private readonly PriorityPresets _presets;
    private readonly QuestSelector _questSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    private readonly QuestJournalUtils _questJournalUtils;
    private readonly OpenQuestCleanUp _openQuestCleanUp;
    private readonly DragOrder<ElementId> _drag = new();
    private Job? _lastKnownJob;
    private string _presetName = string.Empty;
    private string? _selectedPresetName;
    private bool? _jobQuestsReplace;

    public QueueComponent(QuestController questController, QuestFunctions questFunctions, QuestSelector questSelector,
        QuestTooltipComponent questTooltipComponent, UiUtils uiUtils, IChatGui chatGui, PriorityPresets presets,
        IDalamudPluginInterface pluginInterface, Configuration configuration, QuestJournalUtils questJournalUtils,
        OpenQuestCleanUp openQuestCleanUp)
    {
        _questController = questController;
        _questFunctions = questFunctions;
        _questSelector = questSelector;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _chatGui = chatGui;
        _presets = presets;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _questJournalUtils = questJournalUtils;
        _openQuestCleanUp = openQuestCleanUp;

        _questSelector.SuggestionPredicate = quest =>
            !quest.Info.IsMainScenarioQuest &&
            !questFunctions.IsQuestUnobtainable(quest.Id) &&
            questController.PriorityManager.Quests.All(x => x.Id != quest.Id);
        _questSelector.DefaultPredicate = quest => questFunctions.IsQuestAccepted(quest.Id);
        _questSelector.OnSelect = quest => _questController.PriorityManager.Add(quest);
    }

    public event EventHandler? OpenRequested;

    public void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public unsafe void Draw()
    {
        Job currentJob = (Job)(PlayerState.Instance()->CurrentClassJobId);
        if (_lastKnownJob != null && currentJob != _lastKnownJob && _jobQuestsReplace is { } replace)
            LoadPreset(_presets.JobQuestsName, replace);
        _lastKnownJob = currentJob;

        ImGui.Text(_L("Quests to do first:"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Order: this list, then class, primal and raid quests, then quests you track in the To-Do list, then the story."));
        bool canInterrupt = _questController.PriorityManager.CanInterrupt;
        if (ImGui.Checkbox(_L("Interrupt the current quest"), ref canInterrupt))
            _questController.PriorityManager.CanInterrupt = canInterrupt;
        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("When off, quests added here wait until the current quest is done.\nTurns back on when the plugin restarts."));
        _questSelector.DrawSelection();
        DrawSavedLists();
        DrawQuestList();
        DrawRewardTotal();
        if (_questController.PriorityManager.IsEmpty)
            ImGui.TextDisabled(_L("No quests yet. Add one above or pick a saved list."));

        _openQuestCleanUp.DrawButton();
        using (ImRaii.Disabled(_questController.PriorityManager.IsEmpty))
        {
            string removeLabel = _L("Remove Finished Quests");
            SameLineIfFits(FontAwesomeIcon.Eraser, removeLabel);
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Eraser, removeLabel))
                _questController.PriorityManager.RemoveCompleted(
                    x => _questFunctions.IsPriorityQuestDone(x) ||
                         (!_questFunctions.IsQuestAccepted(x) && _questFunctions.IsQuestUnobtainable(x)),
                    _questFunctions.IsQuestAccepted);

            string clearLabel = _L("Clear All");
            SameLineIfFits(FontAwesomeIcon.Trash, clearLabel);
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, clearLabel))
                    _questController.PriorityManager.Clear();
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                QstWidgets.Tooltip(_L("Hold CTRL to enable this button."));
        }

        if (_questController.PriorityManager.HasPendingAcceptOnly &&
            ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Inbox, _L("Change All to Normal Priority")))
            _questController.PriorityManager.ClearAllAcceptOnly();
    }

    private static void SameLineIfFits(FontAwesomeIcon icon, string label)
    {
        float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        float width = ImGuiComponents.GetIconButtonWithTextWidth(icon, label);
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right)
            ImGui.SameLine();
    }

    private void DrawRewardTotal()
    {
        if (_questController.PriorityManager.IsEmpty)
            return;

        (ulong experience, ulong gil) = _questController.PriorityManager.GetRewardTotals(_questFunctions.IsPriorityQuestDone);
        ImGui.TextUnformatted(_LF("Total: {0} XP, {1} gil",
            experience.ToString("N0", CultureInfo.CurrentCulture), gil.ToString("N0", CultureInfo.CurrentCulture)));
        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("Quests still to do. Synced quests (most society dailies) scale XP to your level."));
    }

    private void DrawQuestList()
    {
        List<Quest> priorityQuests = [.. _questController.PriorityManager.Quests];
        Quest? itemToRemove = null;
        for (int i = 0; i < priorityQuests.Count; ++i)
        {
            Quest quest = priorityQuests[i];
            using (ImRaii.PushId($"Quest{quest.Id}"))
            {
                if (_uiUtils.DrawQuestRow(quest.Id, quest.Info.Name))
                    _questTooltipComponent.Draw(quest.Info);

                _questJournalUtils.ShowContextMenu(quest.Info, quest, Source);

                if (_questController.PriorityManager.IsAcceptOnly(quest.Id))
                {
                    bool accepted = _questFunctions.IsQuestAccepted(quest.Id);
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                        ImGui.TextColored(accepted ? QstTheme.Success : QstTheme.Accent,
                            FontAwesomeIcon.Inbox.ToIconString());
                    if (ImGui.IsItemHovered())
                        QstWidgets.Tooltip(accepted
                            ? _L("Accepted; completion follows the normal quest order.")
                            : _L("Accept only — picked up before any queued quest is completed."));
                }

                if (priorityQuests.Count > 1)
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        int padCount = 8;

                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDown.ToIconString()).X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Edit.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * padCount -
                                       ImGui.GetStyle().ItemSpacing.X);
                    }

                    _drag.Handle(quest.Id);
                    ImGui.SameLine();
                }
                else
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        int padCount = 6;

                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Edit.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * padCount);
                    }
                }

                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit))
                    (bool success, string filename) = QuestRegistry.OpenEditor(quest.Info);
                if (ImGui.IsItemHovered())
                    QstWidgets.Tooltip(_L("Edit quest path"));
                ImGui.SameLine();

                if (ImGuiComponentsLocal.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = quest;
                if (ImGui.IsItemHovered())
                    QstWidgets.Tooltip(_L("Remove from Priority Quests"));
            }
        }

        bool move = _drag.End(out int from, out int to);
        if (itemToRemove != null)
            _questController.PriorityManager.Remove(itemToRemove);
        else if (move)
            _questController.PriorityManager.Move(from, to);
    }

    private static List<ElementId> ParseClipboardItems() => QuestClipboard.Paste();

    private void ExportToClipboard()
    {
        QuestClipboard.Copy(_questController.PriorityManager.Quests.Select(x => x.Id));
        _chatGui.Print(_L("Copied quests to clipboard."), CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private void ImportFromClipboard(List<ElementId> questIds) => _questController.PriorityManager.Import(questIds);

    private void DrawSavedLists()
    {
        Dictionary<string, List<string>> userLists = _configuration.Priority.Presets;
        Vector2 square = new(ImGui.GetFrameHeight());
        float button = square.X + ImGui.GetStyle().ItemSpacing.X;

        // Wider buttons than reserved here make the auto-resizing quest window grow every frame.
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 2 * button);
        if (ImGui.BeginCombo("##PresetSelection", _selectedPresetName ?? _L("Saved lists..."), ImGuiComboFlags.HeightLarge))
        {
            if (userLists.Count > 0)
            {
                ImGui.TextDisabled(_L("Yours"));
                foreach (string name in userLists.Keys)
                {
                    if (ImGui.Selectable(name, _selectedPresetName == name))
                        _selectedPresetName = name;
                }

                ImGui.Separator();
            }

            ImGui.TextDisabled(_L("Built-in"));
            foreach (IGrouping<PriorityPresets.EGroup, PriorityPresets.Preset> group in _presets.Grouped(_configuration.Advanced.Debug))
            {
                ImGui.TextDisabled("  " + PriorityPresets.GroupName(group.Key));
                using var indent = ImRaii.PushIndent();
                foreach (PriorityPresets.Preset preset in group)
                {
                    if (ImGui.Selectable(preset.Short ?? preset.Name, _selectedPresetName == preset.Name))
                        _selectedPresetName = preset.Name;
                    if (preset.Hint != null && ImGui.IsItemHovered())
                        QstWidgets.Tooltip(preset.Hint);
                }
            }

            ImGui.EndCombo();
        }

        using (ImRaii.Disabled(_selectedPresetName == null))
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.FileImport, square))
                LoadPreset(_selectedPresetName!, replace: true);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                QstWidgets.Tooltip(_L("Replace the list with this saved list"));

            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Plus, square))
                LoadPreset(_selectedPresetName!, replace: false);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                QstWidgets.Tooltip(_L("Add this saved list to the end of the list"));
        }

        bool noQuests = _questController.PriorityManager.IsEmpty;
        using (ImRaii.Disabled(noQuests))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Save, _L("Save as...")))
                ImGui.OpenPopup("##SaveList");
        }

        if (_selectedPresetName != null && userLists.ContainsKey(_selectedPresetName))
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Trash))
                {
                    userLists.Remove(_selectedPresetName);
                    _selectedPresetName = null;
                    SaveConfig();
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                QstWidgets.Tooltip(_L("Delete this saved list (hold CTRL)"));
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(noQuests))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy))
                ExportToClipboard();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            QstWidgets.Tooltip(_L("Copy the list to the clipboard"));

        ImGui.SameLine();
        List<ElementId> clipboardItems = ParseClipboardItems();
        using (ImRaii.Disabled(clipboardItems.Count == 0))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Paste))
                ImportFromClipboard(clipboardItems);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            QstWidgets.Tooltip(_L("Add quests from the clipboard to the end of the list"));

        if (_configuration.Advanced.Debug)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Redo))
                _presets.Reload();
            if (ImGui.IsItemHovered())
                QstWidgets.Tooltip(_L("Reload the built-in lists"));
        }

        DrawSavePopup(userLists);
    }

    private void DrawSavePopup(Dictionary<string, List<string>> userLists)
    {
        using ImRaii.PopupDisposable popup = ImRaii.Popup("##SaveList");
        if (!popup)
            return;

        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##PresetName", _L("Name..."), ref _presetName, 128);

        string name = _presetName.Trim();
        bool isBuiltIn = _presets.BuiltIn.Any(x => x.Name == name);
        bool exists = userLists.ContainsKey(name);
        using (ImRaii.Disabled(name.Length == 0 || isBuiltIn || (exists && !ImGui.IsKeyDown(ImGuiKey.ModCtrl))))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Save, _L("Save")))
            {
                SavePreset(name);
                _presetName = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }

        if (isBuiltIn)
            ImGui.TextColored(QstTheme.Danger, _L("A built-in list already has this name."));
        else if (exists)
            ImGui.TextColored(QstTheme.Amber, _L("Hold CTRL to overwrite your saved list."));
    }

    private void LoadPreset(string name, bool replace)
    {
        if (replace)
            _questController.PriorityManager.Clear();

        if (_presets.TryGetByName(name, out List<ElementId>? quests))
            _questController.PriorityManager.Import(quests);
        _jobQuestsReplace = name == _presets.JobQuestsName ? replace : null;
    }

    private void SavePreset(string name)
    {
        List<string> questIds = _questController.PriorityManager.Quests
            .Select(q => q.Id.ToString())
            .ToList();
        _configuration.Priority.Presets[name] = questIds;
        _selectedPresetName = name;
        SaveConfig();
    }

    private void SaveConfig() => _pluginInterface.SavePluginConfig(_configuration);
}
