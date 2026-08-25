using System.Collections.ObjectModel;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    IChatGui chatGui,
    IFramework framework,
    ILogger<PluginConfigComponent> logger,
    AutomatonIpc automatonIpc,
    PandorasBoxIpc pandorasBoxIpc) : ConfigComponent(pluginInterface, configuration)
{
    private const string NightmareXivRepositoryUrl =
        "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";

    private static readonly string[] NightmareXivRepositoryAlternates =
    [
        "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json"
    ];

    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;
    private readonly Configuration _configuration = configuration;
    private readonly CombatController _combatController = combatController;
    private readonly UiUtils _uiUtils = uiUtils;
    private readonly ICommandManager _commandManager = commandManager;
    private readonly IChatGui _chatGui = chatGui;
    private readonly IFramework _framework = framework;
    private readonly ILogger<PluginConfigComponent> _logger = logger;
    private readonly HashSet<string> _installingPlugins = [];
    private readonly HashSet<string> _enablingOptions = [];

    private static readonly ReadOnlyDictionary<ECombatModule, PluginInfo> CombatPlugins =
        new Dictionary<ECombatModule, PluginInfo>
        {
            {
                ECombatModule.BossMod,
                new("Boss Mod",
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
            new(NightmareXivRepositoryUrl),
            "/lifestream",
            AlternateRepositoryUrls: NightmareXivRepositoryAlternates),
        new("TextAdvance",
            "TextAdvance",
            _L("Automatically accepts and turns in quests, skips cutscenes and dialogue."),
            new("https://github.com/NightmareXIV/TextAdvance"),
            new(NightmareXivRepositoryUrl),
            "/at c",
            AlternateRepositoryUrls: NightmareXivRepositoryAlternates),
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
                    () => automatonIpc.IsAutoSnipeEnabled,
                    () => automatonIpc.SetAutoSnipeEnabled(true))
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
            new(NightmareXivRepositoryUrl),
            "/pnotify",
            AlternateRepositoryUrls: NightmareXivRepositoryAlternates),
        new("Pandora's Box",
            "PandorasBox",
            _L("Pandora's Box is a collection of tweaks."),
            new("https://github.com/PunishXIV/PandorasBox"),
            new("https://puni.sh/api/plugins"),
            "/pandora",
            [
                new(_L("'Auto Active Time Maneuver' enabled"),
                    _L("Automatically completes active time maneuvers in single player instances, trials and raids"),
                    () => pandorasBoxIpc.IsAutoActiveTimeManeuverEnabled,
                    () => pandorasBoxIpc.SetAutoActiveTimeManeuverEnabled(true))
            ]),
        new("Stylist",
            "Stylist",
            _L("Gear manager"),
            new("https://github.com/NightmareXIV/Stylist"),
            new(NightmareXivRepositoryUrl),
            "/stylist c",
            AlternateRepositoryUrls: NightmareXivRepositoryAlternates),
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
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, _L("Install all required plugins")))
                InstallMissingRequiredPlugins();
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

        allRequiredInstalled = !HasMissingRequiredPlugins() && IsSelectedCombatPluginReady();
        ImGui.SetNextItemOpen(isOpen: true, ImGuiCond.Once);
        if (QstWidgets.SectionHeader(_L("Required plugins:"), "RequiredPlugins", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                DrawInstallAllRequiredButton();
                foreach (PluginInfo plugin in RequiredPlugins)
                    DrawPlugin(plugin, checklistPadding);
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

                    DrawCombatPlugin(ECombatModule.BossMod, checklistPadding);
                }

                ImGui.TextWrapped(_L("The following rotation/combat plugin(s) are provided for compatibility and testing purposes:"));
                using (ImRaii.PushIndent())
                {
                    DrawCombatPlugin(ECombatModule.WrathCombo, checklistPadding);
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
            IExposedPlugin? installedPlugin = FindInstalledPlugin(plugin, requireLoaded: false);
            bool isLoaded = installedPlugin is { IsLoaded: true };
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
            _uiUtils.ChecklistItem(label, isLoaded);

            DrawPluginDetails(plugin, checklistPadding, installedPlugin);
            ImGui.EndGroup();
            AddConfigClickable(installedPlugin, plugin);
            return isLoaded;
        }
    }

    private bool DrawCombatPlugin(ECombatModule combatModule, float checklistPadding)
    {
        ImGui.Spacing();

        PluginInfo plugin = CombatPlugins[combatModule];
        using (ImRaii.PushId("plugin_" + plugin.DisplayName))
        {
            IExposedPlugin? installedPlugin = FindInstalledPlugin(plugin, requireLoaded: false);
            bool isLoaded = installedPlugin is { IsLoaded: true };
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
                Vector4 iconColor = isLoaded ? QstTheme.Success : QstTheme.Danger;
                FontAwesomeIcon icon = isLoaded ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;

                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(iconColor, icon.ToIconString());
            }
            AddConfigClickable(installedPlugin, plugin);

            DrawPluginDetails(plugin, checklistPadding, installedPlugin, blurb: false);
            return isLoaded || _configuration.General.CombatModule != combatModule;
        }
    }

    private void DrawPluginDetails(PluginInfo plugin, float checklistPadding, IExposedPlugin? installedPlugin,
        bool blurb = true)
    {
        bool isLoaded = installedPlugin is { IsLoaded: true };
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

                    _uiUtils.ChecklistItem(detail.DisplayName, isLoaded && detailOk);
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

            if (isLoaded)
            {
                bool canEnableRecommended = !allDetailsOk && HasEnableableRecommendedOptions(plugin);
                if (canEnableRecommended)
                {
                    bool enabling = _enablingOptions.Contains(plugin.InternalName);
                    using (ImRaii.Disabled(enabling))
                    {
                        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.ToggleOn,
                                enabling ? _L("Enabling...") : _L("Enable recommended options")))
                            StartEnableRecommendedOptions(plugin);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_LF("Enable the recommended options for {0}.", plugin.DisplayName));
                }

                if (!allDetailsOk && plugin.ConfigCommand != null && plugin.ConfigCommand.StartsWith('/'))
                {
                    if (canEnableRecommended)
                        ImGui.SameLine();

                    ImRaii.ColorDisposable? color = null;
                    if (!allDetailsOk)
                        color = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
                    if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Cog))
                        _commandManager.ProcessCommand(plugin.ConfigCommand);
                    color?.Dispose();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Open Config"));
                }
            }
            else if (installedPlugin != null)
            {
                ImGui.TextColored(QstTheme.Amber, _L("Installed but not loaded"));
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plug, _L("Open installer")))
                    _pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, plugin.DisplayName);
            }
            else
            {
                DrawInstallButtons(plugin);
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

    public void InstallMissingRequiredPlugins()
        => _ = InstallMissingRequiredPluginsAsync();

    private async Task InstallMissingRequiredPluginsAsync()
    {
        foreach (PluginInfo plugin in RequiredPlugins)
            await TryInstallIfMissingAsync(plugin).ConfigureAwait(false);

        ECombatModule combatModule = _configuration.General.CombatModule;
        if (combatModule != ECombatModule.None)
            await TryInstallIfMissingAsync(CombatPlugins[combatModule]).ConfigureAwait(false);
    }

    private async Task TryInstallIfMissingAsync(PluginInfo plugin)
    {
        IExposedPlugin? installed = FindInstalledPlugin(plugin, requireLoaded: false);
        if (installed is { IsLoaded: true })
            return;

        if (installed != null || IsPluginPresent(plugin.InternalName))
        {
            _pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, plugin.DisplayName);
            return;
        }

        await StartInstallAsync(plugin).ConfigureAwait(false);
    }

    private bool HasMissingRequiredPlugins()
        => RequiredPlugins.Any(plugin => FindInstalledPlugin(plugin, requireLoaded: true) == null);

    private bool IsSelectedCombatPluginReady()
    {
        ECombatModule combatModule = _configuration.General.CombatModule;
        return combatModule == ECombatModule.None
               || FindInstalledPlugin(CombatPlugins[combatModule], requireLoaded: true) != null;
    }

    private void DrawInstallAllRequiredButton()
    {
        if (!HasMissingRequiredPlugins())
            return;

        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, _L("Install all required plugins")))
            InstallMissingRequiredPlugins();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Adds missing repositories and installs required plugins."));

        ImGui.Spacing();
    }

    private void DrawInstallButtons(PluginInfo plugin)
    {
        bool installing = _installingPlugins.Contains(plugin.InternalName);
        bool repoAdded = IsRepositoryAdded(plugin);
        bool firstButton = true;

        if (!repoAdded && plugin.DalamudRepositoryUri != null)
        {
            using (ImRaii.Disabled(installing))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Add repository")))
                    TryAddRepository(plugin);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_LF("Add {0} to Custom Plugin Repositories.", plugin.DalamudRepositoryUri));

            firstButton = false;
        }

        if (!firstButton)
            ImGui.SameLine();

        using (ImRaii.Disabled(installing))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download,
                    installing ? _L("Installing...") : _L("Install plugin")))
                _ = StartInstallAsync(plugin);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_LF("Install {0} from its plugin repository.", plugin.DisplayName));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Globe, _L("Open Website")))
            Util.OpenLink(plugin.WebsiteUri.ToString());
    }

    private static bool IsRepositoryAdded(PluginInfo plugin)
        => plugin.RepositoryUrls.Any(DalamudReflector.HasRepo);

    private void TryAddRepository(PluginInfo plugin)
    {
        if (plugin.DalamudRepositoryUri == null)
            return;

        string repoUrl = plugin.DalamudRepositoryUri.ToString();
        if (DalamudReflector.HasRepo(repoUrl))
        {
            _chatGui.Print(_LF("{0} repository is already added.", plugin.DisplayName), CommandHandler.MessageTag,
                CommandHandler.TagColor);
            return;
        }

        DalamudReflector.AddRepo(repoUrl, enabled: true);
        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();
        _chatGui.Print(_LF("Added {0} repository.", plugin.DisplayName), CommandHandler.MessageTag,
            CommandHandler.TagColor);
    }

    private async Task StartInstallAsync(PluginInfo plugin)
    {
        if (plugin.DalamudRepositoryUri == null)
        {
            _pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, plugin.DisplayName);
            return;
        }

        if (IsPluginPresent(plugin.InternalName))
        {
            _chatGui.Print(_LF("{0} is already installed.", plugin.DisplayName), CommandHandler.MessageTag,
                CommandHandler.TagColor);
            if (FindInstalledPlugin(plugin, requireLoaded: true) == null)
                _pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, plugin.DisplayName);
            return;
        }

        if (!_installingPlugins.Add(plugin.InternalName))
            return;

        string repoUrl = plugin.RepositoryUrls.FirstOrDefault(DalamudReflector.HasRepo)
                         ?? plugin.DalamudRepositoryUri.ToString();
        try
        {
            if (await InstallFromRepositoryAsync(repoUrl, plugin.InternalName).ConfigureAwait(false))
            {
                _chatGui.Print(_LF("Installed {0}.", plugin.DisplayName), CommandHandler.MessageTag,
                    CommandHandler.TagColor);
                await EnableRecommendedOptionsAsync(plugin).ConfigureAwait(false);
            }
            else if (IsPluginPresent(plugin.InternalName))
            {
                _chatGui.Print(_LF("{0} is already installed.", plugin.DisplayName), CommandHandler.MessageTag,
                    CommandHandler.TagColor);
            }
            else
            {
                _chatGui.PrintError(
                    _LF("Could not install {0}. Check the plugin installer for details.", plugin.DisplayName),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
        }
        catch (Exception e) when (IsInstallConflict(e))
        {
            if (IsPluginPresent(plugin.InternalName))
            {
                _chatGui.Print(_LF("{0} is already installed.", plugin.DisplayName), CommandHandler.MessageTag,
                    CommandHandler.TagColor);
                return;
            }

            if (TryRemoveOrphanedPluginFiles(plugin.InternalName, out string? leftoverPath)
                && await InstallFromRepositoryAsync(repoUrl, plugin.InternalName).ConfigureAwait(false))
            {
                _chatGui.Print(_LF("Removed leftover {0} files and installed the plugin.", plugin.DisplayName),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                await EnableRecommendedOptionsAsync(plugin).ConfigureAwait(false);
                return;
            }

            _chatGui.PrintError(
                _LF("Could not install {0}: leftover files at '{1}' are in use. Close the game or delete that folder, then try again.",
                    plugin.DisplayName, leftoverPath ?? plugin.InternalName),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _logger.LogWarning(e, "{Plugin} leftover files at {Path} blocked install", plugin.InternalName,
                leftoverPath);
        }
        catch (Exception e)
        {
            if (IsPluginPresent(plugin.InternalName))
            {
                _chatGui.Print(_LF("{0} is already installed.", plugin.DisplayName), CommandHandler.MessageTag,
                    CommandHandler.TagColor);
                return;
            }

            _chatGui.PrintError(
                _LF("Could not install {0}. Check the plugin installer for details.", plugin.DisplayName),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _logger.LogError(e, "Failed to install {Plugin}", plugin.InternalName);
        }
        finally
        {
            _installingPlugins.Remove(plugin.InternalName);
        }
    }

    private static bool IsInstallConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is IOException or UnauthorizedAccessException)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     ECommons <c>AddPlugin</c> hard-codes <c>InstallPluginAsync</c> arguments. Dalamud added an
    ///     optional <c>inheritedWorkingPluginId</c> parameter; reflection still requires it, which
    ///     throws "Parameter count mismatch". Bind the live method's parameters by name instead.
    /// </summary>
    private async Task<bool> InstallFromRepositoryAsync(string repoUrl, string internalName)
    {
        var plugins = await DalamudReflector.GetPluginMaster(repoUrl).ConfigureAwait(false);
        if (plugins == null || plugins.Count == 0)
        {
            _logger.LogError("No plugin manifests returned from {RepoUrl}", repoUrl);
            return false;
        }

        object? manifest = plugins.FirstOrDefault(candidate =>
            string.Equals((string?)candidate.GetFoP("InternalName"), internalName, StringComparison.Ordinal));
        if (manifest == null)
        {
            _logger.LogError("Plugin {InternalName} was not found in {RepoUrl}", internalName, repoUrl);
            return false;
        }

        if (!DalamudReflector.HasRepo(repoUrl))
            DalamudReflector.AddRepo(repoUrl, enabled: true);

        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();

        if (!IsPluginPresent(internalName))
            TryRemoveOrphanedPluginFiles(internalName, out _);

        object pluginManager = DalamudReflector.GetPluginManager();
        MethodInfo? installMethod = pluginManager.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "InstallPluginAsync")
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault();
        if (installMethod == null)
        {
            _logger.LogError("PluginManager.InstallPluginAsync was not found");
            return false;
        }

        object?[] arguments = BindInstallArguments(installMethod, manifest);
        if (installMethod.Invoke(pluginManager, arguments) is not Task installTask)
        {
            _logger.LogError("InstallPluginAsync did not return a Task");
            return false;
        }

        await installTask.ConfigureAwait(false);

        object? localPlugin = installTask.GetFoP("Result");
        return localPlugin != null && (bool)localPlugin.GetFoP("IsLoaded")!;
    }

    private static object?[] BindInstallArguments(MethodInfo installMethod, object manifest)
    {
        ParameterInfo[] parameters = installMethod.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            arguments[i] = parameters[i].Name switch
            {
                "repoManifest" => manifest,
                "useTesting" => false,
                "reason" => PluginLoadReason.Installer,
                _ => parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null
            };
        }

        return arguments;
    }

    private IExposedPlugin? FindInstalledPlugin(PluginInfo pluginInfo, bool requireLoaded = true)
    {
        return _pluginInterface.InstalledPlugins.FirstOrDefault(x =>
            string.Equals(x.InternalName, pluginInfo.InternalName, StringComparison.OrdinalIgnoreCase)
            && (!requireLoaded || x.IsLoaded));
    }

    private bool IsPluginPresent(string internalName)
    {
        if (_pluginInterface.InstalledPlugins.Any(plugin =>
                string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase)))
            return true;

        try
        {
            object pluginManager = DalamudReflector.GetPluginManager();
            if (pluginManager.GetType().GetProperty("InstalledPlugins")?.GetValue(pluginManager)
                is not System.Collections.IEnumerable installed)
                return false;

            foreach (object plugin in installed)
            {
                string? name = plugin.GetType().GetProperty("InternalName")?.GetValue(plugin) as string;
                if (string.Equals(name, internalName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Could not query PluginManager.InstalledPlugins for {Plugin}", internalName);
        }

        return false;
    }

    private bool TryRemoveOrphanedPluginFiles(string internalName, out string? pluginDir)
    {
        pluginDir = GetInstalledPluginDirectory(internalName);
        if (pluginDir == null || !Directory.Exists(pluginDir))
            return true;

        if (IsPluginPresent(internalName))
            return true;

        try
        {
            ClearReadOnlyAttributes(pluginDir);
            Directory.Delete(pluginDir, recursive: true);
            _logger.LogInformation("Removed leftover {Plugin} files at {Path}", internalName, pluginDir);
            return !Directory.Exists(pluginDir);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not remove leftover {Plugin} files at {Path}", internalName, pluginDir);
            return false;
        }
    }

    private string? GetInstalledPluginDirectory(string internalName)
    {
        try
        {
            object pluginManager = DalamudReflector.GetPluginManager();
            object? directory = pluginManager.GetFoP("pluginDirectory");
            string? root = directory switch
            {
                DirectoryInfo info => info.FullName,
                string path => path,
                _ => directory?.GetFoP("FullName") as string
            };
            if (string.IsNullOrEmpty(root))
                return null;

            return Path.Combine(root, internalName);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Could not resolve installed plugin directory for {Plugin}", internalName);
            return null;
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            return;

        directory.Attributes &= ~FileAttributes.ReadOnly;
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            entry.Attributes &= ~FileAttributes.ReadOnly;
    }

    private sealed record PluginInfo
    (
        string DisplayName,
        string InternalName,
        string Details,
        Uri WebsiteUri,
        Uri? DalamudRepositoryUri,
        string? ConfigCommand = null,
        List<PluginDetailInfo>? DetailsToCheck = null,
        string[]? AlternateRepositoryUrls = null)
    {
        public IReadOnlyList<string> RepositoryUrls
        {
            get
            {
                if (DalamudRepositoryUri == null)
                    return [];

                if (AlternateRepositoryUrls is { Length: > 0 })
                    return [DalamudRepositoryUri.ToString(), .. AlternateRepositoryUrls];

                return [DalamudRepositoryUri.ToString()];
            }
        }
    }

    private static bool HasEnableableRecommendedOptions(PluginInfo plugin)
        => plugin.DetailsToCheck?.Any(detail => detail.Enable != null) == true;

    private void StartEnableRecommendedOptions(PluginInfo plugin)
    {
        if (!HasEnableableRecommendedOptions(plugin) || _enablingOptions.Contains(plugin.InternalName))
            return;

        _ = EnableRecommendedOptionsAsync(plugin);
    }

    private async Task EnableRecommendedOptionsAsync(PluginInfo plugin)
    {
        if (!_enablingOptions.Add(plugin.InternalName))
            return;

        try
        {
            if (plugin.DetailsToCheck is not { Count: > 0 } details
                || details.All(detail => detail.Enable == null))
                return;

            const int maxAttempts = 20;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                bool allEnabled = await _framework.RunOnTick(() =>
                {
                    bool ok = true;
                    foreach (PluginDetailInfo detail in details)
                    {
                        if (detail.Predicate())
                            continue;

                        ok &= detail.Enable?.Invoke() == true;
                    }

                    return ok;
                }).ConfigureAwait(false);

                if (allEnabled)
                {
                    _chatGui.Print(_LF("Enabled recommended options on {0}.", plugin.DisplayName),
                        CommandHandler.MessageTag, CommandHandler.TagColor);
                    return;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            _logger.LogWarning("Could not enable recommended options for {Plugin}", plugin.InternalName);
            _chatGui.PrintError(
                _LF("Could not enable recommended options on {0}. Open the plugin config to enable them.",
                    plugin.DisplayName),
                CommandHandler.MessageTag, CommandHandler.TagColor);
        }
        finally
        {
            _enablingOptions.Remove(plugin.InternalName);
        }
    }

    private sealed record PluginDetailInfo(
        string DisplayName,
        string Details,
        Func<bool> Predicate,
        Func<bool>? Enable = null);
}
