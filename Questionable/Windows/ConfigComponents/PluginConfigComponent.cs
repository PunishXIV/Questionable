using System.Collections.ObjectModel;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ECommons.ImGuiMethods;
using Questionable.Model.Common;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.ConfigComponents;

internal sealed class PluginConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    CombatController combatController,
    UiUtils uiUtils,
    ICommandManager commandManager,
    AutomatonIpc automatonIpc,
    PandorasBoxIpc pandorasBoxIpc) : ConfigComponent(pluginInterface, configuration)
{
    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;
    private readonly Configuration _configuration = configuration;
    private readonly CombatController _combatController = combatController;
    private readonly UiUtils _uiUtils = uiUtils;
    private readonly ICommandManager _commandManager = commandManager;

    private static readonly ReadOnlyDictionary<ECombatModule, PluginInfo> CombatPlugins =
        new Dictionary<ECombatModule, PluginInfo>
        {
            {
                ECombatModule.BossMod,
                new("Boss Mod (VBM)",
                    "BossMod",
                    "Automates all kinds of combat and interaction in overworld and duty content",
                    new("https://github.com/awgil/ffxiv_bossmod"),
                    new("https://puni.sh/api/repository/veyn"),
                    "/vbm")
            },
            {
                ECombatModule.WrathCombo,
                new("Wrath Combo",
                    "WrathCombo",
                    string.Empty,
                    new("https://github.com/PunishXIV/WrathCombo"),
                    new("https://puni.sh/api/plugins"),
                    "/wrath")
            },
            {
                ECombatModule.RotationSolverReborn,
                new("Rotation Solver Reborn",
                    "RotationSolver",
                    string.Empty,
                    new("https://github.com/FFXIV-CombatReborn/RotationSolverReborn"),
                    new(
                        "https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json"),
                    "/rsr")
            }
        }.AsReadOnly();

    private static readonly IReadOnlyList<PluginInfo> RequiredPlugins =
    [
        new("vnavmesh",
            "vnavmesh",
            _L("vnavmesh handles the navigation within a zone, moving your character to the next quest-related objective."),
            new("https://github.com/awgil/ffxiv_navmesh/"),
            new("https://puni.sh/api/repository/veyn"),
            "/vnav"),
        new("Lifestream",
            "Lifestream",
            _L("Used to travel to aethernet shards in cities."),
            new("https://github.com/NightmareXIV/Lifestream"),
            new("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json"),
            "/lifestream"),
        new("TextAdvance",
            "TextAdvance",
            _L("Automatically accepts and turns in quests, skips cutscenes and dialogue."),
            new("https://github.com/NightmareXIV/TextAdvance"),
            new("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json"),
            "/at c"),
        //CombatPlugins[ECombatModule.BossMod]
    ];

    private readonly IReadOnlyList<PluginInfo> _recommendedPlugins =
    [
        new("Artisan",
            "Artisan",
            _L("Automates crafting"),
            new("https://github.com/PunishXIV/Artisan"),
            new("https://puni.sh/api/plugins"),
            "/artisan"),
        new("AutoDuty",
            "AutoDuty",
            _L("Automates duties"),
            new("https://github.com/erdelf/AutoDuty"),
            new("https://puni.sh/api/repository/erdelf"),
            "/ad"),
        new("AutoHook",
            "AutoHook",
            _L("Automates fishing"),
            new("https://github.com/PunishXIV/AutoHook"),
            new("https://puni.sh/api/plugins"),
            "/autohook"),
        new("CBT (formerly known as Automaton)",
            "Automaton",
            _L("Automaton is a collection of automation-related tweaks."),
            new("https://github.com/Jaksuhn/Automaton"),
            new("https://puni.sh/api/repository/croizat"),
            "/cbt",
            [
                new(_L("'Sniper no sniping' enabled"),
                    _L("Automatically completes sniping tasks introduced in Stormblood"),
                    () => automatonIpc.IsAutoSnipeEnabled)
            ]),
        new("MogMail",
            "Mogmail",
            _L("Claim mailed items during QST operation"),
            new("https://github.com/Nexaii/Mogmail"),
            new("https://puni.sh/api/plugins/nexai"),
            "/mogmail"),
        new("NotificationMaster",
            "NotificationMaster",
            _L("Sends a configurable out-of-game notification if a quest requires manual actions."),
            new Uri("https://github.com/NightmareXIV/NotificationMaster"),
            new("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json"),
            "/pnotify"),
        new("Pandora's Box",
            "PandorasBox",
            _L("Pandora's Box is a collection of tweaks."),
            new("https://github.com/PunishXIV/PandorasBox"),
            new("https://puni.sh/api/plugins"),
            "/pandora",
            [
                new(_L("'Auto Active Time Maneuver' enabled"),
                    _L("Automatically completes active time maneuvers in single player instances, trials and raids"),
                    () => pandorasBoxIpc.IsAutoActiveTimeManeuverEnabled)
            ]),
        new("Stylist",
            "Stylist",
            _L("Gear manager"),
            new("https://github.com/NightmareXIV/Stylist"),
            new("https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json"),
            "/stylist c"),
    ];

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Dependencies") + "###Plugins");
        if (!tab)
            return;

        Draw(out bool allRequiredInstalled);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (allRequiredInstalled)
            ImGui.TextColored(QstTheme.Success, _L("All required plugins are installed."));
        else
        {
            ImGui.TextColored(QstTheme.Danger,
                _L("Required plugins are missing, Questionable will not work properly."));
        }
    }

    public void Draw(out bool allRequiredInstalled)
    {
        float checklistPadding;
        using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            checklistPadding = ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X +
                               ImGui.GetStyle().ItemSpacing.X;
        }

        allRequiredInstalled = true;
        ImGui.SetNextItemOpen(isOpen: true, ImGuiCond.Once);
        if (QstWidgets.SectionHeader(_L("Required plugins:"), "RequiredPlugins", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                foreach (PluginInfo plugin in RequiredPlugins)
                    allRequiredInstalled &= DrawPlugin(plugin, checklistPadding);
            }
        }

        if (QstWidgets.SectionHeader(_L("Rotation/Automation plugins:"), "RotationPlugins", defaultOpen: false))
        {
            using (ImRaii.Disabled(_combatController.IsRunning))
            {
                using (ImRaii.PushIndent())
                {
                    if (ImGui.RadioButton(_L("No rotation/combat plugin\n(combat must be done manually)"),
                        _configuration.General.CombatModule == ECombatModule.None))
                    {
                        _configuration.General.CombatModule = ECombatModule.None;
                        _pluginInterface.SavePluginConfig(_configuration);
                    }

                    allRequiredInstalled &= DrawCombatPlugin(ECombatModule.BossMod, checklistPadding);
                }

                ImGui.TextWrapped(_L("The following rotation/combat plugin(s) are provided for compatibility and testing purposes:"));
                using (ImRaii.PushIndent())
                {
                    allRequiredInstalled &= DrawCombatPlugin(ECombatModule.WrathCombo, checklistPadding);
                    allRequiredInstalled &=
                        DrawCombatPlugin(ECombatModule.RotationSolverReborn, checklistPadding);
                }
            }
        }

        if (QstWidgets.SectionHeader(_L("Recommended/niche plugins:"), "NichePlugins", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                foreach (PluginInfo plugin in _recommendedPlugins)
                    DrawPlugin(plugin, checklistPadding);
            }
        }
    }

    private void AddConfigClickable(IExposedPlugin? installedPlugin, PluginInfo plugin)
    {
        if (installedPlugin != null && plugin.ConfigCommand != null && plugin.ConfigCommand.StartsWith('/'))
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_L("Open Config"));
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (ImGui.IsItemClicked())
                _commandManager.ProcessCommand(plugin.ConfigCommand);

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

            ImGui.BeginGroup();
            if (installedPlugin != null && installedPlugin.InternalName.Equals("vnavmesh", StringComparison.Ordinal) && (installedPlugin.Manifest.Author.Contains("AtmoOmen")))
                plugin = new(
                    plugin.DisplayName,
                    plugin.InternalName,
                    plugin.Details,
                    new("https://github.com/AtmoOmen/ffxiv_navmesh-cn"),
                    new("https://gh.atmoomen.top/DalamudPlugins/main/pluginmaster.json"),
                    plugin.ConfigCommand
                );
            _uiUtils.ChecklistItem(label, isInstalled);

            DrawPluginDetails(plugin, checklistPadding, isInstalled);
            ImGui.EndGroup();
            AddConfigClickable(installedPlugin, plugin);
            return isInstalled;
        }
    }

    private bool DrawCombatPlugin(ECombatModule combatModule, float checklistPadding)
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
                Vector4 iconColor = isInstalled ? QstTheme.Success : QstTheme.Danger;
                FontAwesomeIcon icon = isInstalled ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;

                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(iconColor, icon.ToIconString());
            }
            AddConfigClickable(installedPlugin, plugin);

            DrawPluginDetails(plugin, checklistPadding, isInstalled, blurb: false);
            return isInstalled || _configuration.General.CombatModule != combatModule;
        }
    }

    private void DrawPluginDetails(PluginInfo plugin, float checklistPadding, bool isInstalled, bool blurb = true)
    {
        using (ImRaii.PushIndent(checklistPadding))
        {
            if (!string.IsNullOrEmpty(plugin.Details) && blurb)
                ImGui.TextWrapped(plugin.Details);

            bool allDetailsOk = true;
            if (plugin.DetailsToCheck != null)
            {
                foreach (PluginDetailInfo detail in plugin.DetailsToCheck)
                {
                    bool detailOk = detail.Predicate();
                    allDetailsOk &= detailOk;

                    _uiUtils.ChecklistItem(detail.DisplayName, isInstalled && detailOk);
                    if (!string.IsNullOrEmpty(detail.Details))
                    {
                        using (ImRaii.PushIndent(checklistPadding))
                        {
                            ImGui.TextWrapped(detail.Details);
                        }
                    }
                }
            }

            ImGui.Spacing();

            if (isInstalled)
            {
                if (!allDetailsOk && plugin.ConfigCommand != null && plugin.ConfigCommand.StartsWith('/'))
                {
                    ImRaii.ColorDisposable? color = null;
                    if (!allDetailsOk)
                        color = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
                    if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Cog))
                        _commandManager.ProcessCommand(plugin.ConfigCommand);
                    color?.Dispose();
                }
            }
            else
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Globe, _L("Open Website")))
                    Util.OpenLink(plugin.WebsiteUri.ToString());

                ImGui.SameLine();
                if (plugin.DalamudRepositoryUri != null)
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Code, _L("Open Repository")))
                        Util.OpenLink(plugin.DalamudRepositoryUri.ToString());
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    ImGuiComponents.HelpMarker(_L("Available on official Dalamud Repository"));
                }
            }
        }
    }

    private static bool PluginImageButton(PluginInfo plugin, float size, bool isInstalled, bool isActive)
    {
        string url = $"https://qstxiv.github.io/icons/{plugin.InternalName}.png";
        if (ThreadLoadImageHandler.TryGetTextureWrap(url, out IDalamudTextureWrap? logo))
        {
            return ImGui.ImageButton(
                logo.Handle,
                new(size.Scale(), size.Scale()),
                2,
                isInstalled ? QstTheme.Success : QstTheme.Danger,
                isActive ? Vector4.One : new(0.5f, 0.5f, 0.5f, 1f)
            );
        }

        return false;
    }

    private IExposedPlugin? FindInstalledPlugin(PluginInfo pluginInfo)
    {
        return _pluginInterface.InstalledPlugins.FirstOrDefault(x =>
            x.InternalName == pluginInfo.InternalName && x.IsLoaded);
    }

    private sealed record PluginInfo
    (
        string DisplayName,
        string InternalName,
        string Details,
        Uri WebsiteUri,
        Uri? DalamudRepositoryUri,
        string? ConfigCommand = null,
        List<PluginDetailInfo>? DetailsToCheck = null);

    private sealed record PluginDetailInfo(string DisplayName, string Details, Func<bool> Predicate);
}
