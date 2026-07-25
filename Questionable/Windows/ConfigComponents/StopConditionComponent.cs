using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Questing;
namespace Questionable.Windows.ConfigComponents;

internal sealed class StopConditionComponent : ConfigComponent
{
    private readonly IClientState _clientState;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelector _acceptQuestSelector;
    private readonly QuestSelector _completeQuestSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    private Vector2 Size;
    internal readonly IEnumerable<string> commandExceptions = ["UI stop", "ESC pressed"];

    public StopConditionComponent(
        IDalamudPluginInterface pluginInterface,
        QuestSelector questSelector,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils,
        IClientState clientState,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _pluginInterface = pluginInterface;
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _clientState = clientState;

        _completeQuestSelector = questSelector;
        _completeQuestSelector.SuggestionPredicate = quest => configuration.Stop.QuestsToStopAfter.All(x => x != quest.Id);
        _completeQuestSelector.DefaultPredicate = quest =>
            quest.Info.IsMainScenarioQuest && questFunctions.IsQuestAccepted(quest.Id);
        _completeQuestSelector.QuestSelected = quest =>
        {
            configuration.Stop.QuestsToStopAfter.Add(quest.Id);
            Save();
        };

        _acceptQuestSelector = new QuestSelector(questRegistry)
        {
            SuggestionPredicate = quest => configuration.Stop.QuestsToStopWhenAccepted.All(x => x != quest.Id),
            DefaultPredicate = quest =>
                    quest.Info.IsMainScenarioQuest && !questFunctions.IsQuestAcceptedOrComplete(quest.Id),
            QuestSelected = quest =>
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
        Size = ImGui.GetWindowContentRegionMax();
        var wrap = ImRaii.TextWrapPos(Size.X + 10);

        bool runCommand = Configuration.Stop.RunCommandAfterStop;
        if (ImGui.Checkbox(_L("Run command when Questionable finishes automatic questing"), ref runCommand))
        {
            Configuration.Stop.RunCommandAfterStop = runCommand;
            Save();
        }
        string command = Configuration.Stop.CommandAfterStop;
        if (ImGui.InputText(_L("Command"), ref command, 128))
        {
            Configuration.Stop.CommandAfterStop = command;
            Save();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.Stop.CommandAfterStop))
                Configuration.Stop.CommandAfterStop = "/li auto";
            Save();
        }
        ImGuiComponentsLocal.HelpMarker("Excludes these stop reasons:", commandExceptions.ToArray());

        ImGui.Separator();

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox(_L("Stop Questionable when any of the conditions below are met"), ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!enabled))
        {
            // Level stop condition section
            ImGui.Text(_L("Stop when character level reaches:"));

            bool levelToStopAfter = Configuration.Stop.LevelToStopAfter;
            if (ImGui.Checkbox(_L("Enable level stop condition"), ref levelToStopAfter))
            {
                Configuration.Stop.LevelToStopAfter = levelToStopAfter;
                Save();
            }

            using (ImRaii.Disabled(!levelToStopAfter))
            {
                int targetLevel = Configuration.Stop.TargetLevel;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt(_L("Stop at level"), ref targetLevel, 1, 5))
                {
                    Configuration.Stop.TargetLevel = Math.Max(1, Math.Min(100, targetLevel));
                    Save();
                }

                // Show current level for reference
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
            }

            ImGui.Separator();

            bool removeWhenCompleteConditionMet = Configuration.Stop.RemoveWhenCompleteConditionMet;
            if (ImGui.Checkbox(_L("Remove from list after complete"), ref removeWhenCompleteConditionMet))
            {
                Configuration.Stop.RemoveWhenCompleteConditionMet = removeWhenCompleteConditionMet;
                Save();
            }

            DrawQuestStopSection(
                _L("Stop when completing any of the quests selected below:"),
                "Complete",
                _completeQuestSelector,
                Configuration.Stop.QuestsToStopAfter,
                () => Configuration.Stop.QuestsToStopAfter.Clear());

            ImGui.Separator();

            DrawQuestStopSection(
                _L("Stop when accepting any of the quests selected below:"),
                "Accept",
                _acceptQuestSelector,
                Configuration.Stop.QuestsToStopWhenAccepted,
                () => Configuration.Stop.QuestsToStopWhenAccepted.Clear());
        }
    }

    private void DrawQuestStopSection(string label, string sectionId, QuestSelector selector, List<ElementId> quests,
        Action clearAll)
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
                    (Vector4 Color, FontAwesomeIcon Icon, string Status) = _uiUtils.GetQuestStyle(questId);
                    bool hovered;
                    using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(Color, Icon.ToIconString());
                        hovered = ImGui.IsItemHovered();
                    }

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(quest.Info.Name);
                    hovered |= ImGui.IsItemHovered();

                    if (hovered)
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
                Save();
            }
        }
    }
}
