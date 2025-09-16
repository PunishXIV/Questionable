using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Questionable.Controller;
using Questionable.External;

namespace Questionable.Windows.ConfigComponents;

internal sealed class PluginConfigComponent : ConfigComponent
{
    private static readonly IReadOnlyList<PluginInfo> RequiredPlugins =
    [
        new("vnavmesh",
            "vnavmesh",
            """
            vnavmesh handles the navigation within a zone, moving
            your character to the next quest-related objective.
            """,
            new Uri("https://github.com/awgil/ffxiv_navmesh/"),
            new Uri("https://puni.sh/api/repository/veyn")),
        new("Lifestream",
            "Lifestream",
            """
            Used to travel to aethernet shards in cities.
            """,
            new Uri("https://github.com/NightmareXIV/Lifestream"),
            new Uri("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json")),
        new("TextAdvance",
            "TextAdvance",
            """
            Automatically accepts and turns in quests, skips cutscenes
            and dialogue.
            """,
            new Uri("https://github.com/NightmareXIV/TextAdvance"),
            new Uri("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json")),
    ];

    private static readonly ReadOnlyDictionary<Configuration.ECombatModule, PluginInfo> CombatPlugins =
        new Dictionary<Configuration.ECombatModule, PluginInfo>
        {
            {
                Configuration.ECombatModule.BossMod,
                new("Boss Mod (VBM)",
                    "BossMod",
                    string.Empty,
                    new Uri("https://github.com/awgil/ffxiv_bossmod"),
                    new Uri("https://puni.sh/api/repository/veyn"))
            },
            {
                Configuration.ECombatModule.WrathCombo,
                new PluginInfo("Wrath Combo",
                    "WrathCombo",
                    string.Empty,
                    new Uri("https://github.com/PunishXIV/WrathCombo"),
                    new Uri("https://puni.sh/api/plugins"))
            },
            {
                Configuration.ECombatModule.RotationSolverReborn,
                new("Rotation Solver Reborn",
                    "RotationSolver",
                    string.Empty,
                    new Uri("https://github.com/FFXIV-CombatReborn/RotationSolverReborn"),
                    new Uri(
                        "https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json"))
            },
        }.AsReadOnly();

    private readonly IReadOnlyList<PluginInfo> _recommendedPlugins;

    private readonly Configuration _configuration;
    private readonly CombatController _combatController;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly UiUtils _uiUtils;
    private readonly ICommandManager _commandManager;

