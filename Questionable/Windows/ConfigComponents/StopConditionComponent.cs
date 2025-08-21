using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;

namespace Questionable.Windows.ConfigComponents;

internal sealed class StopConditionComponent : ConfigComponent
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestSelector _questSelector;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;

    public StopConditionComponent(
        IDalamudPluginInterface pluginInterface,
        QuestSelector questSelector,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestTooltipComponent questTooltipComponent,
        UiUtils uiUtils,
        Configuration configuration)
        : base(pluginInterface, configuration)
    {
        _pluginInterface = pluginInterface;
        _questSelector = questSelector;
        _questRegistry = questRegistry;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;

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
        using var tab = ImRaii.TabItem("Stop###StopConditionns");
        if (!tab)
            return;

        bool enabled = Configuration.Stop.Enabled;
        if (ImGui.Checkbox("Stop Questionable when completing any of the quests selected below", ref enabled))
        {
            Configuration.Stop.Enabled = enabled;
            Save();
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!enabled))
        {
            ImGui.Text("Quests to stop after:");

            _questSelector.DrawSelection();

            List<ElementId> questsToStopAfter = Configuration.Stop.QuestsToStopAfter;
            Quest? itemToRemove = null;
            for (int i = 0; i < questsToStopAfter.Count; i++)
            {
                ElementId questId = questsToStopAfter[i];

                if (!_questRegistry.TryGetQuest(questId, out Quest? quest))
                    continue;

                using (ImRaii.PushId($"Quest{questId}"))
                {
                    var style = _uiUtils.GetQuestStyle(questId);
                    bool hovered;
                    using (var _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
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
                Configuration.Stop.QuestsToStopAfter.Remove(itemToRemove.Id);
                Save();
            }
        }
    }
}
