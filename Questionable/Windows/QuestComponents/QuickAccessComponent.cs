using System.IO.Compression;
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
    ReportApi reportApi,
    AutoRetainerIpc autoRetainer,
    IChatGui chatGui,
    IDalamudPluginInterface pluginInterface)
{
    private static readonly HashSet<string> ReportedPlugins = new(StringComparer.Ordinal)
    {
        "Artisan", "AutoDuty", "AutoRetainer", "Automaton", "BossMod", "BossModReborn", "GatherChill", "Knightshopper", "Lifestream",
        "MappingTheRealm", "Mogmail", "PandorasBox", "RotationSolver", "SelectString", "Stylist", "TextAdvance", "vnavmesh", "vsatisfy", "WahTools",
        "WigglyQuest", "WrathCombo", "YesAlready",
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
            _L("Saves a zip with troubleshooting information and the recent log, to attach to a report in #questionable-issues"),
            tint: QstTheme.SpecialButton,
            enabled: objectTable[0] != null,
            showLabel: showLabel);
        if (leftClicked)
            Export(auto: false);
    }

    internal void Export(bool auto, EErrorKind? kind = null)
    {
        // Retry loops fail the same way over and over; one zip covers them.
        if (objectTable[0] == null || auto && DateTime.Now - _lastAutoExport < AutoExportGap)
            return;

        TroubleshootingReport.Input input = BuildReport();
        string report = TroubleshootingReport.Build(input);
        string? contentId = ProfileController.CurrentContentId;
        Upload? upload = auto && Configuration.Instance().Reports.Share == true && contentId != null
            ? new(QuestReporter.CharacterHash(contentId), questController.CurrentQuest?.Quest.Id.ToString(),
                kind is { } error ? ErrorName(error) : null)
            : null;
        if (auto)
            _lastAutoExport = DateTime.Now;
        // Reading a whole dalamud.log takes a moment; WahTools exports off the game thread too.
        if (Interlocked.Exchange(ref _exporting, 1) == 0)
            Task.Run(() => WriteReportFile(report, input.PrivateTexts, contentId, auto, upload));
    }

    private sealed record Upload(string Character, string? Quest, string? Kind);

    internal static string ErrorName(EErrorKind kind) => kind switch
    {
        EErrorKind.Path => "path",
        EErrorKind.MappingTheRealm => "mtr",
        EErrorKind.Death => "death",
        EErrorKind.Step => "step",
        _ => "other",
    };

    private static readonly TimeSpan AutoExportGap = TimeSpan.FromMinutes(5);
    private const int AutoExportsKept = 10;
    private DateTime _lastAutoExport = DateTime.MinValue;
    private int _exporting;
    private static readonly string[] LogFiles = ["dalamud.old.log", "dalamud.log"];

    private static readonly string[] LoggedPlugins = ["Questionable", "MappingTheRealm", "vnavmesh"];

    private async Task WriteReportFile(string report, IReadOnlyList<string> privateTexts, string? contentId, bool auto,
        Upload? upload)
    {
        try
        {
            (DirectoryInfo folder, string file) = SaveZip(report, privateTexts, contentId, auto);
            if (auto)
            {
                foreach (FileInfo old in folder.GetFiles("Questionable-error-*.zip").OrderByDescending(x => x.Name, StringComparer.Ordinal).Skip(AutoExportsKept))
                    old.Delete();
                string? code = upload == null
                    ? null
                    : await reportApi.UploadZipAsync(await File.ReadAllBytesAsync(file).ConfigureAwait(false), upload.Character,
                        upload.Quest, upload.Kind, typeof(QuickAccessComponent).Assembly.GetName().Version!.ToString(4)).ConfigureAwait(false);
                string message = code != null
                    ? _LF("Uploaded a troubleshooting zip for this error, report code {0}. If you report it in #questionable-issues, mention this code. A copy is saved in {1}", code, file)
                    : _LF("Saved a troubleshooting zip for this error: {0}. If you report it in #questionable-issues, attach this zip.", file);
                await Svc.Framework.RunOnFrameworkThread(() => chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor))
                    .ConfigureAwait(false);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            await Svc.Framework.RunOnFrameworkThread(() => chatGui.Print(
                _LF("Troubleshooting zip saved: {0}. Create a new thread in #questionable-issues on https://discord.gg/punishxiv, describe the problem and attach this zip from the folder that just opened.", file),
                CommandHandler.MessageTag, CommandHandler.TagColor)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Svc.Log.Warning(e, "Couldn't write the troubleshooting zip");
        }
        finally
        {
            Interlocked.Exchange(ref _exporting, 0);
        }
    }

    private (DirectoryInfo Folder, string File) SaveZip(string report, IReadOnlyList<string> privateTexts, string? contentId, bool auto)
    {
        string? root = DalamudLog.FindFolder(pluginInterface.ConfigDirectory.Parent?.Parent?.FullName, File.Exists);
        // The old log is the previous session, e.g. one that crashed; WahTools exports both too.
        List<string> entries = root == null
            ? ["No dalamud.log was found next to Questionable's settings or in its logs folder; questionable-recent.log has Questionable's own lines."]
            : [.. LogFiles.Select(x => Path.Combine(root, x)).Where(File.Exists)
                .SelectMany(x => DalamudLog.Read(x, LoggedPlugins))];
        if (root != null && DalamudLog.IsFull(Path.Combine(root, "dalamud.log")))
            entries.Insert(0, "dalamud.log reached Dalamud's 100 MB limit and stopped growing, so it misses this session's recent lines; questionable-recent.log has Questionable's own. Restarting the game starts a new log.");
        DirectoryInfo folder = pluginInterface.ConfigDirectory.CreateSubdirectory("reports");
        string file = Path.Combine(folder.FullName,
            string.Create(CultureInfo.InvariantCulture, $"Questionable-{(auto ? "error-" : "")}{DateTime.Now:yyyy-MM-dd-HHmmss}.zip"));
        using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            AddText(zip, "report.txt", report);
            AddText(zip, "dalamud-questionable.log", TroubleshootingReport.LogText(entries, privateTexts));
            AddText(zip, "questionable-recent.log", TroubleshootingReport.LogText(recentLog.GetLines(), privateTexts));
            if (TravelSettings(contentId) is { } travel)
                AddText(zip, "mappingtherealm-settings.txt", travel);
        }

        return (folder, file);
    }

    private string? TravelSettings(string? contentId)
    {
        try
        {
            string? folder = pluginInterface.ConfigDirectory.Parent?.FullName;
            string path = Path.Combine(folder ?? "", "MappingTheRealm.json");
            if (folder == null || !File.Exists(path))
                return null;

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            return TroubleshootingReport.TravelSettings(reader.ReadToEnd(), contentId);
        }
        catch (Exception e)
        {
            Svc.Log.Warning(e, "Couldn't read MappingTheRealm's settings for the troubleshooting zip");
            return null;
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using StreamWriter writer = new(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private TroubleshootingReport.Input BuildReport()
    {
        Configuration config = Configuration.Instance();
        IPlayerCharacter player = (IPlayerCharacter)objectTable[0]!;
        QuestController.QuestProgress? progress = questController.CurrentQuest;
        QuestStep? step = progress?.FindStep();

        List<string?> facts =
        [
            string.Create(CultureInfo.InvariantCulture, $"{CommandHandler.MessageTag}{(pluginInterface.IsDev ? " dev" : "")} | paths {config.PathData.InstalledDataVersion} | client {clientState.ClientLanguage} | ui {config.General.Language} | profile: {(config.ActiveProfileName != null ? "yes" : "no")}"),
            progress == null
                ? "Quest: none"
                : string.Create(CultureInfo.InvariantCulture, $"Quest: {progress.Quest.Id} {progress.Quest.Info.Name} | seq {progress.Sequence} step {progress.Step} | source: {progress.Quest.Source}"),
            progress == null ? null : step == null
                ? "Step: no path data for this step"
                : $"Step: {step.InteractionType}, target {step.DataId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, territory {TerritoryData.GetNameAndId(step.TerritoryId)}",
            progress == null ? null : $"Game quest state: {questFunctions.GetQuestProgressInfo(progress.Quest.Id)?.ToString() ?? "not accepted"}",
            $"State: {questController.AutomationType}, {(questController.IsRunning ? "running" : "idle")} | task: {questController.ToStatString()} | status: {questController.DebugState ?? "-"} | executor: {questController.CurrentTaskState ?? "-"}{(questController.HasExternalOperation ? " | plugin operation active" : "")}",
            string.Create(CultureInfo.InvariantCulture, $"Character: {player.ClassJob.Value.Abbreviation} {player.Level} | {TerritoryData.GetNameAndId(clientState.TerritoryType)} | {player.Position.ToInternalString()} | target {targetManager.Target?.BaseId.ToString(CultureInfo.InvariantCulture) ?? "none"} | flags: {DescribeConditions()}"),
            DescribePlugins(),
            TroubleshootingReport.DescribeFailure(movementController.LastFailure, DateTimeOffset.UtcNow),
        ];

        return new()
        {
            Facts = facts.OfType<string>().ToList(),
            Settings = TroubleshootingReport.ChangedSettings(config),
            Log = recentLog.GetEntries(),
            // The file's log lines can name alts (character list, AutoRetainer pass).
            PrivateTexts = [player.Name.TextValue, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName,
                .. autoRetainer.GetCharacters().Select(x => x.Name)],
            NowUtc = DateTime.UtcNow,
        };
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
