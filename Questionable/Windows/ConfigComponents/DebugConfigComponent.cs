using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Questionable.PathData;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.ConfigComponents;

internal sealed class DebugConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration, PathDataUpdater pathDataUpdater) : ConfigComponent(pluginInterface, configuration)
{
    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Advanced###Debug");
        if (!tab)
            return;

        ImGui.TextColored(ImGuiColors.DalamudRed,
            _L("Enabling any option here may cause unexpected behavior. Use at your own risk."));

        ImGui.Separator();

        bool neverFly = Configuration.Advanced.NeverFly;
        if (ImGui.Checkbox(_L("Disable flying (even if unlocked for the zone)"), ref neverFly))
        {
            Configuration.Advanced.NeverFly = neverFly;
            Save();
        }

        if (ImGui.CollapsingHeader(_L("Information")))
        {
            using (ImRaii.PushIndent())
            {
                bool debugOverlay = Configuration.Advanced.DebugOverlay;
                if (ImGui.Checkbox(_L("Enable debug overlay"), ref debugOverlay))
                {
                    Configuration.Advanced.DebugOverlay = debugOverlay;
                    Save();
                }

                using (ImRaii.Disabled(!debugOverlay))
                {
                    using (ImRaii.PushIndent())
                    {
                        bool combatDataOverlay = Configuration.Advanced.CombatDataOverlay;
                        if (ImGui.Checkbox(_L("Enable combat data overlay"), ref combatDataOverlay))
                        {
                            Configuration.Advanced.CombatDataOverlay = combatDataOverlay;
                            Save();
                        }
                    }
                }

                bool highlightNpc = Configuration.Advanced.HighlightSelectedNpc;
                if (ImGui.Checkbox(_L("Highlight NPCs related to the current quest sequence"), ref highlightNpc))
                {
                    Configuration.Advanced.HighlightSelectedNpc = highlightNpc;
                    Save();
                }

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

                bool additionalStatusInformation = Configuration.Advanced.AdditionalStatusInformation;
                if (ImGui.Checkbox(_L("Draw additional status information"), ref additionalStatusInformation))
                {
                    Configuration.Advanced.AdditionalStatusInformation = additionalStatusInformation;
                    Save();
                }

                if (additionalStatusInformation)
                {
                    bool showTracked = Configuration.Advanced.ShowTracked;
                    bool showDailies = Configuration.Advanced.ShowDailies;
                    bool showDirector = Configuration.Advanced.ShowDirector;
                    bool showActionManager = Configuration.Advanced.ShowActionManager;
                    bool showNewGamePlus = Configuration.Advanced.ShowNewGamePlus;
                    using (ImRaii.PushIndent())
                    {
                        ImGui.AlignTextToFramePadding();
                        if (ImGui.Checkbox(_L("Show Tracked Quests"), ref showTracked))
                        {
                            Configuration.Advanced.ShowTracked = showTracked;
                            Save();
                        }

                        if (ImGui.Checkbox(_L("Show Accepted/Complete Daily Quests"), ref showDailies))
                        {
                            Configuration.Advanced.ShowDailies = showDailies;
                            Save();
                        }

                        if (ImGui.Checkbox(_L("Show Director info"), ref showDirector))
                        {
                            Configuration.Advanced.ShowDirector = showDirector;
                            Save();
                        }

                        if (ImGui.Checkbox(_L("Show Action Manager"), ref showActionManager))
                        {
                            Configuration.Advanced.ShowActionManager = showActionManager;
                            Save();
                        }

                        if (ImGui.Checkbox(_L("Show NG+ Chapter"), ref showNewGamePlus))
                        {
                            Configuration.Advanced.ShowNewGamePlus = showNewGamePlus;
                            Save();
                        }
                    }
                }
            }
        }

        ImGui.Separator();

        ImGui.Text(_L("AutoDuty Settings"));
        using (ImRaii.PushIndent())
        {
            ImGui.AlignTextToFramePadding();
            bool disableAutoDutyBareMode = Configuration.Advanced.DisableAutoDutyBareMode;
            if (ImGui.Checkbox(_L("Use Pre-Loop/Loop/Post-Loop settings"), ref disableAutoDutyBareMode))
            {
                Configuration.Advanced.DisableAutoDutyBareMode = disableAutoDutyBareMode;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                _L("Typically, the loop settings for AutoDuty are disabled when running dungeons with Questionable, since they can cause issues (or even shut down your PC)."));
        }

