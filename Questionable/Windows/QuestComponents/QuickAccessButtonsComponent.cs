using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.QuestComponents;

internal sealed class QuickAccessButtonsComponent
(
    QuestController questController,
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
        DrawPriorityQuestsButton();
        ImGui.SameLine();
        DrawJournalProgressButton();
        ImGui.SameLine();
        DrawReloadDataButton();
        ImGui.SameLine();
        DrawRebuildNavmeshButton();
        ImGui.SameLine();
        DrawTroubleshootingButton(questController.CurrentQuest, questController.IsRunning);

        if (pluginInterface.IsDev)
        {
            ImGui.SameLine();
            DrawValidationIssuesButton();
        }
    }

    private void DrawPriorityQuestsButton()
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.ExclamationCircle,
                _L("Priority Quests"),
                _L("Configure priority quests which will be done as soon as possible."),
                enabled: objectTable[0] != null))
            priorityWindow.ToggleOrUncollapse();
    }

    private void DrawRebuildNavmeshButton()
    {
        bool isNavmeshAvailable = commandManager.Commands.ContainsKey("/vnav");
        string tooltip = isNavmeshAvailable
            ? _L("Hold CTRL to enable this button. Rebuilding the navmesh will take some time.")
            : _L("vnavmesh is not available. Please install it first.");
        if (QstWidgets.RailButton(FontAwesomeIcon.GlobeEurope, _L("Rebuild Navmesh"), tooltip,
                enabled: isNavmeshAvailable && ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            commandManager.ProcessCommand("/vnav rebuild");
    }

    private void DrawReloadDataButton()
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.RedoAlt, _L("Reload Data")))
            Reload?.Invoke(this, EventArgs.Empty);
    }

    private void DrawJournalProgressButton()
    {
        if (QstWidgets.RailButton(FontAwesomeIcon.BookBookmark, _L("Journal Progress")))
            journalProgressWindow.ToggleOrUncollapse();
    }

    private void DrawTroubleshootingButton(QuestController.QuestProgress? questProgress, bool isRunning)
    {
        bool leftClicked = QstWidgets.RailButton(FontAwesomeIcon.Handshake,
            _L("Stuck?"),
            _L("Left click: Copy troubleshooting information to clipboard\nRight click: Copy list of completed quests to clipboard"),
            tint: isRunning ? QstTheme.Accent : null,
            enabled: objectTable[0] != null);
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

    private void DrawValidationIssuesButton()
    {
        int errorCount = questRegistry.ValidationErrorCount;
        int infoCount = questRegistry.ValidationIssueCount - questRegistry.ValidationErrorCount;
        bool hasErrors = errorCount > 0;

        if (QstWidgets.RailButton(hasErrors ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.InfoCircle,
                _L("Quest Validation"),
                _LF("Quest validation: {0} errors, {1} infos", errorCount, infoCount),
                tint: hasErrors ? QstTheme.Danger : QstTheme.Info))
            questValidationWindow.ToggleOrUncollapse();
    }
}
