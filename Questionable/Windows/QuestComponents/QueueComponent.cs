using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class QueueComponent
{
    // Quest journal menus compare against this to tell the queue's own rows apart.
    internal const string Source = "PriorityWindow";

    private readonly IChatGui _chatGui;
    private bool _showPresets;

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

    // The quest window listens and switches to its Up next tab.
    public event EventHandler? OpenRequested;

    public void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public unsafe void Draw()
    {
        Job currentJob = (Job)(PlayerState.Instance()->CurrentClassJobId);
        if (_lastKnownJob != null && currentJob != _lastKnownJob && _selectedPresetName == _presets.JobQuestsName)
            LoadPreset(_presets.JobQuestsName);
        _lastKnownJob = currentJob;

        using (QstWidgets.SegmentGroup(armed: false))
        {
            if (QstWidgets.SegmentToggle(FontAwesomeIcon.ListOl, !_showPresets, _L("List"), _L("Show the list")))
                _showPresets = false;
            ImGui.SameLine();
            if (QstWidgets.SegmentToggle(FontAwesomeIcon.Bookmark, _showPresets, _L("Presets"), _L("Show presets")))
                _showPresets = true;
        }

        if (_showPresets)
        {
            DrawPresets();
            return;
        }

        if (QstWidgets.SectionHeader(_L("Explanation"), "PriorityExplanation", defaultOpen: false))
        {
            ImGui.TextWrapped(
                _L("Questionable will generally try to do:"));
            QstWidgets.BulletTextWrapped(_L("Priority quests added below, in order"));
            QstWidgets.BulletTextWrapped(_L("'Priority' quests: class quests, ARR primals, ARR raids"));
            QstWidgets.BulletTextWrapped(
                _L("Supported quests in your 'To-Do list' (quests from your Quest Journal that are always on-screen)"));
            QstWidgets.BulletTextWrapped(_L("MSQ quest (if available, unless it is marked as 'ignored' in your Journal)"));
            ImGui.TextWrapped(
                _L("If you don't have any active MSQ quest and there is no Priority Quest added here, " +
                "it will always try to pick up the next quest in the MSQ first."));
            ImGui.Spacing();
            QstWidgets.BulletTextWrapped(_L("Quests marked 'Accept only' are picked up first, but not done from this list."));
            QstWidgets.BulletTextWrapped(
                _L("With 'Interrupt the current quest' off, quests added here wait until the current quest is done."));
        }

        ImGui.Text(_L("Quests to do first:"));
        bool canInterrupt = _questController.PriorityManager.CanInterrupt;
        if (ImGui.Checkbox(_L("Interrupt the current quest"), ref canInterrupt))
            _questController.PriorityManager.CanInterrupt = canInterrupt;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("When off, quests added here wait until the current quest is done.\nTurns back on when the plugin restarts."));
        _questSelector.DrawSelection();
        DrawQuestList();
        DrawRewardTotal();

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
                ImGui.SetTooltip(_L("Hold CTRL to enable this button."));
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
            ImGui.SetTooltip(_L("Quests still to do. Synced quests (most society dailies) scale XP to your level."));
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
                        ImGui.SetTooltip(accepted
                            ? _L("Accepted — completion follows the normal quest order.")
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
                    ImGui.SetTooltip(_L("Edit quest path"));
                ImGui.SameLine();

                if (ImGuiComponentsLocal.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = quest;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Remove from priority quests"));
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

    private void DrawPresets()
    {
        IReadOnlyList<PriorityPresets.Preset> builtInPresets = _presets.BuiltIn;
        Dictionary<string, List<string>> userPresets = _configuration.Priority.Presets;

        string preview = _selectedPresetName ?? _L("Select a preset...");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##PresetSelection", preview, ImGuiComboFlags.HeightLarge))
        {
            if (userPresets.Count > 0)
            {
                ImGui.Separator();
                ImGui.TextDisabled(_L("Custom"));
                foreach (string name in userPresets.Keys)
                {
                    if (ImGui.Selectable(name, _selectedPresetName == name))
                    {
                        _selectedPresetName = name;
                        LoadPreset(name);
                    }
                }
            }

            ImGui.TextDisabled(_L("Built-in"));
            foreach (string name in builtInPresets.Select(x => x.Name))
            {
                if (ImGui.Selectable(name, _selectedPresetName == name))
                {
                    _selectedPresetName = name;
                    LoadPreset(name);
                }
            }

            ImGui.EndCombo();
        }

        bool addPresetsToList = _configuration.Priority.AddPresetsToList;
        if (ImGui.Checkbox(_L("Add presets to the current list"), ref addPresetsToList))
        {
            _configuration.Priority.AddPresetsToList = addPresetsToList;
            SaveConfig();
        }

        if (addPresetsToList)
        {
            ImGui.TextWrapped(_L("Selecting a preset adds its quests to the end of your current priority list. " +
                "You can save your current list as a preset by entering a name below and selecting Save."));
        }
        else
        {
            ImGui.TextColoredWrapped(QstTheme.Danger, _L("Selecting a preset will override your current priority list and activate the preset. " +
                "You can save your current list as a preset by entering a name below and selecting Save."));
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##PresetName", _L("Preset name..."), ref _presetName, 128);

        bool nameEmpty = string.IsNullOrWhiteSpace(_presetName);
        bool nameIsBuiltIn = !nameEmpty && builtInPresets.Any(x => x.Name == _presetName.Trim());
        bool nameExists = !nameEmpty && userPresets.ContainsKey(_presetName.Trim());
        bool noQuests = _questController.PriorityManager.IsEmpty;

        using (ImRaii.Disabled(nameEmpty || nameIsBuiltIn || noQuests || (nameExists && !ImGui.IsKeyDown(ImGuiKey.ModCtrl))))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Save, _L("Save Preset")))
            {
                SavePreset(_presetName.Trim());
                _presetName = string.Empty;
            }
        }
        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Redo))
            _presets.Reload();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Reload presets"));

        bool isUserPreset = _selectedPresetName != null && userPresets.ContainsKey(_selectedPresetName);
        if (isUserPreset)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Delete Preset")))
                {
                    userPresets.Remove(_selectedPresetName!);
                    _selectedPresetName = null;
                    SaveConfig();
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_L("Hold CTRL to enable this button."));
        }

        if (nameIsBuiltIn)
            ImGui.TextColored(QstTheme.Danger, _L("Cannot overwrite a built-in preset."));
        else if (nameExists)
            ImGui.TextColored(QstTheme.Amber, _L("Hold CTRL to overwrite existing preset."));

        ImGui.Spacing();
        List<ElementId> clipboardItems = ParseClipboardItems();
        using (ImRaii.Disabled(clipboardItems.Count == 0))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Paste, _L("Import from Clipboard")))
                ImportFromClipboard(clipboardItems);
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(noQuests))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Copy, _L("Export to Clipboard")))
                ExportToClipboard();
        }
    }

    private void LoadPreset(string name)
    {
        if (!_configuration.Priority.AddPresetsToList)
            _questController.PriorityManager.Clear();

        if (_presets.TryGetByName(name, out List<ElementId>? quests))
            _questController.PriorityManager.Import(quests);
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
