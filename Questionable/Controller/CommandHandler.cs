using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Shared;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class CommandHandler : IDisposable
{
    public const ushort TagColor = 576;
    public static readonly string MessageTag = $"QST v{typeof(QuestionablePlugin).Assembly.GetName().Version!.ToString(4)}";
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;

    private readonly ICommandManager _commandManager;
    private readonly Configuration _configuration;
    private readonly ConfigWindow _configWindow;
    private readonly IDataManager _dataManager;
    private readonly DebugOverlay _debugOverlay;
    private readonly GameFunctions _gameFunctions;
    private readonly JournalProgressWindow _journalProgressWindow;
    private readonly MovementController _movementController;
    private readonly OneTimeSetupWindow _oneTimeSetupWindow;
    private readonly PriorityWindow _priorityWindow;
    private readonly QuestController _questController;
    private readonly QuestStarter _questStarter;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelectionWindow _questSelectionWindow;
    private readonly QuestValidationWindow _questValidationWindow;
    private readonly QuestWindow _questWindow;
    private readonly QuestData _questData;
    private readonly EventInfoComponent _eventInfoComponent;
    private readonly OpenQuestCleanUp _openQuestCleanUp;
    private readonly ITargetManager _targetManager;

    private IReadOnlyList<uint> _lastUnlockLinks = [];

    public CommandHandler(
        ICommandManager commandManager,
        IChatGui chatGui,
        QuestController questController,
        QuestStarter questStarter,
        MovementController movementController,
        QuestRegistry questRegistry,
        ConfigWindow configWindow,
        DebugOverlay debugOverlay,
        OneTimeSetupWindow oneTimeSetupWindow,
        QuestWindow questWindow,
        QuestSelectionWindow questSelectionWindow,
        JournalProgressWindow journalProgressWindow,
        PriorityWindow priorityWindow,
        QuestValidationWindow questValidationWindow,
        ITargetManager targetManager,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        IDataManager dataManager,
        IClientState clientState,
        Configuration configuration,
        QuestData questData,
        EventInfoComponent eventInfoComponent,
        OpenQuestCleanUp openQuestCleanUp)
    {
        _commandManager = commandManager;
        _chatGui = chatGui;
        _questController = questController;
        _questStarter = questStarter;
        _movementController = movementController;
        _questRegistry = questRegistry;
        _configWindow = configWindow;
        _debugOverlay = debugOverlay;
        _oneTimeSetupWindow = oneTimeSetupWindow;
        _questWindow = questWindow;
        _questSelectionWindow = questSelectionWindow;
        _journalProgressWindow = journalProgressWindow;
        _priorityWindow = priorityWindow;
        _questValidationWindow = questValidationWindow;
        _targetManager = targetManager;
        _questFunctions = questFunctions;
        _gameFunctions = gameFunctions;
        _dataManager = dataManager;
        _clientState = clientState;
        _configuration = configuration;
        _questData = questData;
        _eventInfoComponent = eventInfoComponent;
        _openQuestCleanUp = openQuestCleanUp;

        _clientState.Logout += OnLogout;
        // Shown in the plugin installer; the Debug-only commands stay out of it.
        string[] help = [_L("Opens the Questing window"), .. HelpLines()];
        _commandManager.AddHandler("/qst", new(ProcessCommand)
        {
            HelpMessage = string.Join($"{Environment.NewLine}\t", help)
        });
        _commandManager.AddHandler("/qst@", new(ProcessDebugCommand)
        {
            ShowInHelp = false
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler("/qst@");
        _commandManager.RemoveHandler("/qst");
        _clientState.Logout -= OnLogout;
    }

    private void ProcessCommand(string command, string arguments)
    {
        if (OpenSetupIfNeeded(arguments))
            return;

        string[] parts = arguments.Split(' ');
        if (NeedsDebug(parts[0]) && !_configuration.Advanced.Debug)
        {
            PrintUnknown(parts[0]);
            return;
        }

        switch (parts[0])
        {
            // help-all used to be the long list; it stays as an unlisted alias.
            case "h":
            case "help":
            case "ha":
            case "help-all":
                PrintHelp(HelpLines());
                if (_configuration.Advanced.Debug)
                {
                    _chatGui.Print(_L("/qst do <questId> - highlights the specified quest in the debug overlay (requires debug overlay to be enabled)"), MessageTag, TagColor);
                    _chatGui.Print(_L("/qst do - clears the highlighted quest in the debug overlay (requires debug overlay to be enabled)"), MessageTag, TagColor);
                    _chatGui.Print(_L("/qst sim <questId> [sequence] [step] - simulates the specified quest (or clears it if no questId is specified)"), MessageTag, TagColor);
                    _chatGui.Print(_L("/qst mountid - prints information about your current mount"), MessageTag, TagColor);
                }

                break;

            case "c":
            case "config":
                _configWindow.ToggleOrUncollapse();
                break;

            case "start":
                _questWindow.IsOpenAndUncollapsed = true;
                _questStarter.Start(_L("Start command"));
                break;

            case "stop":
                if (!_questController.HasExternalOperation)
                    _movementController.Stop();
                _questController.Stop(_L("Stop command"));
                break;

            case "reload":
                _questWindow.Reload();
                break;

            case "skipstep":
                if (_questController.CurrentQuest == null)
                    _chatGui.PrintError(_L("There's no current quest."), MessageTag, TagColor);
                else if (!_questController.CanSkipStep())
                    _chatGui.PrintError(_L("The last step of a sequence can't be skipped; the game moves the quest on once it's done."), MessageTag, TagColor);
                else
                    _questController.SkipStep();
                break;

            case "do":
                ConfigureDebugOverlay(parts.Skip(1).ToArray());
                break;

            case "next":
                SetNextQuest(parts.Skip(1).ToArray());
                break;

            case "sim":
                SetSimulatedQuest(parts.Skip(1).ToArray());
                break;

            case "which":
                if (_targetManager.Target == null)
                    _chatGui.Print(_L("Target an NPC first, then use /qst which."), MessageTag, TagColor);
                else
                    _questSelectionWindow.OpenForTarget(_targetManager.Target, GameFunctions.GetBaseId(_targetManager.Target));
                break;

            case "z":
            case "zone":
                if (parts.Length < 2)
                    _questSelectionWindow.OpenForCurrentZone();
                else if (uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint zoneId))
                    _questSelectionWindow.OpenForZone(zoneId);
                else
                    PrintNotNumber(parts[1]);
                break;

            case "j":
            case "journal":
                _journalProgressWindow.ToggleOrUncollapse();
                break;

            case "p":
            case "priority":
                _priorityWindow.ToggleOrUncollapse();
                break;

            case "cleanup":
                _openQuestCleanUp.Run();
                break;

            case "mountid":
                PrintMountId();
                break;

            case "handle-interrupt":
                _questController.InterruptQueueWithCombat();
                break;

            case "validation":
                _questValidationWindow.ToggleOrUncollapse();
                break;

            case "d2qwh":
                if (parts.Length < 2)
                    break;
                string highText = FormatQuestWork(parts.Skip(1).ToArray(), high: true);
                ImGui.SetClipboardText(highText);
                _chatGui.Print(highText);
                break;

            case "d2qwl":
                if (parts.Length < 2)
                    break;
                string lowText = FormatQuestWork(parts.Skip(1).ToArray());
                ImGui.SetClipboardText(lowText);
                _chatGui.Print(lowText);
                break;

            case "gen":
                _questRegistry.OpenEditor();
                break;

            case "clearlog":
                LogQuestCompletion.ClearLog();
                _chatGui.Print(_L("Completions log has been cleared"), MessageTag, TagColor);
                break;

            case "debug":
            case "debugmode":
                _configuration.Advanced.Debug = !_configuration.Advanced.Debug;
                _configuration.Save();
                _chatGui.Print(_configuration.Advanced.Debug ? _L("Debug mode enabled.") : _L("Debug mode disabled."),
                    MessageTag, TagColor);
                break;

            case "rewards":
                ushort rewardsId = 2772;
                if (parts.Length > 1 && !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out rewardsId))
                {
                    PrintNotNumber(parts[1]);
                    break;
                }

                if (_questData.TryGetQuestInfo(new QuestId(rewardsId), out var info) &&
                    info is QuestInfo questInfo &&
                    questInfo.ItemRewards.Concat(questInfo.TripleTriadCardRewards).Select(x => x.ToString()) is var rewards &&
                    rewards.ToArray().Length != 0)
                    _chatGui.Print($"{string.Join(',', rewards)}", MessageTag, TagColor);
                else
                    _chatGui.Print("no results", MessageTag, TagColor);
                break;

            case "tname":
                if (parts.Length == 1)
                    break;
                if (uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint territoryId))
                    _chatGui.Print($"{TerritoryData.GetNameAndId(territoryId)}");
                else
                    PrintNotNumber(parts[1]);
                break;

            case "titlebarpill":
                _configuration.General.TitleBarPillCenter = !_configuration.General.TitleBarPillCenter;
                _configuration.Save();
                break;

            case "redeem":
                _questStarter.RedeemRewards(dry: parts.Length > 1 && parts[1].EqualsIgnoreCaseAny("dry"));
                break;

            case "":
                _questWindow.ToggleOrUncollapse();
                break;

            default:
                PrintUnknown(parts[0]);
                break;
        }
    }

    private static string[] HelpLines() =>
    [
        _L("/qst help - lists these commands"),
        _L("/qst config - opens the configuration window"),
        _L("/qst start - starts doing quests"),
        _L("/qst stop - stops doing quests"),
        _L("/qst reload - reload all quest data"),
        _L("/qst skipstep - skips the current quest step, e.g. one you finished by hand"),
        _L("/qst next <questId> - sets the next quest to do (or clears it if no questId is specified)"),
        _L("/qst which - shows all quests starting with your selected target"),
        _L("/qst zone - shows all quests starting in the current zone (only includes quests with a known quest path, and currently visible unaccepted quests)"),
        _L("/qst journal - toggles the Journal Progress window"),
        _L("/qst priority - toggles the Priority window"),
        _L("/qst validation - toggles the Quest Validation window"),
        _L("/qst cleanup - adds every quest you already have open to the priority list, finished ones first"),
        _L("/qst handle-interrupt - makes Questionable handle queued interrupts immediately (useful if you manually start combat)"),
        _L("/qst clearlog - clears QuestCompletionLog.json"),
        _L("/qst redeem - Redeem all reward items in inventory"),
    ];

    private void PrintHelp(string[] lines)
    {
        _chatGui.Print(_L("Available commands:"), MessageTag, TagColor);
        _chatGui.Print(_L("/qst - toggles the Questing window"), MessageTag, TagColor);
        foreach (string line in lines)
            _chatGui.Print(line, MessageTag, TagColor);
    }

    // Tools for writing and checking quest paths; without Debug they answer like an unknown command.
    internal static bool NeedsDebug(string subcommand) =>
        subcommand is "do" or "sim" or "mountid" or "gen" or "d2qwh" or "d2qwl" or "rewards" or "tname";

    private void PrintUnknown(string subcommand) =>
        _chatGui.PrintError(_LF("Unknown subcommand {0}", subcommand), MessageTag, TagColor);

    private void PrintNotNumber(string value) =>
        _chatGui.PrintError(_LF("'{0}' is not a number.", value), MessageTag, TagColor);

    [SuppressMessage("Globalization", "CA1305")]
    private static string FormatQuestWork(string[] parts, bool high = false)
    {
        List<string> values = [];
        foreach (string part in parts)
        {
            if (!byte.TryParse(part.RemoveOtherChars("0123456789"), NumberStyles.None, CultureInfo.InvariantCulture, out byte d))
                continue;

            QuestWorkValue work = new(d);
            string value = $" {{\"{(high ? "High" : "Low")}\": {(high ? work.High : work.Low)}}}";
            if (!values.Contains(value))
                values.Add(value);
        }

        return values.Join(",");
    }

    private void ProcessDebugCommand(string command, string arguments)
    {
        if (OpenSetupIfNeeded(arguments))
            return;

        string[] parts = arguments.Split(' ');
        if (!_configuration.Advanced.Debug)
        {
            PrintUnknown(parts[0]);
            return;
        }

        switch (parts[0])
        {
            case "eventquests":
                var eventQuests = _eventInfoComponent.GetActiveQuests().Select(q => q.ToString()).ToArray();
                if (eventQuests.Length > 0)
                    _chatGui.Print(string.Join(", ", eventQuests));
                else
                    _chatGui.Print("There are no active event quests.", MessageTag, TagColor);
                break;
            case "unlock-links":
                IReadOnlyList<uint>? unlockLinks = _gameFunctions.GetUnlockLinks();
                if (unlockLinks != null)
                {
                    _chatGui.Print(_LF("Saved {0} unlock links to log.", unlockLinks.Count), MessageTag, TagColor);

                    List<uint> newUnlockLinks = unlockLinks.Except(_lastUnlockLinks).ToList();
                    if (_lastUnlockLinks.Count > 0 && newUnlockLinks.Count > 0)
                        _chatGui.Print(_LF("New unlock links: {0}", string.Join(", ", newUnlockLinks)), MessageTag, TagColor);

                    _lastUnlockLinks = unlockLinks;
                }
                else
                    _chatGui.PrintError(_L("Could not query unlock links."), MessageTag, TagColor);

                break;

            case "taxi":
                unsafe
                {
                    List<string> taxiStands = [];
                    ExcelSheet<ChocoboTaxiStand> taxiStandNames = _dataManager.GetExcelSheet<ChocoboTaxiStand>();
                    UIState* uiState = UIState.Instance();
                    for (byte i = 0; i < uiState->UnlockedChocoboTaxiStands.Length * 8; ++i)
                    {
                        if (!(uiState->IsChocoboTaxiStandUnlocked(i)) && taxiStandNames.HasRow(i + 0x120000u))
                        {
                            ChocoboTaxiStand row = taxiStandNames.GetRow(i + 0x120000u);
                            // 0 and 1 are unused
                            if (row.TargetLocations[0].RowId >= 2)
                                taxiStands.Add($"{row.PlaceName} ({i})");
                        }
                    }

                    _chatGui.Print(_L("Locked taxi stands:"), MessageTag, TagColor);
                    foreach (string taxiStand in taxiStands)
                        _chatGui.Print($"- {taxiStand}", MessageTag, TagColor);
                }

                break;

            case "festivals":
                unsafe
                {
                    List<string> activeFestivals = [];
                    for (byte i = 0; i < 4; ++i)
                    {
                        GameMain.Festival festival = GameMain.Instance()->ActiveFestivals[i];
                        if (festival.Id == 0)
                            continue;

                        activeFestivals.Add($"{festival.Id}({festival.Phase})");
                    }

                    _chatGui.Print(_LF("Active festivals: {0}", string.Join(", ", activeFestivals)), MessageTag, TagColor);
                }

                break;

            case "loc":
                _configuration.Advanced.DebugLocalisation = !_configuration.Advanced.DebugLocalisation;
                _configuration.Save();
                _chatGui.Print(_L("This setting takes effect after the plugin is reloaded."));
                break;

            default:
                PrintUnknown(parts[0]);
                break;
        }
    }

    private bool OpenSetupIfNeeded(string arguments)
    {
        if (!_configuration.IsPluginSetupComplete())
        {
            if (string.IsNullOrEmpty(arguments))
                _oneTimeSetupWindow.IsOpenAndUncollapsed = true;
            else
                _chatGui.PrintError(_L("Please complete the one-time setup first."), MessageTag, TagColor);
            return true;
        }

        return false;
    }

    private void ConfigureDebugOverlay(string[] arguments)
    {
        if (!_debugOverlay.DrawConditions())
        {
            _chatGui.PrintError(_L("You don't have the debug overlay enabled."), MessageTag, TagColor);
            return;
        }

        if (arguments.Length >= 1 && ElementId.TryFromString(arguments[0], out ElementId? questId) && questId != null)
        {
            if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                _debugOverlay.HighlightedQuest = quest.Id;
                _chatGui.Print(_LF("Set highlighted quest to {0} ({1}).", questId, quest.Info.Name), MessageTag, TagColor);
            }
            else
                _chatGui.PrintError(_LF("Unknown quest {0}.", questId), MessageTag, TagColor);
        }
        else
        {
            _debugOverlay.HighlightedQuest = null;
            _chatGui.Print(_L("Cleared highlighted quest."), MessageTag, TagColor);
        }
    }

    private void SetNextQuest(string[] arguments)
    {
        if (arguments.Length >= 1 && ElementId.TryFromString(arguments[0], out ElementId? questId) && questId != null)
        {
            (var isLocked, string[]? reasons) = _questFunctions.IsQuestLocked(questId);
            if (isLocked)
                _chatGui.PrintError(_LF("Quest {0} is locked.", questId) + (reasons != null ? string.Join(',', reasons) : ""),
                    MessageTag, TagColor);
            else if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                _questController.SetNextQuest(quest);
                _chatGui.Print(_LF("Set next quest to {0} ({1}).", questId, quest.Info.Name) + (reasons != null ? string.Join(',', reasons) : ""),
                    MessageTag, TagColor);
            }
            else
                _chatGui.PrintError(_LF("Unknown quest {0}.", questId) + (reasons != null ? string.Join(',', reasons) : ""),
                    MessageTag, TagColor);
        }
        else
        {
            _questController.SetNextQuest(quest: null);
            _chatGui.Print(_L("Cleared next quest."), MessageTag, TagColor);
        }
    }

    private void SetSimulatedQuest(string[] arguments)
    {
        if (arguments.Length >= 1 && ElementId.TryFromString(arguments[0], out ElementId? questId) && questId != null)
        {
            if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                byte sequenceId = 0;
                int stepId = 0;
                if (arguments.Length >= 2 && byte.TryParse(arguments[1], out byte parsedSequence))
                {
                    QuestSequence? sequence = quest.FindSequence(parsedSequence);
                    if (sequence != null)
                    {
                        sequenceId = sequence.Sequence;
                        if (arguments.Length >= 3 && int.TryParse(arguments[2], out int parsedStep))
                        {
                            QuestStep? step = sequence.FindStep(parsedStep);
                            if (step != null)
                                stepId = parsedStep;
                        }
                    }
                }

                _questStarter.SimulateQuest(quest, sequenceId, stepId);
                _chatGui.Print(_LF("Simulating quest {0} ({1}).", questId, quest.Info.Name), MessageTag, TagColor);
            }
            else
                _chatGui.PrintError(_LF("Unknown quest {0}.", questId), MessageTag, TagColor);
        }
        else
        {
            _questStarter.StopSimulate();
            _chatGui.Print(_questController.SimulatedQuest == null
                ? _L("Cleared simulated quest.")
                : _L("Wait for the external operation to stop, then clear the simulated quest again."), MessageTag, TagColor);
        }
    }

    private void PrintMountId()
    {
        ushort? mountId = GameFunctions.GetMountId();
        if (mountId != null)
        {
            Mount? row = _dataManager.GetExcelSheet<Mount>().GetRowOrDefault(mountId.Value);
            _chatGui.Print(
                _LF("Mount ID: {0}, Name: {1}, Obtainable: {2}", mountId, row?.Singular.ToString() ?? "", (row?.Order == -1 ? "No" : "Yes")),
                MessageTag, TagColor);
        }
        else
            _chatGui.Print(_L("You are not mounted."), MessageTag, TagColor);
    }

    private void OnLogout(int type, int code) => _lastUnlockLinks = [];
}
