using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class StopConditionComponent : ConfigComponent
{
    private readonly IClientState _clientState;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelector _acceptQuestSelector;
    private readonly QuestSelector _completeQuestSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    private readonly PriorityPresets _presets;
    private readonly Configuration.EEndAction[] _endActions = Enum.GetValues<Configuration.EEndAction>();
    // Lifestream's /li auto picks the estate, apartment or inn from its own settings.
    private static readonly Configuration.EDestination[] Destinations =
    [
        Configuration.EDestination.None, Configuration.EDestination.HomeWorld,
        Configuration.EDestination.Auto, Configuration.EDestination.GrandCompany,
    ];
    private Vector2 _contentSize;

    public StopConditionComponent(
        IDalamudPluginInterface pluginInterface,
        QuestSelector questSelector,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils,
        PriorityPresets presets,
        IClientState clientState,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _presets = presets;
        _clientState = clientState;

        _completeQuestSelector = questSelector;
        _completeQuestSelector.SuggestionPredicate = quest => configuration.Stop.QuestsToStopAfter.All(x => x != quest.Id);
        _completeQuestSelector.DefaultPredicate = quest =>
            quest.Info.IsMainScenarioQuest && !questFunctions.IsQuestComplete(quest.Id) &&
            !questFunctions.IsQuestUnobtainable(quest.Id);
        _completeQuestSelector.OnSelect = quest =>
        {
            configuration.Stop.QuestsToStopAfter.Add(quest.Id);
            Save();
        };

        _acceptQuestSelector = new QuestSelector(questRegistry)
        {
            SuggestionPredicate = quest => configuration.Stop.QuestsToStopWhenAccepted.All(x => x != quest.Id),
            DefaultPredicate = quest =>
                    quest.Info.IsMainScenarioQuest && !questFunctions.IsQuestAcceptedOrComplete(quest.Id) &&
                    !questFunctions.IsQuestUnobtainable(quest.Id),
            OnSelect = quest =>
                {
                    configuration.Stop.QuestsToStopWhenAccepted.Add(quest.Id);
                    Save();
                }
        };
    }

    public void Draw()
    {
        _contentSize = ImGui.GetWindowContentRegionMax();
        using var wrap = ImRaii.TextWrapPos(_contentSize.X + 10);

        DrawConditions();
        DrawAfterStop();
    }

    private void DrawConditions()
    {
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("Stop the run when"));

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox(_L("Use stop conditions"), ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        using var disabled = ImRaii.Disabled(!enabled);
        using var indent = ImRaii.PushIndent();

        bool levelToStopAfter = Configuration.Stop.LevelToStopAfter;
        if (ImGui.Checkbox(_L("Stop at level"), ref levelToStopAfter))
        {
            Configuration.Stop.LevelToStopAfter = levelToStopAfter;
            Save();
        }

        ImGui.SameLine();
        int targetLevel = Configuration.Stop.TargetLevel;
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##TargetLevel", ref targetLevel, 1, 5))
        {
            Configuration.Stop.TargetLevel = Math.Max(1, Math.Min(100, targetLevel));
            Save();
        }

        unsafe
        {
            PlayerState* playerState = PlayerState.Instance();
            short currentLevel = playerState->CurrentLevel;
            if (currentLevel > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(_LF("(Current: {0})", currentLevel));
            }
        }

        bool priorityListEmpty = Configuration.Stop.PriorityListEmpty;
        if (ImGui.Checkbox(_L("Stop when the Priority Quests list is empty"), ref priorityListEmpty))
        {
            Configuration.Stop.PriorityListEmpty = priorityListEmpty;
            Save();
        }

        ImGuiComponents.HelpMarker(_L("Questionable only does your Priority Quests, then stops instead of going on with the story or other quests."));

        ImGui.Spacing();

        DrawQuestStopSection(
            _L("Stop when completing any of the quests selected below:"),
            "Complete",
            _completeQuestSelector,
            Configuration.Stop.QuestsToStopAfter,
            () =>
            {
                Configuration.Stop.QuestsToStopAfter.Clear();
                Configuration.Stop.PresetsAfterQuest.Clear();
            },
            withPreset: true);

        bool removeOnComplete = Configuration.Stop.RemoveWhenCompleteConditionMet;
        if (ImGui.Checkbox(_L("Remove from the list once completed"), ref removeOnComplete))
        {
            Configuration.Stop.RemoveWhenCompleteConditionMet = removeOnComplete;
            Save();
        }

        ImGui.Spacing();

        DrawQuestStopSection(
            _L("Stop when accepting any of the quests selected below:"),
            "Accept",
            _acceptQuestSelector,
            Configuration.Stop.QuestsToStopWhenAccepted,
            () => Configuration.Stop.QuestsToStopWhenAccepted.Clear());
    }

    private void DrawAfterStop()
    {
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("After the run stops"));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_L("Go to"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        // A saved pick that is no longer offered stays listed, so it isn't reset.
        Configuration.EDestination[] destinations = Destinations.Contains(Configuration.Stop.Destination)
            ? Destinations
            : [.. Destinations, Configuration.Stop.Destination];
        DrawComboOption("##Destination", destinations, destinations.Select(QuestStopPolicy.GetName).ToArray(),
            () => Configuration.Stop.Destination, x => Configuration.Stop.Destination = x);
        ImGuiComponents.HelpMarker(_L("Travels there with Lifestream first. The commands below wait until you have arrived."));

        bool runCommand = Configuration.Stop.RunCommandAfterStop;
        if (ImGui.Checkbox(_L("Run commands"), ref runCommand))
        {
            Configuration.Stop.RunCommandAfterStop = runCommand;
            Save();
        }

        using (ImRaii.PushIndent())
        {
            string command = Configuration.Stop.CommandAfterStop;
            Vector2 commandSize = new(ImGui.CalcItemWidth(),
                ImGui.GetTextLineHeight() * (command.Count(c => c == '\n') + 1) + ImGui.GetStyle().FramePadding.Y * 2);
            using (ImRaii.Disabled(!runCommand))
            {
                if (ImGui.InputTextMultiline("##Command", ref command, 512, commandSize))
                {
                    Configuration.Stop.CommandAfterStop = command;
                    Save();
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    Save();
            }
            ImGuiComponentsLocal.HelpMarker(
                _L("Press Enter for another command, one per line. Plugin commands and game commands both work. Never runs after these stops:"),
                QuestStopPolicy.ExcludedStopReasons.ToArray());
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_L("Wait"));
        ImGui.SameLine();
        int wait = Configuration.Stop.WaitSeconds;
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##WaitSeconds", ref wait, 5, 30))
        {
            Configuration.Stop.WaitSeconds = Math.Clamp(wait, 0, QuestStopPolicy.MaxWaitSeconds);
            Save();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(_L("seconds before the next step"));
        ImGuiComponents.HelpMarker(_L("For SND scripts or other long commands: wait this long before logging out, closing the game or switching characters. Lifestream travel is waited for anyway."));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_L("After that"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        DrawComboOption("##EndAction", _endActions, _endActions.Select(QuestStopPolicy.GetName).ToArray(),
            () => Configuration.Stop.EndAction, x => Configuration.Stop.EndAction = x);
        ImGuiComponents.HelpMarker(_L("What Questionable does once the commands have run. The game shows its own countdown, which you can cancel. With a character list, this waits for the last character."));

        DrawCheckbox(_L("Do this after a problem stop too (stuck, too many deaths, missing path)"),
            () => Configuration.Stop.RunAfterProblemStop, x => Configuration.Stop.RunAfterProblemStop = x,
            _L("Off: travel, commands and the action above only run after a normal finish (nothing left, a stop condition, or a stop button in the quest window). A character list also stops at a character that hit a problem."));
    }

    private void DrawQuestStopSection(string label, string sectionId, QuestSelector selector, List<ElementId> quests,
        Action clearAll, bool withPreset = false)
    {
        using (ImRaii.PushId(sectionId))
        {
            ImGui.Text(label);
            selector.DrawSelection();

            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Paste, _L("Paste")))
            {
                foreach (ElementId questId in QuestClipboard.Paste())
                {
                    if (!quests.Contains(questId) && _questRegistry.IsKnownQuest(questId))
                        quests.Add(questId);
                }

                Save();
            }

            if (ImGui.IsItemHovered())
                QstWidgets.Tooltip(_L("Adds quests copied from a stop list or Priority Quests."));

            if (quests.Count > 0)
            {
                ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Copy, _L("Copy")))
                    QuestClipboard.Copy(quests);

                ImGui.SameLine();
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Clear all")))
                    {
                        clearAll();
                        Save();
                    }
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    QstWidgets.Tooltip(_L("Hold CTRL to enable this button."));
            }

            Quest? itemToRemove = null;
            for (int i = 0; i < quests.Count; i++)
            {
                ElementId questId = quests[i];

                if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                    continue;

                using (ImRaii.PushId($"Quest{questId}"))
                {
                    if (withPreset)
                    {
                        DrawPresetCombo(questId);
                        ImGui.SameLine();
                    }

                    if (_uiUtils.DrawQuestRow(questId, quest.Info.Name))
                        _questTooltipComponent.Draw(quest.Info);

                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * 2);
                    }

                    if (ImGuiComponentsLocal.IconButton(string.Create(CultureInfo.InvariantCulture, $"##Remove{i}"), FontAwesomeIcon.Times))
                        itemToRemove = quest;
                }
            }

            if (itemToRemove != null)
            {
                quests.Remove(itemToRemove.Id);
                if (withPreset)
                    Configuration.Stop.SetPreset(itemToRemove.Id, null);
                Save();
            }
        }
    }

    private void DrawPresetCombo(ElementId questId)
    {
        string? presetId = Configuration.Stop.GetPreset(questId);
        string stop = _L("Stop");
        string preview = presetId == null ? stop : _presets.GetName(presetId) ?? _L("(missing preset)");

        ImGui.SetNextItemWidth(170 * ImGuiHelpers.GlobalScale);
        bool open = ImGui.BeginCombo("##Preset", preview, ImGuiComboFlags.HeightLarge);
        if (!open && ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("When this quest is done: stop, or add a preset to Priority Quests and keep going."));
        if (!open)
            return;

        string? picked = presetId;
        if (ImGui.Selectable(stop, presetId == null))
            picked = null;

        if (Configuration.Priority.Presets.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled(_L("Custom"));
            foreach (string name in Configuration.Priority.Presets.Keys)
            {
                string id = PriorityPresets.CustomPrefix + name;
                if (ImGui.Selectable(name, presetId == id))
                    picked = id;
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled(_L("Built-in"));
        foreach (IGrouping<PriorityPresets.EGroup, PriorityPresets.Preset> group in _presets.Grouped(Configuration.Advanced.Debug))
        {
            ImGui.TextDisabled("  " + PriorityPresets.GroupName(group.Key));
            using var indent = ImRaii.PushIndent();
            foreach (PriorityPresets.Preset preset in group)
            {
                string id = PriorityPresets.BuiltInPrefix + preset.Key;
                if (ImGui.Selectable(preset.Short ?? preset.Name, presetId == id))
                    picked = id;
            }
        }

        ImGui.EndCombo();

        if (picked != presetId)
        {
            Configuration.Stop.SetPreset(questId, picked);
            Save();
        }
    }
}
