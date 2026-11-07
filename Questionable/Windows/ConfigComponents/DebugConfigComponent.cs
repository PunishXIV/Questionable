using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Questionable.AutoGen.Plugin;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class DebugConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    DraftQuestPathService draftQuestPathService) : ConfigComponent(pluginInterface, configuration)
{
    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Advanced") + "###Debug");
        if (!tab)
            return;

        ImGui.TextColored(QstTheme.Danger,
            _L("Enabling any option here may cause unexpected behavior. Use at your own risk."));

        ImGui.Separator();

        DrawCheckbox(_L("Disable flying (even if unlocked for the zone)"),
            () => Configuration.Advanced.NeverFly,
            value => Configuration.Advanced.NeverFly = value);

        ImGui.Separator();

        bool allowPathGeneration = DrawCheckbox(_L("Allow questpath generation (experimental)"),
            () => Configuration.Advanced.AllowPathGeneration,
            value => Configuration.Advanced.AllowPathGeneration = value);

        if (allowPathGeneration)
        {
            using (ImRaii.PushIndent())
            {
                ImGui.TextColored(QstTheme.Accent,
                    _L("Generated paths are unreviewed machine drafts: expect wrong targets, missing steps and stalls."));
                ImGui.TextColored(QstTheme.Accent,
                    _L("Stay at the keyboard while one is running - never leave it unattended."));
                ImGui.TextUnformatted(
                    _L("Right-click a quest without a path in the Journal Progress window to generate a draft."));

                if (!draftQuestPathService.UserDirectoryIsLoaded)
                {
                    ImGui.TextColored(QstTheme.Danger,
                        _L("Generated paths only load in debug mode or on a dev install; without one of those, this option does nothing."));
                }
            }
        }

        string displayName = Configuration.General.DisplayName;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2);
        if (ImGui.InputText(_L("Display name"), ref displayName, 20))
            Configuration.General.DisplayName = displayName;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.General.DisplayName))
                Configuration.General.DisplayName = _L("Anonymous");
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.Text(_L("Your name as the author of quest paths you create or generate here."));
                ImGui.Text(_L("Defaults to \"Anonymous\" if left blank."));
            }
        }

        ImGui.Separator();

        if (QstWidgets.SectionHeader(_L("Information"), "Information", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                bool debugOverlay = DrawCheckbox(_L("Enable debug overlay"),
                    () => Configuration.Advanced.DebugOverlay,
                    value => Configuration.Advanced.DebugOverlay = value);

                using (ImRaii.Disabled(!debugOverlay))
                {
                    using (ImRaii.PushIndent())
                    {
                        DrawCheckbox(_L("Show saved position"),
                            () => Configuration.Advanced.ShowSavedPos,
                            value => Configuration.Advanced.ShowSavedPos = value);

                        DrawCheckbox(_L("Show flag position"),
                            () => Configuration.Advanced.ShowFlagPos,
                            value => Configuration.Advanced.ShowFlagPos = value);

                        DrawCheckbox(_L("Enable combat data overlay"),
                            () => Configuration.Advanced.CombatDataOverlay,
                            value => Configuration.Advanced.CombatDataOverlay = value);
                    }
                }

                bool highlightNpc = DrawCheckbox(_L("Highlight NPCs related to the current quest sequence"),
                    () => Configuration.Advanced.HighlightSelectedNpc,
                    value => Configuration.Advanced.HighlightSelectedNpc = value);

                using (ImRaii.Disabled(!highlightNpc))
                {
                    using (ImRaii.PushIndent())
                    {
                        ImGui.SetNextItemWidth(150f);
                        DrawComboOption(("Highlight Color"),
                            Enum.GetValues<ObjectHighlightColor>(),
                            Enum.GetNames<ObjectHighlightColor>(),
                            () => Configuration.Advanced.HighlightColor,
                            v => Configuration.Advanced.HighlightColor = v);
                    }
                }

                bool showStatusInfo = DrawCheckbox(_L("Draw additional status information"),
                    () => Configuration.Advanced.AdditionalStatusInformation,
                    value => Configuration.Advanced.AdditionalStatusInformation = value);

                if (showStatusInfo)
                {
                    using (ImRaii.PushIndent())
                    {
                        ImGui.AlignTextToFramePadding();
                        DrawCheckbox(_L("Show Tracked Quests"),
                            () => Configuration.Advanced.ShowTracked,
                            value => Configuration.Advanced.ShowTracked = value);
                        DrawCheckbox(_L("Show Accepted/Complete Daily Quests"),
                            () => Configuration.Advanced.ShowDailies,
                            value => Configuration.Advanced.ShowDailies = value);
                        DrawCheckbox(_L("Show Director info"),
                            () => Configuration.Advanced.ShowDirector,
                            value => Configuration.Advanced.ShowDirector = value);
                        DrawCheckbox(_L("Show Action Manager"),
                            () => Configuration.Advanced.ShowActionManager,
                            value => Configuration.Advanced.ShowActionManager = value);
                        DrawCheckbox(_L("Show NG+ Chapter"),
                            () => Configuration.Advanced.ShowNewGamePlus,
                            value => Configuration.Advanced.ShowNewGamePlus = value);
                        DrawCheckbox(_L("Show Hovered Item"),
                            () => Configuration.Advanced.ShowHoveredItem,
                            value => Configuration.Advanced.ShowHoveredItem = value);
                    }
                }
            }
        }

        ImGui.Separator();
        if (QstWidgets.SectionHeader(_L("Testing"), "QuestSkips", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                if (Configuration.Advanced.Debug)
                {
                    bool stopMsq = Configuration.Advanced.StopMSQ;
                    if (ImGui.Checkbox("Stop MSQ", ref stopMsq))
                    {
                        Configuration.Advanced.StopMSQ = stopMsq;
                        Save();
                    }
                }

                bool preventQuestCompletion = Configuration.Advanced.PreventQuestCompletion;
                if (ImGui.Checkbox(_L("Prevent quest completion"), ref preventQuestCompletion))
                {
                    Configuration.Advanced.PreventQuestCompletion = preventQuestCompletion;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will not attempt to turn-in and complete quests. This will do everything automatically except the final turn-in step."));

                bool skipCraftworksDelivery = Configuration.Advanced.SkipCraftworksDelivery;
                if (ImGui.Checkbox(_L("Testing: fill Wachumeqimeqi turn-ins without delivering"), ref skipCraftworksDelivery))
                {
                    Configuration.Advanced.SkipCraftworksDelivery = skipCraftworksDelivery;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Questionable puts the items into the Wachumeqimeqi delivery window but doesn't press Deliver, so the quest isn't used up. Close the window and talk to the NPC again to retry."));

                bool abandonBeforeComplete = Configuration.Advanced.AbandonQuestBeforeCompletion;
                bool removeOnAbandon = Configuration.Advanced.RemoveFromPriorityWhenAbandoned;
                if (ImGui.Checkbox(_L("Abandon quest before completion"), ref abandonBeforeComplete))
                {
                    Configuration.Advanced.AbandonQuestBeforeCompletion = abandonBeforeComplete;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will attempt to send an AbandonQuest command to the server when it arrives at the CompleteQuest step. " +
                    "This setting is reset to Off when the plugin is loaded to avoid confusion with quests not being completed."));
                if (ImGui.Checkbox(_L("Remove from priority when abandoned"), ref removeOnAbandon))
                {
                    Configuration.Advanced.RemoveFromPriorityWhenAbandoned = removeOnAbandon;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will also remove a quest from the priority queue when it is abandoned. " +
                    "This setting is reset to Off when the plugin is loaded to avoid confusion with quests not being completed."));

                bool openEditor = Configuration.Advanced.OpenEditor;
                if (ImGui.Checkbox(_L("Open editor when starting quest"), ref openEditor))
                {
                    Configuration.Advanced.OpenEditor = openEditor;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("On starting a quest, Questionable will open the path for it in your default text editor."));
            }
        }
    }
}
