using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class QuickAccessButtonsComponent
(
    QuestController questController,
    QuestFunctions questFunctions,
    QuestRegistry questRegistry,
    QuestValidationWindow questValidationWindow,
    JournalProgressWindow journalProgressWindow,
    PriorityWindow priorityWindow,
    ICommandManager commandManager,
    IObjectTable objectTable,
    IClientState clientState,
    IChatGui chatGui,
    IDalamudPluginInterface pluginInterface)
{

    public event EventHandler? Reload;

    public void Draw()
    {
        DrawReloadDataButton();
        ImGui.SameLine();
        DrawRebuildNavmeshButton();
        ImGui.SameLine();
        DrawClearVBMMapsButton();

        if (pluginInterface.IsDev)
        {
            ImGui.SameLine();
            DrawValidationIssuesButton();
        }
    }

    internal void DrawPriorityQuestsButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.ExclamationCircle,
                _L("Priority Quests"),
                _L("Configure priority quests which will be done as soon as possible."),
                enabled: objectTable[0] != null,
                showLabel: showLabel))
            priorityWindow.ToggleOrUncollapse();
    }

    internal void DrawCleanUpButton(bool showLabel = false)
    {
        List<ElementId> openQuests = questFunctions.OpenQuests;
        int queueable = openQuests.Count(x => !questController.PriorityManager.Contains(x));
        bool loggedIn = objectTable[0] != null;

        string tooltip;
        if (!loggedIn)
            tooltip = _L("Unavailable while not logged in.");
        else if (openQuests.Count == 0)
            tooltip = _L("No open quests in your journal that Questionable knows how to continue.");
        else if (queueable == 0)
            tooltip = _LF("All {0} open quest(s) are already in your priority list.", openQuests.Count);
        else
            tooltip = _LF("Queue the {0} quest(s) you already have open, finished ones first.", queueable) +
                      "\n" +
                      _L("Long or blocked quests are queued too - check the priority list afterwards.");

        if (QstWidgets.RailButton(FontAwesomeIcon.Broom,
                _L("Clean Up"),
                tooltip,
                enabled: loggedIn && queueable > 0,
                showLabel: showLabel))
            QueueOpenQuests();
    }

    internal void QueueOpenQuests()
    {
        IReadOnlyList<Quest> added = questController.QueueOpenQuests();
        if (added.Count == 0)
        {
            chatGui.Print(_L("No open quests to clean up."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        chatGui.Print(_LF("Added {0} open quest(s) to the priority list:", added.Count) + " " +
                      string.Join(", ", added.Select(x => x.Info.Name)),
            CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    internal void DrawRebuildNavmeshButton(bool showLabel = false)
    {
        bool isNavmeshAvailable = commandManager.Commands.ContainsKey("/vnav");
        string tooltip = isNavmeshAvailable
            ? _L("Hold CTRL to enable this button. Rebuilding the navmesh will take some time.")
            : _L("vnavmesh is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.GlobeEurope, _L("Rebuild Navmesh"), tooltip,
                enabled: isNavmeshAvailable && ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                showLabel: showLabel))
            commandManager.ProcessCommand("/vnav rebuild");
    }

    internal void DrawClearVBMMapsButton(bool showLabel = false)
    {
        bool isNavmeshAvailable = commandManager.Commands.ContainsKey("/vbm");
        string tooltip = isNavmeshAvailable
            ? _L("Clear BossMod obstacle maps to fix pathfinding issues. Hold CTRL to enable this button")
            : _L("VBM is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.Directions, _L("Clear VBM obstacle maps"), tooltip,
                enabled: isNavmeshAvailable && ImGui.IsKeyDown(ImGuiKey.ModCtrl),
                showLabel: showLabel))
            commandManager.ProcessCommand("/vbm clear-maps");
    }

    internal void DrawReloadDataButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.RedoAlt, _L("Reload Data"),
                _L("Reset sequence progess and reload quest data from disk"),
                showLabel: showLabel))
            Reload?.Invoke(this, EventArgs.Empty);
    }

    internal void DrawJournalProgressButton(bool showLabel = false)
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.BookBookmark, _L("Journal Progress"),
                _L("Utility to browse quest data used by this plugin"),
                showLabel: showLabel))
            journalProgressWindow.ToggleOrUncollapse();
    }

    internal void DrawTroubleshootingButton(bool showLabel = false, bool highlighted = false)
    {
        QuestController.QuestProgress? questProgress = questController.CurrentQuest;
        bool isRunning = highlighted || questController.IsRunning;
        bool leftClicked = QstWidgets.RailButton(FontAwesomeIcon.Handshake,
            _L("Stuck?"),
            _L("Left click: Copy troubleshooting information to clipboard\nRight click: Copy list of completed quests to clipboard"),
            tint: isRunning ? QstTheme.Accent : null,
            enabled: objectTable[0] != null,
            showLabel: showLabel);
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (leftClicked || rightClicked)
        {
            string output = "";
            List<LogQuestCompletion.QuestCompletion> questCompletions = LogQuestCompletion.ReadQuestCompletions();
            if (rightClicked)
            {
                output = JsonSerializer.Serialize(questCompletions, JsonOptions.Default);
                ImGui.SetClipboardText(output);
                chatGui.Print(_L("List of completed quests has been copied to clipboard. Please paste it to this discord channel, and then run " +
                        "'/qst clearlog' to reset the log.") + "\nhttps://discord.com/channels/1001823907193552978/1447612869431656508/1447612869431656508",
                        CommandHandler.MessageTag, CommandHandler.TagColor);
            }
            else
            {
                // Dalamud troubleshooting json is written after plugin manager changes; we can't access the data from dalamud directly
                SortedDictionary<string, string>? plugins = [];
                try
                {
                    JsonNode? dalTrouble = JsonNode.Parse(
                            File.ReadAllText(Path.Join(pluginInterface.DalamudAssetDirectory.Parent?.Parent?.FullName, "dalamud.troubleshooting.json"))
                        );
                    var pluginNames = dalTrouble?["PluginStates"]
                        .Deserialize<SortedDictionary<string, string>>()
                        ?.Where(kvp => kvp.Value == "Loaded")
                        .Select(kvp => kvp.Key)
                        .ToHashSet(StringComparer.Ordinal);
                    plugins = new(dalTrouble?["LoadedPlugins"]
                        ?.AsArray()
                        .Where(node =>
                            node?["InstalledFromUrl"]?.GetValue<string>() is { Length: > 0 } &&
                            node?["InternalName"]?.GetValue<string>() is { } name &&
                            pluginNames?.Contains(name) == true)
                        .ToDictionary(
                            node => node!["Name"]!.GetValue<string>(),
                            node => node!["AssemblyVersion"]!.GetValue<string>() ?? "unknown"
                            , StringComparer.Ordinal) ?? [], StringComparer.Ordinal);
                }
                catch (Exception) { }
                Configuration? config = (Configuration?)pluginInterface.GetPluginConfig();
                IPlayerCharacter player = (IPlayerCharacter)objectTable[0]!;
                Dictionary<string, object?> troubleshooting = new(StringComparer.Ordinal){
                    { "LoadedPlugins", plugins },
                    { "QST", new Dictionary<string,string>(StringComparer.Ordinal){
                        { "Version", CommandHandler.MessageTag },
                        { "Debug", config?.Advanced.Debug.ToString() ?? "false" }
                    } },
                    { "Configuration", config },
                    { "CompletedQuests", questCompletions.Count },
                    { "Quest", new Dictionary<string,object?>(StringComparer.Ordinal){
                        { "ToString", questProgress?.ToString() },
                        { "QW", questProgress != null ? QuestFunctions.GetQuestProgressInfo(questProgress.Quest.Id)?.ToString() : "Error: questProgress is null" },
                        { "Source", questProgress?.Quest.Source }
                    }},
                    { "Character", new Dictionary<string,object>(StringComparer.Ordinal){
                        { "ClassJob", (EExtendedClassJob?)player.ClassJob.RowId },
                        { "Level", player.Level },
                        { "Position", player.Position.ToInternalString() },
                        { "Territory", TerritoryData.GetNameAndId(clientState.TerritoryType) }
                    }},
                };
                output = JsonSerializer.Serialize(troubleshooting, JsonOptions.Default);
                ImGui.SetClipboardText(output);
                chatGui.Print(_L("Troubleshooting information has been copied to clipboard. " +
                    "Please create a new thread in #questionable-issues in https://discord.gg/punishxiv describing the problem and pasting this troubleshooting information."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
        }
    }

    internal void DrawValidationIssuesButton(bool showLabel = false)
    {
        int errorCount = questRegistry.ValidationErrorCount;
        int infoCount = questRegistry.ValidationIssueCount - questRegistry.ValidationErrorCount;
        bool hasErrors = errorCount > 0;

        if (QstWidgets.RailButton(hasErrors ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.InfoCircle,
                _L("Quest Validation"),
                _LF("Quest validation: {0} errors, {1} infos", errorCount, infoCount),
                tint: hasErrors ? QstTheme.Danger : QstTheme.Info,
                showLabel: showLabel))
            questValidationWindow.ToggleOrUncollapse();
    }
}
