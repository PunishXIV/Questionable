using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
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

        _acceptQuestSelector = new QuestSelector(questRegistry);
        _acceptQuestSelector.SuggestionPredicate = quest => configuration.Stop.QuestsToStopWhenAccepted.All(x => x != quest.Id);
        _acceptQuestSelector.DefaultPredicate = quest =>
            quest.Info.IsMainScenarioQuest && !questFunctions.IsQuestAcceptedOrComplete(quest.Id);
        _acceptQuestSelector.QuestSelected = quest =>
        {
            configuration.Stop.QuestsToStopWhenAccepted.Add(quest.Id);
            Save();
        };
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Stop###StopConditionns");
        if (!tab)
            return;

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox("Stop Questionable when any of the conditions below are met", ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!enabled))
        {
            // Level stop condition section
            ImGui.Text("Stop when character level reaches:");

            bool levelToStopAfter = Configuration.Stop.LevelToStopAfter;
            if (ImGui.Checkbox("Enable level stop condition", ref levelToStopAfter))
            {
                Configuration.Stop.LevelToStopAfter = levelToStopAfter;
                Save();
            }

            using (ImRaii.Disabled(!levelToStopAfter))
            {
                int targetLevel = Configuration.Stop.TargetLevel;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Stop at level", ref targetLevel, 1, 5))
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
                        ImGui.TextDisabled($"(Current: {currentLevel})");
                    }
                }
            }

            ImGui.Separator();

            DrawQuestStopSection(
                "Stop when completing any of the quests selected below:",
                _completeQuestSelector,
                Configuration.Stop.QuestsToStopAfter,
                () => Configuration.Stop.QuestsToStopAfter.Clear());

            ImGui.Separator();

            DrawQuestStopSection(
                "Stop when accepting any of the quests selected below:",
                _acceptQuestSelector,
                Configuration.Stop.QuestsToStopWhenAccepted,
                () => Configuration.Stop.QuestsToStopWhenAccepted.Clear());
        }
    }

    private void DrawQuestStopSection(string label, QuestSelector selector, List<ElementId> quests,
        Action clearAll)
    {
        ImGui.Text(label);
        selector.DrawSelection();

        if (quests.Count > 0)
        {
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Clear All"))
                {
                    clearAll();
                    Save();
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold CTRL to enable this button.");

            ImGui.Separator();
        }

        Quest? itemToRemove = null;
        for (int i = 0; i < quests.Count; i++)
        {
            ElementId questId = quests[i];

            if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                continue;

            using (ImRaii.PushId($"Quest{questId}"))
            {
                (Vector4 Color, FontAwesomeIcon Icon, string Status) style = _uiUtils.GetQuestStyle(questId);
                bool hovered;
                using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(style.Color, style.Icon.ToIconString());
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

                if (ImGuiComponents.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
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