    public PluginConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        CombatController combatController,
        UiUtils uiUtils,
        ICommandManager commandManager,
        AutomatonIpc automatonIpc,
        PandorasBoxIpc pandorasBoxIpc)
        : base(pluginInterface, configuration)
    {
        _configuration = configuration;
        _combatController = combatController;
        _pluginInterface = pluginInterface;
        _uiUtils = uiUtils;
        _commandManager = commandManager;
        _recommendedPlugins =
        [
            new PluginInfo("CBT (formerly known as Automaton)",
                "Automaton",
                """
                Automaton is a collection of automation-related tweaks.
                """,
                new Uri("https://github.com/Jaksuhn/Automaton"),
                new Uri("https://puni.sh/api/repository/croizat"),
                "/cbt",
                [
                    new PluginDetailInfo("'Sniper no sniping' enabled",
                        "Automatically completes sniping tasks introduced in Stormblood",
                        () => automatonIpc.IsAutoSnipeEnabled)
                ]),
            new PluginInfo("Pandora's Box",
                "PandorasBox",
                """
                Pandora's Box is a collection of tweaks.
                """,
                new Uri("https://github.com/PunishXIV/PandorasBox"),
                new Uri("https://puni.sh/api/plugins"),
                "/pandora",
                [
                    new PluginDetailInfo("'Auto Active Time Maneuver' enabled",
                        """
                        Automatically completes active time maneuvers in
                        single player instances, trials and raids"
                        """,
                        () => pandorasBoxIpc.IsAutoActiveTimeManeuverEnabled)
                ]),
            new("NotificationMaster",
                "NotificationMaster",
                """
                Sends a configurable out-of-game notification if a quest
                requires manual actions.
                """,
                new Uri("https://github.com/NightmareXIV/NotificationMaster"),
                null),
        ];
    }

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem("Dependencies###Plugins");
        if (!tab)
            return;

        Draw(out bool allRequiredInstalled);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (allRequiredInstalled)
            ImGui.TextColored(ImGuiColors.ParsedGreen, "All required plugins are installed.");
        else
            ImGui.TextColored(ImGuiColors.DalamudRed,
                "Required plugins are missing, Questionable will not work properly.");
    }

    public void Draw(out bool allRequiredInstalled)
    {
        float checklistPadding;
        using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            checklistPadding = ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X +
                               ImGui.GetStyle().ItemSpacing.X;
        }

        ImGui.Text("Questionable requires the following plugins to work:");
        allRequiredInstalled = true;
        using (ImRaii.PushIndent())
        {
            foreach (var plugin in RequiredPlugins)
                allRequiredInstalled &= DrawPlugin(plugin, checklistPadding);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Questionable recommends Boss Mod (VBM) for rotation/combat automation.");

        using (ImRaii.Disabled(_combatController.IsRunning))
        {
            using (ImRaii.PushIndent())
            {
                if (ImGui.RadioButton("No rotation/combat plugin (combat must be done manually)",
                        _configuration.General.CombatModule == Configuration.ECombatModule.None))
                {
                    _configuration.General.CombatModule = Configuration.ECombatModule.None;
                    _pluginInterface.SavePluginConfig(_configuration);
                }

                allRequiredInstalled &= DrawCombatPlugin(Configuration.ECombatModule.BossMod, checklistPadding);
                allRequiredInstalled &= DrawCombatPlugin(Configuration.ECombatModule.WrathCombo, checklistPadding);
            }
            ImGui.Text("The following rotation/combat plugin(s) are provided for compatibility and testing purposes:");
            using (ImRaii.PushIndent())
            {
                allRequiredInstalled &=
                    DrawCombatPlugin(Configuration.ECombatModule.RotationSolverReborn, checklistPadding);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("The following plugins are recommended, but not required:");
        using (ImRaii.PushIndent())
        {
            foreach (var plugin in _recommendedPlugins)
                DrawPlugin(plugin, checklistPadding);
        }
    }

    private bool DrawPlugin(PluginInfo plugin, float checklistPadding)
    {
        using (ImRaii.PushId("plugin_" + plugin.DisplayName))
        {
            IExposedPlugin? installedPlugin = FindInstalledPlugin(plugin);
            bool isInstalled = installedPlugin != null;
            string label = plugin.DisplayName;
            if (installedPlugin != null)
                label += $" v{installedPlugin.Version}";

            _uiUtils.ChecklistItem(label, isInstalled);

            DrawPluginDetails(plugin, checklistPadding, isInstalled);
            return isInstalled;
        }
    }

    private bool DrawCombatPlugin(Configuration.ECombatModule combatModule, float checklistPadding)
    {
        ImGui.Spacing();

        PluginInfo plugin = CombatPlugins[combatModule];
        using (ImRaii.PushId("plugin_" + plugin.DisplayName))
        {
            IExposedPlugin? installedPlugin = FindInstalledPlugin(plugin);
            bool isInstalled = installedPlugin != null;
            string label = plugin.DisplayName;
            if (installedPlugin != null)
                label += $" v{installedPlugin.Version}";

            if (ImGui.RadioButton(label, _configuration.General.CombatModule == combatModule))
            {
                _configuration.General.CombatModule = combatModule;
                _pluginInterface.SavePluginConfig(_configuration);
            }

            ImGui.SameLine(0);
            using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                var iconColor = isInstalled ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
                var icon = isInstalled ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;

                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(iconColor, icon.ToIconString());
            }

            DrawPluginDetails(plugin, checklistPadding, isInstalled);
            return isInstalled || _configuration.General.CombatModule != combatModule;
        }
    }

    private void DrawPluginDetails(PluginInfo plugin, float checklistPadding, bool isInstalled)
    {
        using (ImRaii.PushIndent(checklistPadding))
        {
            if (!string.IsNullOrEmpty(plugin.Details))
                ImGui.TextUnformatted(plugin.Details);

            bool allDetailsOk = true;
            if (plugin.DetailsToCheck != null)
            {
                foreach (var detail in plugin.DetailsToCheck)
                {
                    bool detailOk = detail.Predicate();
                    allDetailsOk &= detailOk;

                    _uiUtils.ChecklistItem(detail.DisplayName, isInstalled && detailOk);
                    if (!string.IsNullOrEmpty(detail.Details))
                    {
                        using (ImRaii.PushIndent(checklistPadding))
                        {
                            ImGui.TextUnformatted(detail.Details);
                        }
                    }
                }
            }

            ImGui.Spacing();

            if (isInstalled)
            {
                if (!allDetailsOk && plugin.ConfigCommand != null && plugin.ConfigCommand.StartsWith('/'))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Cog, "Open configuration"))
                        _commandManager.ProcessCommand(plugin.ConfigCommand);
                }
            }
            else
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Globe, "Open Website"))
                    Util.OpenLink(plugin.WebsiteUri.ToString());

                ImGui.SameLine();
                if (plugin.DalamudRepositoryUri != null)
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Code, "Open Repository"))
                        Util.OpenLink(plugin.DalamudRepositoryUri.ToString());
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    ImGuiComponents.HelpMarker("Available on official Dalamud Repository");
                }
            }
        }
    }

    private IExposedPlugin? FindInstalledPlugin(PluginInfo pluginInfo)
    {
        return _pluginInterface.InstalledPlugins.FirstOrDefault(x =>
            x.InternalName == pluginInfo.InternalName && x.IsLoaded);
    }

    private sealed record PluginInfo(
        string DisplayName,
        string InternalName,
        string Details,
        Uri WebsiteUri,
        Uri? DalamudRepositoryUri,
        string? ConfigCommand = null,
        List<PluginDetailInfo>? DetailsToCheck = null);

    private sealed record PluginDetailInfo(string DisplayName, string Details, Func<bool> Predicate);
}
