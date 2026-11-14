using Dalamud.Interface.Colors;
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
    private readonly NotificationConfigComponent _notifications;
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
        NotificationConfigComponent notifications,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _presets = presets;
        _notifications = notifications;
        _clientState = clientState;

        _completeQuestSelector = questSelector;
        _completeQuestSelector.SuggestionPredicate = quest => configuration.Stop.QuestsToStopAfter.All(x => x != quest.Id);
        // Both lists default to the main scenario quests still ahead of this character.
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

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Stop") + "###StopConditionns");
        if (!tab)
            return;
        _contentSize = ImGui.GetWindowContentRegionMax();
        using var wrap = ImRaii.TextWrapPos(_contentSize.X + 10);

        DrawConditions();
        ImGui.Separator();
        DrawAfterStop();
        ImGui.Separator();
        ImGui.TextColored(QstTheme.Accent, _L("Notifications"));
        _notifications.DrawSection();
    }

    private void DrawConditions()
    {
        ImGui.TextColored(QstTheme.Accent, _L("Stop the run when"));

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox(_L("Stop Questionable when any of the conditions below are met"), ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        using ImRaii.ColorDisposable muted = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !enabled);
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
        if (ImGui.Checkbox(_L("Remove from list after complete"), ref removeOnComplete))
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
        ImGui.TextColored(QstTheme.Accent, _L("After the run stops"));

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
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Travels there with Lifestream first. The commands below wait until you have arrived."));

        bool runCommand = Configuration.Stop.RunCommandAfterStop;
        if (ImGui.Checkbox(_L("Run command when Questionable finishes automatic questing"), ref runCommand))
        {
            Configuration.Stop.RunCommandAfterStop = runCommand;
            Save();
        }

        using (ImRaii.PushIndent())
        {
            string command = Configuration.Stop.CommandAfterStop;
            // One line tall, growing with each command added.
            Vector2 commandSize = new(ImGui.CalcItemWidth(),
                ImGui.GetTextLineHeight() * (command.Count(c => c == '\n') + 1) + ImGui.GetStyle().FramePadding.Y * 2);
            if (ImGui.InputTextMultiline("##Command", ref command, 512, commandSize))
            {
                Configuration.Stop.CommandAfterStop = command;
                Save();
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                Save();
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
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("For SND scripts or commands that keep running: Questionable waits this long after the commands before logging out, closing the game or switching to the next character. Lifestream travel is waited for on its own."));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_L("After that"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        DrawComboOption("##EndAction", _endActions, _endActions.Select(QuestStopPolicy.GetName).ToArray(),
            () => Configuration.Stop.EndAction, x => Configuration.Stop.EndAction = x);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("What Questionable does once the commands have run. The game shows its own countdown, which you can cancel. With a character list, this waits for the last character."));

        DrawCheckbox(_L("Do this after a problem stop too (stuck, too many deaths, missing path)"),
            () => Configuration.Stop.RunAfterProblemStop, x => Configuration.Stop.RunAfterProblemStop = x,
            _L("Off: travel, the commands and the action above only run when a run finished: nothing left to do, a stop condition above, or one of the stop buttons in the main window. A character list also stops at a character that hit a problem."));
    }

    private void DrawQuestStopSection(string label, string sectionId, QuestSelector selector, List<ElementId> quests,
        Action clearAll, bool withPreset = false)
    {
        using (ImRaii.PushId(sectionId))
        {
            ImGui.Text(label);
            selector.DrawSelection();

            if (quests.Count > 0)
            {
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Clear All")))
                    {
                        clearAll();
                        Save();
                    }
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(_L("Hold CTRL to enable this button."));
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

                    if (ImGuiComponentsLocal.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
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
            ImGui.SetTooltip(_L("When this quest completes: stop, or add a preset to the priority quests and keep going."));
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
        foreach (PriorityPresets.Preset preset in _presets.BuiltIn)
        {
            string id = PriorityPresets.BuiltInPrefix + preset.Key;
            if (ImGui.Selectable(preset.Name, presetId == id))
                picked = id;
        }

        ImGui.EndCombo();

        if (picked != presetId)
        {
            Configuration.Stop.SetPreset(questId, picked);
            Save();
        }
    }
}