        ImGui.Separator();
        if (ImGui.CollapsingHeader(_L("Quest/Interaction Skips")))
        {
            using (ImRaii.PushIndent())
            {
                bool skipAetherCurrents = Configuration.Advanced.SkipAetherCurrents;
                if (ImGui.Checkbox(_L("Don't pick up aether currents/aether current quests"), ref skipAetherCurrents))
                {
                    Configuration.Advanced.SkipAetherCurrents = skipAetherCurrents;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("If not done during the MSQ by Questionable, you have to manually pick up any missed aether currents/quests. You can set Questionable to run all pending aether current quests using the Priority Quests preset system."));

                bool skipClassJobQuests = Configuration.Advanced.SkipClassJobQuests;
                if (ImGui.Checkbox(_L("Don't pick up class/job/role quests"), ref skipClassJobQuests))
                {
                    Configuration.Advanced.SkipClassJobQuests = skipClassJobQuests;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Class and job skills for A Realm Reborn, Heavensward and (for the Lv70 skills) Stormblood are locked behind quests. Not recommended if you plan on queueing for instances with duty finder/party finder."));

                bool skipARealmRebornHardModePrimals = Configuration.Advanced.SkipARealmRebornHardModePrimals;
                if (ImGui.Checkbox(_L("Don't pick up ARR hard mode primal quests"), ref skipARealmRebornHardModePrimals))
                {
                    Configuration.Advanced.SkipARealmRebornHardModePrimals = skipARealmRebornHardModePrimals;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Hard mode Ifrit/Garuda/Titan are required for the Patch 2.5 quest 'Good Intentions' and to start Heavensward."));

                bool skipCrystalTowerRaids = Configuration.Advanced.SkipCrystalTowerRaids;
                if (ImGui.Checkbox(_L("Don't pick up Crystal Tower quests"), ref skipCrystalTowerRaids))
                {
                    Configuration.Advanced.SkipCrystalTowerRaids = skipCrystalTowerRaids;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Crystal Tower raids are required for the Patch 2.55 quest 'A Time to Every Purpose' and to start Heavensward."));

                bool preventQuestCompletion = Configuration.Advanced.PreventQuestCompletion;
                if (ImGui.Checkbox(_L("Prevent quest completion"), ref preventQuestCompletion))
                {
                    Configuration.Advanced.PreventQuestCompletion = preventQuestCompletion;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will not attempt to turn-in and complete quests. This will do everything automatically except the final turn-in step."));

                bool abandonQuestBeforeCompletion = Configuration.Advanced.AbandonQuestBeforeCompletion;
                bool removeFromPriorityWhenAbandoned = Configuration.Advanced.RemoveFromPriorityWhenAbandoned;
                if (ImGui.Checkbox(_L("Abandon quest before completion"), ref abandonQuestBeforeCompletion))
                {
                    Configuration.Advanced.AbandonQuestBeforeCompletion = abandonQuestBeforeCompletion;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will attempt to send an AbandonQuest command to the server when it arrives at the CompleteQuest step. " +
                    "This setting is reset to Off when the plugin is loaded to avoid confusion with quests not being completed."));
                if (ImGui.Checkbox(_L("Remove from priority when abandoned"), ref removeFromPriorityWhenAbandoned))
                {
                    Configuration.Advanced.RemoveFromPriorityWhenAbandoned = removeFromPriorityWhenAbandoned;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable will also remove a quest from the priority queue when it is abandoned. " +
                    "This setting is reset to Off when the plugin is loaded to avoid confusion with quests not being completed."));

                bool namazuPreferCraft = Configuration.Advanced.NamazuPreferCraft;
                if (ImGui.Checkbox(_L("Namazu: prefer Crafting job over Gatherer"), ref namazuPreferCraft))
                {
                    Configuration.Advanced.NamazuPreferCraft = namazuPreferCraft;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Namazu tribe quests can be done as either DoH or DoL, this lets you set that preference."));

                bool showWindowOnStart = Configuration.Advanced.ShowWindowOnStart;
                if (ImGui.Checkbox(_L("Show window on start"), ref showWindowOnStart))
                {
                    Configuration.Advanced.ShowWindowOnStart = showWindowOnStart;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable's progress window will show when the plugin is loaded."));

                bool startMinimized = Configuration.Advanced.StartMinimized;
                if (ImGui.Checkbox(_L("Start minimized"), ref startMinimized))
                {
                    Configuration.Advanced.StartMinimized = startMinimized;
                    Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("When enabled, Questionable's progress window will be in its minimized state when loaded."));

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

        ImGui.Separator();
        ImGui.Text(_L("Path data"));
        using (ImRaii.PushIndent())
        {
            bool autoUpdatePaths = Configuration.PathData.AutoUpdate;
            if (ImGui.Checkbox(_L("Automatically download quest/gathering path updates"), ref autoUpdatePaths))
            {
                Configuration.PathData.AutoUpdate = autoUpdatePaths;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_L("Downloads newer quest/gathering paths without needing a full plugin update."));

            if (ImGui.Button(_L("Check for path updates now")))
                pathDataUpdater.CheckForUpdatesManually();

            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudGrey, pathDataUpdater.Status);

            long installedVersion = Configuration.PathData.InstalledDataVersion;
            ImGui.TextColored(ImGuiColors.DalamudGrey,
                installedVersion == 0
                    ? _L("Using the path data bundled with the plugin.")
                    : _LF("Downloaded path data version: {0}", installedVersion));
        }
    }
}
