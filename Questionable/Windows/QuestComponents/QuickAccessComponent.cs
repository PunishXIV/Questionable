using System.Text.Json;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Questionable.Controller.Steps.Shared;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class QuickAccessComponent
(
    QuestController questController,
    QuestStarter questStarter,
    MovementController movementController,
    QuestFunctions questFunctions,
    QuestRegistry questRegistry,
    QuestValidationWindow questValidationWindow,
    JournalProgressWindow journalProgressWindow,
    QueueComponent queueComponent,
    OpenQuestCleanUp openQuestCleanUp,
    BossModIpc bossModIpc,
    SatisfyIpc satisfy,
    ICommandManager commandManager,
    IObjectTable objectTable,
    IClientState clientState,
    ICondition condition,
    ITargetManager targetManager,
    RecentLog recentLog,
    IChatGui chatGui,
    IDalamudPluginInterface pluginInterface)
{
    private static readonly HashSet<string> ReportedPlugins = new(StringComparer.Ordinal)
    {
        "Artisan", "AutoDuty", "AutoRetainer", "Automaton", "BossMod", "BossModReborn", "GatherChill", "Knightshopper", "Lifestream",
        "MappingTheRealm", "Mogmail", "PandorasBox", "RotationSolver", "SelectString", "Stylist", "TextAdvance", "vnavmesh", "vsatisfy", "WahTools",
        "WrathCombo", "YesAlready",
    };

    public event EventHandler? Reload;

    public void Draw()
    {
        DrawPriorityQuestsButton();
        ImGui.SameLine();
        DrawJournalProgressButton();
        ImGui.SameLine();
        openQuestCleanUp.DrawRailButton();
        ImGui.SameLine();
        DrawRedeemButton();
        ImGui.SameLine();
        DrawDeliveriesButton();
        ImGui.SameLine();
        DrawReloadDataButton();
        ImGui.SameLine();
        DrawRebuildNavmeshButton();
        ImGui.SameLine();
        DrawClearObstaclesButton();

        if (pluginInterface.IsDev)
        {
            ImGui.SameLine();
            DrawValidationButton();
        }
    }

    internal void DrawPriorityQuestsButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.AngleDoubleUp,
                _L("Priority Quests"),
                _L("Quests done as soon as possible."),
                enabled: objectTable[0] != null,
                showLabel: showLabel))
            queueComponent.Open();
    }

    internal void DrawRedeemButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.Gift,
                _L("Redeem reward items"),
                _L("Opens reward items in your inventory now (mounts, minions, orchestrion rolls...), except ones set to never open. Same as /qst redeem."),
                enabled: objectTable[0] != null && !questController.IsRunning,
                showLabel: showLabel))
            questStarter.RedeemRewards();
    }

    private void DrawDeliveriesButton()
    {
        string tooltip = satisfy.IsInstalled
            ? _L("Has Satisfy do this week's custom deliveries for the NPCs ticked on the Custom Deliveries page in Journal Progress. Same as /qst deliveries.")
            : _L("Satisfy (vsatisfy) is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.Truck, _L("Custom Deliveries"), tooltip,
                enabled: objectTable[0] != null && !questController.IsRunning && satisfy.IsInstalled))
            questStarter.RunCustomDeliveries();
    }

    internal void DrawRebuildNavmeshButton(bool showLabel = false)
    {
        bool isNavmeshAvailable = commandManager.Commands.ContainsKey("/vnav");
        string tooltip = isNavmeshAvailable
            ? _L("Hold CTRL to enable this button. Rebuilding the navmesh will take some time.")
            : _L("vnavmesh is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.Route, _L("Rebuild navmesh"), tooltip,
                enabled: isNavmeshAvailable && ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                showLabel: showLabel))
            commandManager.ProcessCommand("/vnav rebuild");
    }

    internal void DrawClearObstaclesButton(bool showLabel = false)
    {
        bool isBossModAvailable = commandManager.Commands.ContainsKey("/vbm");
        string tooltip = isBossModAvailable
            ? _L("Clear Boss Mod obstacle maps to fix pathfinding issues. Hold CTRL to enable this button.")
            : _L("Boss Mod is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.RoadBarrier, _L("Clear Boss Mod obstacle maps"), tooltip,
                enabled: isBossModAvailable && ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                showLabel: showLabel))
            bossModIpc.ClearObstacleMaps();
    }

    internal void DrawReloadDataButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.RedoAlt, _L("Reload data"),
                _L("Reset sequence progress and reload quest data from disk"),
                showLabel: showLabel))
            Reload?.Invoke(this, EventArgs.Empty);
    }

    internal void DrawJournalProgressButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.BookBookmark, _L("Journal Progress"),
                _L("Quests, unlocks, allied societies and custom deliveries"),
                showLabel: showLabel))
            journalProgressWindow.ToggleOrUncollapse();
    }

    internal void DrawTroubleshootingButton(bool showLabel = false)
    {
        bool leftClicked = QstWidgets.RailButton(FontAwesomeIcon.LifeRing,
            _L("Stuck?"),
            _L("Left click: Copy troubleshooting information to clipboard\nRight click: Copy list of completed quests to clipboard"),
            tint: QstTheme.SpecialButton,
            enabled: objectTable[0] != null,
            showLabel: showLabel);
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (rightClicked)
        {
            ImGui.SetClipboardText(JsonSerializer.Serialize(LogQuestCompletion.ReadLog(), JsonOptions.Default));
            chatGui.Print(_L("List of completed quests copied to the clipboard. Paste it in the Discord thread linked below, then run " +
                        "'/qst clearlog' to reset the log.") + "\nhttps://discord.com/channels/1001823907193552978/1447612869431656508/1447612869431656508",
                CommandHandler.MessageTag, CommandHandler.TagColor);
        }
        else if (leftClicked)
        {
            ImGui.SetClipboardText(BuildReport());
            chatGui.Print(_L("Troubleshooting information has been copied to clipboard. " +
                "Please create a new thread in #questionable-issues in https://discord.gg/punishxiv describing the problem and pasting this troubleshooting information."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
        }
    }

    private string BuildReport()
    {
        Configuration config = Configuration.Instance();
        IPlayerCharacter player = (IPlayerCharacter)objectTable[0]!;
        QuestController.QuestProgress? progress = questController.CurrentQuest;
        QuestStep? step = progress?.FindStep();

        List<string?> facts =
        [
            $"{CommandHandler.MessageTag}{(pluginInterface.IsDev ? " dev" : "")} | paths {config.PathData.InstalledDataVersion} | client {clientState.ClientLanguage} | ui {config.General.Language} | profile: {(config.ActiveProfileName != null ? "yes" : "no")}",
            progress == null
                ? "Quest: none"
                : $"Quest: {progress.Quest.Id} {progress.Quest.Info.Name} | seq {progress.Sequence} step {progress.Step} | source: {progress.Quest.Source}",
            progress == null ? null : step == null
                ? "Step: no path data for this step"
                : $"Step: {step.InteractionType}, target {step.DataId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, territory {TerritoryData.GetNameAndId(step.TerritoryId)}",
            progress == null ? null : $"Game quest state: {questFunctions.GetQuestProgressInfo(progress.Quest.Id)?.ToString() ?? "not accepted"}",
            $"State: {questController.AutomationType}, {(questController.IsRunning ? "running" : "idle")} | task: {questController.ToStatString()} | status: {questController.DebugState ?? "-"} | executor: {questController.CurrentTaskState ?? "-"}{(questController.HasExternalOperation ? " | plugin operation active" : "")}",
            $"Character: {player.ClassJob.Value.Abbreviation} {player.Level} | {TerritoryData.GetNameAndId(clientState.TerritoryType)} | {player.Position.ToInternalString()} | target {targetManager.Target?.BaseId.ToString(CultureInfo.InvariantCulture) ?? "none"} | flags: {DescribeConditions()}",
            DescribePlugins(),
            TroubleshootingReport.DescribeFailure(movementController.LastFailure, DateTimeOffset.UtcNow),
        ];

        return TroubleshootingReport.Build(new()
        {
            Facts = facts.OfType<string>().ToList(),
            Settings = TroubleshootingReport.ChangedSettings(config),
            Log = recentLog.GetEntries(),
            PrivateTexts = [player.Name.TextValue, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName],
            NowUtc = DateTime.UtcNow,
        });
    }

    private string DescribeConditions()
    {
        (ConditionFlag Flag, string Name)[] flags =
        [
            (ConditionFlag.Mounted, "mounted"),
            (ConditionFlag.InFlight, "flying"),
            (ConditionFlag.InCombat, "combat"),
            (ConditionFlag.BoundByDuty, "duty"),
            (ConditionFlag.BetweenAreas, "loading"),
            (ConditionFlag.Casting, "casting"),
            (ConditionFlag.OccupiedInQuestEvent, "quest event"),
            (ConditionFlag.OccupiedInEvent, "event"),
            (ConditionFlag.WatchingCutscene, "cutscene"),
        ];
        string active = string.Join(", ", flags.Where(x => condition[x.Flag]).Select(x => x.Name));
        return active.Length == 0 ? "none" : active;
    }

    private string DescribePlugins()
    {
        List<IExposedPlugin> loaded = pluginInterface.InstalledPlugins.Where(x => x.IsLoaded).ToList();
        IEnumerable<string> related = loaded
            .Where(x => ReportedPlugins.Contains(x.InternalName))
            .OrderBy(x => x.InternalName, StringComparer.Ordinal)
            .Select(x => $"{x.InternalName} {x.Version}");
        return $"Plugins: {string.Join(", ", related)} ({loaded.Count} loaded)";
    }

    internal void DrawValidationButton(bool showLabel = false)
    {
        bool running = questRegistry.ValidationRunning;
        int errorCount = questRegistry.ValidationErrorCount;
        int infoCount = questRegistry.ValidationIssueCount - questRegistry.ValidationErrorCount;
        bool hasErrors = errorCount > 0;

        if (QstWidgets.RailButton(hasErrors ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.InfoCircle,
                _L("Quest Validation"),
                (running ? "(!) " : "") + _LF("Quest validation: {0} errors, {1} notes", errorCount, infoCount),
                tint: hasErrors ? QstTheme.Danger : (running || infoCount > 0 ? QstTheme.Info : null),
                showLabel: showLabel))
            questValidationWindow.ToggleOrUncollapse();
    }
}
