using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;

namespace Questionable.Windows.ConfigComponents;

internal sealed class DebugConfigComponent : ConfigComponent
{
    public DebugConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration)
        : base(pluginInterface, configuration)
    {
    }

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem("Advanced###Debug");
        if (!tab)
            return;

        ImGui.TextColored(ImGuiColors.DalamudRed,
            "Enabling any option here may cause unexpected behavior. Use at your own risk.");

        ImGui.Separator();

        bool debugOverlay = Configuration.Advanced.DebugOverlay;
        if (ImGui.Checkbox("Enable debug overlay", ref debugOverlay))
        {
            Configuration.Advanced.DebugOverlay = debugOverlay;
            Save();
        }

        using (ImRaii.Disabled(!debugOverlay))
        {
            using (ImRaii.PushIndent())
            {
                bool combatDataOverlay = Configuration.Advanced.CombatDataOverlay;
                if (ImGui.Checkbox("Enable combat data overlay", ref combatDataOverlay))
                {
                    Configuration.Advanced.CombatDataOverlay = combatDataOverlay;
                    Save();
                }
            }
        }

        bool neverFly = Configuration.Advanced.NeverFly;
        if (ImGui.Checkbox("Disable flying (even if unlocked for the zone)", ref neverFly))
        {
            Configuration.Advanced.NeverFly = neverFly;
            Save();
        }

        bool additionalStatusInformation = Configuration.Advanced.AdditionalStatusInformation;
        if (ImGui.Checkbox("Draw additional status information", ref additionalStatusInformation))
        {
            Configuration.Advanced.AdditionalStatusInformation = additionalStatusInformation;
            Save();
        }

        ImGui.Separator();

        ImGui.Text("AutoDuty Settings");
        using (ImRaii.PushIndent())
        {
            ImGui.AlignTextToFramePadding();
            bool disableAutoDutyBareMode = Configuration.Advanced.DisableAutoDutyBareMode;
            if (ImGui.Checkbox("Use Pre-Loop/Loop/Post-Loop settings", ref disableAutoDutyBareMode))
            {
                Configuration.Advanced.DisableAutoDutyBareMode = disableAutoDutyBareMode;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "Typically, the loop settings for AutoDuty are disabled when running dungeons with Questionable, since they can cause issues (or even shut down your PC).");
        }

        ImGui.Separator();
        ImGui.Text("Quest/Interaction Skips");
        using (ImRaii.PushIndent())
        {
            bool skipAetherCurrents = Configuration.Advanced.SkipAetherCurrents;
            if (ImGui.Checkbox("Don't pick up aether currents/aether current quests", ref skipAetherCurrents))
            {
                Configuration.Advanced.SkipAetherCurrents = skipAetherCurrents;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker("If not done during the MSQ by Questionable, you have to manually pick up any missed aether currents/quests. There is no way to automatically pick up all missing aether currents.");

            bool skipClassJobQuests = Configuration.Advanced.SkipClassJobQuests;
            if (ImGui.Checkbox("Don't pick up class/job/role quests", ref skipClassJobQuests))
            {
                Configuration.Advanced.SkipClassJobQuests = skipClassJobQuests;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Class and job skills for A Realm Reborn, Heavensward and (for the Lv70 skills) Stormblood are locked behind quests. Not recommended if you plan on queueing for instances with duty finder/party finder.");

            bool skipARealmRebornHardModePrimals = Configuration.Advanced.SkipARealmRebornHardModePrimals;
            if (ImGui.Checkbox("Don't pick up ARR hard mode primal quests", ref skipARealmRebornHardModePrimals))
            {
                Configuration.Advanced.SkipARealmRebornHardModePrimals = skipARealmRebornHardModePrimals;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Hard mode Ifrit/Garuda/Titan are required for the Patch 2.5 quest 'Good Intentions' and to start Heavensward.");

            bool skipCrystalTowerRaids = Configuration.Advanced.SkipCrystalTowerRaids;
            if (ImGui.Checkbox("Don't pick up Crystal Tower quests", ref skipCrystalTowerRaids))
            {
                Configuration.Advanced.SkipCrystalTowerRaids = skipCrystalTowerRaids;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Crystal Tower raids are required for the Patch 2.55 quest 'A Time to Every Purpose' and to start Heavensward.");
        }
    }
}
