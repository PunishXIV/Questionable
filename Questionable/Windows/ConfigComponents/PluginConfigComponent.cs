using Dalamud.Interface.Utility;
using System.Collections.ObjectModel;
using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using ECommons.ImGuiMethods;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class PluginConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    CombatController combatController,
    ICommandManager commandManager,
    IChatGui chatGui,
    IFramework framework,
    ILogger<PluginConfigComponent> logger,
    AutomatonIpc automatonIpc,
    PandorasBoxIpc pandorasBoxIpc) : ConfigComponent(pluginInterface, configuration)
{
    private const string NightmareXivRepositoryUrl =
        "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";

    private static readonly string[] NightmareXivMirrors =
    [
        "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json"
    ];

    private const string PunishRepositoryUrl = "https://love.puni.sh/ment.json";
    private static readonly string[] PunishMirrors =
    [
        "https://puni.sh/api/plugins"
    ];
    private const string DalamudOfficialRepo = "https://kamori.goats.dev/Plugin/PluginMaster";

    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;
    private readonly Configuration _configuration = configuration;
    private readonly CombatController _combatController = combatController;
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
                    _L("Combat and boss mechanics"),
                    new("https://github.com/awgil/ffxiv_bossmod"),
                    new("https://puni.sh/api/repository/veyn"),
                    "/vbm")
            },
            {
                ECombatModule.WrathCombo,
                new("Wrath Combo",
                    "WrathCombo",
                    _L("Combat rotations (compatibility)"),
                    new("https://github.com/PunishXIV/WrathCombo"),
                    new(PunishRepositoryUrl),
                    "/wrath",
                    MirrorUrls: PunishMirrors)
            },
            {
                ECombatModule.RotationSolverReborn,
                new("Rotation Solver Reborn",
                    "RotationSolver",
                    _L("Combat rotations (compatibility)"),
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
            _L("Navigation mesh MappingTheRealm walks on"),
            new("https://github.com/awgil/ffxiv_navmesh/"),
            new("https://puni.sh/api/repository/veyn"),
            "/vnav"),
        new("MappingTheRealm",
            "MappingTheRealm",
            _L("Moving to quest objectives, in any zone"),
            new("https://github.com/Knightmore"),
            new("https://puni.sh/api/repository/knightmore")),
        new("TextAdvance",
            "TextAdvance",
            _L("Accepting and turning in quests, skipping dialogue"),
            new("https://github.com/NightmareXIV/TextAdvance"),
            new(NightmareXivRepositoryUrl),
            "/at c",
            MirrorUrls: NightmareXivMirrors),
    ];

    private readonly IReadOnlyList<PluginInfo> _recommendedPlugins =
    [
        new("Artisan",
            "Artisan",
            _L("Crafting quests"),
            new("https://github.com/PunishXIV/Artisan"),
            new(PunishRepositoryUrl),
            "/artisan",
            MirrorUrls: PunishMirrors),
        new("Gather & Chill",
            "GatherChill",
            _L("Gathering quests and the gathering journal"),
            new("https://github.com/LeontopodiumNivale14/Gather-Chill"),
            new("https://puni.sh/api/repository/ice"),
            "/gatherchill"),
        new("Knightshopper",
            "Knightshopper",
            _L("Buying quest items (allow Questionable in its settings)"),
            new("https://github.com/Knightmore"),
            new("https://puni.sh/api/repository/knightmore")),
        new("AutoDuty",
            "AutoDuty",
            _L("Dungeons and trials"),
            new("https://github.com/erdelf/AutoDuty"),
            new("https://puni.sh/api/repository/erdelf"),
            "/ad"),
        new("WahTools",
            "WahTools",
            _L("Fishing quests (Feesh)"),
            new("https://git.wahapp.com/wah/WahTools"),
            new("https://puni.sh/api/repository/wah"),
            "/feesh"),
        new("AutoRetainer",
            "AutoRetainer",
            _L("Questing on several characters in a row"),
            new("https://github.com/PunishXIV/AutoRetainer"),
            new(PunishRepositoryUrl),
            "/ays",
            MirrorUrls: PunishMirrors),
        new("Lifestream",
            "Lifestream",
            _L("Going home after a run, and switching characters"),
            new("https://github.com/NightmareXIV/Lifestream"),
            new(NightmareXivRepositoryUrl),
            "/lifestream",
            MirrorUrls: NightmareXivMirrors),
        new("Satisfy",
            "vsatisfy",
            _L("Custom deliveries"),
            new("https://github.com/awgil/ffxiv_satisfy"),
            new("https://puni.sh/api/repository/veyn"),
            "/vsatisfy"),
        new("Mogmail",
            "Mogmail",
            _L("Claiming Mog Station and reward mail"),
            new("https://github.com/Nexaii/Mogmail"),
            new("https://puni.sh/api/repository/nexai"),
            "/mogmail"),
        new("CBT (formerly known as Automaton)",
            "Automaton",
            _L("Stormblood sniping tasks"),
            new("https://github.com/Jaksuhn/ffxiv-bundleoftweaks"),
            new("https://puni.sh/api/repository/croizat"),
            "/cbt",
            [
                new(_L("'Sniper no sniping' enabled"),
                    _L("Automatically completes sniping tasks introduced in Stormblood"),
                    () => automatonIpc.IsAutoSnipeEnabled,
                    () => automatonIpc.SetAutoSnipeEnabled(true))
            ]),
        new("NotificationMaster",
            "NotificationMaster",
            _L("Notifications outside the game"),
            new("https://github.com/NightmareXIV/NotificationMaster"),
            null,
            "/pnotify"),
        new("Pandora's Box",
            "PandorasBox",
            _L("Active time maneuvers"),
            new("https://github.com/PunishXIV/PandorasBox"),
            new(PunishRepositoryUrl),
            "/pandora",
            [
                new(_L("'Auto Active Time Maneuver' enabled"),
                    _L("Automatically completes active time maneuvers in quest battles, trials and raids"),
                    () => pandorasBoxIpc.IsAutoActiveTimeManeuverEnabled,
                    () => pandorasBoxIpc.SetAutoActiveTimeManeuverEnabled(enabled: true))
            ],
            MirrorUrls: PunishMirrors),
        new("Stylist",
            "Stylist",
            _L("Equipping gear upgrades"),
            new("https://github.com/NightmareXIV/Stylist"),
            new(NightmareXivRepositoryUrl),
            "/stylist c",
            MirrorUrls: NightmareXivMirrors),
        new("QuestMap",
            "QuestMap",
            _L("'View in Quest Map' buttons (quest info and quest graph)"),
            new("https://github.com/GemPlugins/QuestMap"),
            null,
            "/quests"),
    ];

    public void DrawPage()
    {
        Draw(out bool allRequiredInstalled);

        ImGui.Spacing();
        if (allRequiredInstalled)
            ImGui.TextColored(QstTheme.Success, _L("All required plugins are installed."));
        else
            ImGui.TextColored(QstTheme.Danger, _L("Required plugins are missing, Questionable will not work properly."));
    }

    public void Draw(out bool allRequiredInstalled, bool combatPicker = false)
    {
        allRequiredInstalled = !HasMissingRequiredPlugins() && IsCombatPluginReady();
        if (combatPicker)
        {
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            DrawCombatPicker();
            ImGui.Separator();
        }

        ECombatModule combatModule = _configuration.General.CombatModule;
        List<PluginInfo> required = [.. RequiredPlugins];
        if (combatModule != ECombatModule.None)
            required.Add(CombatPlugins[combatModule]);
        List<PluginInfo> optional =
            [.. CombatPlugins.Where(x => x.Key != combatModule).Select(x => x.Value), .. _recommendedPlugins];

        // The setup window sizes itself to its content, so the table needs a width of its own there.
        DrawTable(required, optional, combatPicker ? 620 * ImGuiHelpers.GlobalScale : 0);
    }

    internal void DrawCombatPicker()
    {
        ECombatModule[] modules =
            [ECombatModule.None, ECombatModule.BossMod, ECombatModule.WrathCombo, ECombatModule.RotationSolverReborn];
        string[] labels =
            [.. modules.Select(x => x == ECombatModule.None ? _L("None (fight yourself)") : CombatPlugins[x].DisplayName)];
        using (ImRaii.Disabled(_combatController.IsRunning))
        {
            int index = Math.Max(0, Array.IndexOf(modules, _configuration.General.CombatModule));
            if (ImGui.Combo(_L("Combat plugin") + "###CombatPlugin", ref index, labels, labels.Length))
            {
                _configuration.General.CombatModule = modules[index];
                _pluginInterface.SavePluginConfig(_configuration);
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Questionable is built and tested with Boss Mod. Wrath Combo and Rotation Solver Reborn are there for compatibility and testing."));

        if (!IsCombatPluginReady())
            ImGui.TextColored(QstTheme.Danger, _LF("{0} isn't installed or loaded; install it on the Plugins page.",
                CombatPlugins[_configuration.General.CombatModule].DisplayName));
    }

    private void DrawTable(List<PluginInfo> required, List<PluginInfo> optional, float width)
    {
        using ImRaii.TableDisposable table = ImRaii.Table("Plugins", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp,
            new Vector2(width, 0));
        if (!table)
            return;

        ImGui.TableSetupColumn("##Status", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("##Plugin", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Actions", ImGuiTableColumnFlags.WidthFixed);

        DrawGroup(_L("Required"), required, installAll: true);
        DrawGroup(_L("Optional"), optional, installAll: false);
    }

    private void DrawGroup(string label, List<PluginInfo> plugins, bool installAll)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(QstTheme.Accent, label);
        ImGui.TableNextColumn();
        if (installAll && HasMissingRequiredPlugins())
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, _L("Install all required plugins")))
                InstallMissingPlugins();
            if (ImGui.IsItemHovered())
                QstWidgets.Tooltip(_L("Adds missing repositories and installs required plugins."));
        }

        foreach (PluginInfo plugin in plugins)
            DrawRow(plugin);
    }

    private void DrawRow(PluginInfo plugin)
    {
        using ImRaii.IdDisposable id = ImRaii.PushId("plugin_" + plugin.DisplayName);
        IExposedPlugin? installedPlugin = FindInstalledPlugin(plugin, requireLoaded: false);
        bool isLoaded = installedPlugin is { IsLoaded: true };
        if (installedPlugin != null && installedPlugin.InternalName.Equals("vnavmesh", StringComparison.Ordinal) &&
            installedPlugin.Manifest.Author.Contains("AtmoOmen"))
            plugin = new(
                plugin.DisplayName,
                plugin.InternalName,
                plugin.Details,
                new("https://github.com/AtmoOmen/ffxiv_navmesh-cn"),
                new("https://gh.atmoomen.top/DalamudPlugins/main/pluginmaster.json"),
                plugin.ConfigCommand
            );

        bool allDetailsOk = plugin.DetailsToCheck?.All(x => x.Predicate()) ?? true;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawStatus(isLoaded);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(installedPlugin != null
            ? $"{plugin.DisplayName} v{installedPlugin.Version}"
            : plugin.DisplayName);
        if (installedPlugin != null && !isLoaded)
            ImGui.TextColored(QstTheme.Amber, _L("Installed but not loaded"));
        else
            DrawNote(plugin.Details);

        ImGui.TableNextColumn();
        DrawActions(plugin, installedPlugin, isLoaded, allDetailsOk);

        if (plugin.DetailsToCheck == null)
            return;

        foreach (PluginDetailInfo detail in plugin.DetailsToCheck)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            using (ImRaii.PushIndent())
            {
                DrawStatus(isLoaded && detail.Predicate());
                ImGui.SameLine();
                ImGui.TextUnformatted(detail.DisplayName);
                DrawNote(detail.Details);
            }

            ImGui.TableNextColumn();
        }
    }

    private static void DrawNote(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.TextMuted))
            ImGui.TextWrapped(text);
    }

    private void DrawStatus(bool ok)
    {
        using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ok ? QstTheme.Success : QstTheme.Danger,
                (ok ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString());
        }
    }

    private void DrawActions(PluginInfo plugin, IExposedPlugin? installedPlugin, bool isLoaded, bool allDetailsOk)
    {
        if (isLoaded)
        {
            bool sameLine = false;
            if (!allDetailsOk && CanEnableOptions(plugin))
            {
                bool enabling = _enablingOptions.Contains(plugin.InternalName);
                using (ImRaii.Disabled(enabling))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.ToggleOn,
                            enabling ? _L("Enabling...") : _L("Enable recommended options")))
                        StartEnableOptions(plugin);
                }

                if (ImGui.IsItemHovered())
                    QstWidgets.Tooltip(_LF("Enable the recommended options for {0}.", plugin.DisplayName));
                sameLine = true;
            }

            bool hasCommand = plugin.ConfigCommand is { } command && command.StartsWith('/');
            if (hasCommand || installedPlugin!.HasConfigUi || installedPlugin.HasMainUi)
            {
                if (sameLine)
                    ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Cog, allDetailsOk ? null : QstTheme.Accent))
                {
                    if (hasCommand)
                        _commandManager.ProcessCommand(plugin.ConfigCommand!);
                    else if (installedPlugin!.HasConfigUi)
                        installedPlugin.OpenConfigUi();
                    else
                        installedPlugin!.OpenMainUi();
                }

                if (ImGui.IsItemHovered())
                    QstWidgets.Tooltip(_L("Open Config"));
            }
        }
        else if (installedPlugin != null)
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plug, _L("Open installer")))
                _pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, plugin.DisplayName);
        }
        else
            DrawInstallButtons(plugin);
    }

    public void InstallMissingPlugins()
        => _ = InstallMissingAsync();

    private async Task InstallMissingAsync()
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

    private bool IsCombatPluginReady()
    {
        ECombatModule combatModule = _configuration.General.CombatModule;
        return combatModule == ECombatModule.None
               || FindInstalledPlugin(CombatPlugins[combatModule], requireLoaded: true) != null;
    }

    private void DrawInstallButtons(PluginInfo plugin)
    {
        bool installing = _installingPlugins.Contains(plugin.InternalName);
        using (ImRaii.Disabled(installing))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download,
                    installing ? _L("Installing...") : _L("Install plugin")))
                _ = StartInstallAsync(plugin);
        }

        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_LF("Install {0} from its plugin repository.", plugin.DisplayName));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Globe, _L("Open Website")))
            Util.OpenLink(plugin.WebsiteUri.ToString());
    }

    // Match URLs without query strings so authenticated puni.sh repositories are reused.
    private string? FindConfiguredRepository(PluginInfo plugin)
    {
        List<string> configured = GetRepositoryUrls();
        return plugin.RepositoryUrls.Select(url => FindMatchingRepository(configured, url)).FirstOrDefault(x => x != null);
    }

    internal static string? FindMatchingRepository(IEnumerable<string> configuredUrls, string repoUrl)
        => configuredUrls.FirstOrDefault(configured => IsSameRepository(configured, repoUrl));

    internal static bool IsSameRepository(string a, string b)
        => string.Equals(WithoutQuery(a), WithoutQuery(b), StringComparison.OrdinalIgnoreCase);

    private static string WithoutQuery(string url)
    {
        int query = url.IndexOf('?', StringComparison.Ordinal);
        return (query >= 0 ? url[..query] : url).Trim().TrimEnd('/');
    }

    private List<string> GetRepositoryUrls()
    {
        try
        {
            object config = DalamudReflector.GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
            if (config.GetFoP("ThirdRepoList") is not System.Collections.IEnumerable repos)
                return [];

            return repos.Cast<object>().Select(repo => repo.GetFoP("Url") as string).OfType<string>().ToList();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Could not read the configured plugin repositories");
            return [];
        }
    }

    private async Task StartInstallAsync(PluginInfo plugin)
    {

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

        string repoUrl = FindConfiguredRepository(plugin)
                         ?? plugin.DalamudRepositoryUri?.ToString()
                         ?? DalamudOfficialRepo;
        try
        {
            if (await InstallFromRepositoryAsync(repoUrl, plugin.InternalName).ConfigureAwait(false))
            {
                _chatGui.Print(_LF("Installed {0}.", plugin.DisplayName), CommandHandler.MessageTag,
                    CommandHandler.TagColor);
                await EnableOptionsAsync(plugin).ConfigureAwait(false);
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

            if (TryRemoveLeftoverFiles(plugin.InternalName, out string? leftoverPath)
                && await InstallFromRepositoryAsync(repoUrl, plugin.InternalName).ConfigureAwait(false))
            {
                _chatGui.Print(_LF("Removed leftover {0} files and installed the plugin.", plugin.DisplayName),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                await EnableOptionsAsync(plugin).ConfigureAwait(false);
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

    // ECommons' AddPlugin reflection throws "Parameter count mismatch" on Dalamud's optional parameter; bind by name.
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

        if (!DalamudReflector.HasRepo(repoUrl) && !repoUrl.Equals(DalamudOfficialRepo, StringComparison.Ordinal))
            DalamudReflector.AddRepo(repoUrl, enabled: true);

        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();

        if (!IsPluginPresent(internalName))
            TryRemoveLeftoverFiles(internalName, out _);

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
        => FindInstalledPlugin(_pluginInterface.InstalledPlugins, pluginInfo.InternalName, requireLoaded);

    internal static IExposedPlugin? FindInstalledPlugin(IEnumerable<IExposedPlugin> installedPlugins, string internalName,
        bool requireLoaded = true)
    {
        IExposedPlugin? installedPlugin = null;
        foreach (IExposedPlugin plugin in installedPlugins)
        {
            if (!string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (plugin.IsLoaded)
                return plugin;

            installedPlugin ??= plugin;
        }

        return requireLoaded ? null : installedPlugin;
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

    private bool TryRemoveLeftoverFiles(string internalName, out string? pluginDir)
    {
        pluginDir = GetPluginDirectory(internalName);
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

    private string? GetPluginDirectory(string internalName)
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
        string[]? MirrorUrls = null)
    {
        public IReadOnlyList<string> RepositoryUrls
        {
            get
            {
                if (DalamudRepositoryUri == null)
                    return [];

                if (MirrorUrls is { Length: > 0 })
                    return [DalamudRepositoryUri.ToString(), .. MirrorUrls];

                return [DalamudRepositoryUri.ToString()];
            }
        }
    }

    private static bool CanEnableOptions(PluginInfo plugin)
        => plugin.DetailsToCheck?.Any(detail => detail.Enable != null) == true;

    private void StartEnableOptions(PluginInfo plugin)
    {
        if (!CanEnableOptions(plugin) || _enablingOptions.Contains(plugin.InternalName))
            return;

        _ = EnableOptionsAsync(plugin);
    }

    private async Task EnableOptionsAsync(PluginInfo plugin)
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
