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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace Questionable.Windows.ConfigComponents;

internal sealed class StopConditionComponent : ConfigComponent
{
    private readonly IClientState _clientState;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelector _questSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    //private readonly IPlayerState _playerState;

    public StopConditionComponent(
        IDalamudPluginInterface pluginInterface,
        QuestSelector questSelector,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils,
        IClientState clientState,
        //IPlayerState playerState,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _pluginInterface = pluginInterface;
        _questSelector = questSelector;
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _clientState = clientState;
        //_playerState = playerState;

        _questSelector.SuggestionPredicate = quest => configuration.Stop.QuestsToStopAfter.All(x => x != quest.Id);
        _questSelector.DefaultPredicate = quest => quest.Info.IsMainScenarioQuest && questFunctions.IsQuestAccepted(quest.Id);
        _questSelector.QuestSelected = quest =>
        {
            configuration.Stop.QuestsToStopAfter.Add(quest.Id);
            Save();
        };
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Stop###StopConditionns");
        if (!tab)
        {
            return;
        }

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

            // Quest completion stop condition section
            ImGui.Text("Stop when completing any of the quests selected below:");

            _questSelector.DrawSelection();

            List<ElementId> questsToStopAfter = Configuration.Stop.QuestsToStopAfter;

            // 'Clear All' button if there are quests to clear for fast removal
            if (questsToStopAfter.Count > 0)
            {
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Clear All"))
                    {
                        Configuration.Stop.QuestsToStopAfter.Clear();
                        Save();
                    }
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Hold CTRL to enable this button.");
                }

                ImGui.Separator();
            }

            Quest? itemToRemove = null;
            for(int i = 0; i < questsToStopAfter.Count; i++)
            {
                ElementId questId = questsToStopAfter[i];

                if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    continue;
                }

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
                    {
                        _questTooltipComponent.Draw(quest.Info);
                    }

                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * 2);
                    }

                    if (ImGuiComponents.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                    {
                        itemToRemove = quest;
                    }
                }
            }

            if (itemToRemove != null)
            {
                Configuration.Stop.QuestsToStopAfter.Remove(itemToRemove.Id);
                Save();
            }
        }
    }
}
