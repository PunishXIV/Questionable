using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Shared;
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Windows;
using Quest = Questionable.Model.Quest;
using static Questionable.Utils.LocalizeShortcut;
using Questionable.Data;
using Questionable.Model;

namespace Questionable.Controller;

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
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelectionWindow _questSelectionWindow;
    private readonly QuestValidationWindow _questValidationWindow;
    private readonly QuestWindow _questWindow;
    private readonly QuestData _questData;
    private readonly TerritoryData _territoryData;
    private readonly ITargetManager _targetManager;

    private IReadOnlyList<uint> _previouslyUnlockedUnlockLinks = [];

    public CommandHandler(
        ICommandManager commandManager,
        IChatGui chatGui,
        QuestController questController,
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
        TerritoryData territoryData)
    {
        _commandManager = commandManager;
        _chatGui = chatGui;
        _questController = questController;
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
        _territoryData = territoryData;

        _clientState.Logout += OnLogout;
        _commandManager.AddHandler("/qst", new(ProcessCommand)
        {
            HelpMessage = string.Join($"{Environment.NewLine}\t",
                _L("Opens the Questing window"),
                _L("/qst help - displays simplified commands"),
                _L("/qst help-all - displays all available commands"),
                _L("/qst config - opens the configuration window"),
                _L("/qst start - starts doing quests"),
                _L("/qst stop - stops doing quests"))
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
        switch (parts[0])
        {
            case "h":
            case "help":
                _chatGui.Print(_L("Available commands:"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst - toggles the Questing window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst help - displays simplified commands"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst help-all - displays all available commands"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst config - opens the configuration window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst start - starts doing quests"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst stop - stops doing quests"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst reload - reload all quest data"), MessageTag, TagColor);
                break;

            case "ha":
            case "help-all":
                _chatGui.Print(_L("Available commands:"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst - toggles the Questing window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst help - displays simplified commands"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst help-all - displays all available commands"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst config - opens the configuration window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst start - starts doing quests"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst stop - stops doing quests"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst reload - reload all quest data"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst do <questId> - highlights the specified quest in the debug overlay (requires debug overlay to be enabled)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst do - clears the highlighted quest in the debug overlay (requires debug overlay to be enabled)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst next <questId> - sets the next quest to do (or clears it if no questId is specified)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst sim <questId> [sequence] [step] - simulates the specified quest (or clears it if no questId is specified)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst which - shows all quests starting with your selected target"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst zone - shows all quests starting in the current zone (only includes quests with a known quest path, and currently visible unaccepted quests)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst journal - toggles the Journal Progress window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst priority - toggles the Priority window"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst mountid - prints information about your current mount"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst handle-interrupt - makes Questionable handle queued interrupts immediately (useful if you manually start combat)"), MessageTag, TagColor);
                _chatGui.Print(_L("/qst clearlog - clears QuestCompletionLog.json"), MessageTag, TagColor);
                break;

            case "c":
            case "config":
                _configWindow.ToggleOrUncollapse();
                break;

            case "start":
                _questWindow.IsOpenAndUncollapsed = true;
                _questController.Start(_L("Start command"));
                break;

            case "stop":
                _movementController.Stop();
                _questController.Stop(_L("Stop command"));
                break;

            case "reload":
                _questWindow.Reload();
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
                _questSelectionWindow.OpenForTarget(_targetManager.Target, GameFunctions.GetBaseID(_targetManager.Target));
                break;

            case "z":
            case "zone":
                if (parts.Length < 2)
                    _questSelectionWindow.OpenForCurrentZone();
                else
                    _questSelectionWindow.OpenForZone(uint.Parse(parts.Skip(1).First(), CultureInfo.InvariantCulture));
                break;

            case "j":
            case "journal":
                _journalProgressWindow.ToggleOrUncollapse();
                break;

            case "p":
            case "priority":
                _priorityWindow.ToggleOrUncollapse();
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
                string highOutp = D2QW(parts.Skip(1).ToArray(), true);
                ImGui.SetClipboardText(highOutp);
                _chatGui.Print(highOutp);
                break;

            case "d2qwl":
                if (parts.Length < 2)
                    break;
                string lowOutp = D2QW(parts.Skip(1).ToArray());
                ImGui.SetClipboardText(lowOutp);
                _chatGui.Print(lowOutp);
                break;

            case "gen":
                _questRegistry.OpenEditor();
                break;

            case "clearlog":
                LogQuestCompletion.ClearQuestCompletions();
                _chatGui.PrintError(_L("Completions log has been cleared"));
                break;

            case "@debugmode":
                _configuration.Advanced.Debug = !_configuration.Advanced.Debug;
                _configuration.Save();
                break;

            case "rewards":
                ushort rewardsId = 2772;
                if (parts.Length > 1)
                    rewardsId = ushort.Parse(parts[1], CultureInfo.InvariantCulture);
                if (_questData.TryGetQuestInfo(new QuestId(rewardsId), out var qInfo) &&
                    qInfo is QuestInfo questInfo &&
                    questInfo.ItemRewards.Concat(questInfo.TripleTriadCardRewards).Select(x => x.ToString()) is var rewards &&
                    rewards.ToArray().Length != 0)
                    _chatGui.Print($"{string.Join(',', rewards)}", MessageTag, TagColor);
                else
                    _chatGui.Print("no results", MessageTag, TagColor);
                break;

            case "tname":
                if (parts.Length == 1)
                    break;
                var tnameId = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                _chatGui.Print($"{TerritoryData.GetNameAndId(tnameId)}");
                break;

            //case "abandon-quest":
            //    if (parts.Length > 1)
            //        _questController.AbandonQuest(parts[1]);
            //    else
            //        _questController.AbandonQuest();
            //    break;

            case "":
                _questWindow.ToggleOrUncollapse();
                break;

            default:
                _chatGui.PrintError(_LF("Unknown subcommand {0}", parts[0]), MessageTag, TagColor);
                break;
        }
    }

    [SuppressMessage("Globalization", "CA1305")]
    private static string D2QW(string[] parts, bool High = false)
    {
        List<string> outp = [];
        foreach (string part in parts)
        {
            byte d = byte.Parse(part.RemoveOtherChars("0123456789"), CultureInfo.InvariantCulture);
            QuestWorkValue qw = new(d);
            string value = " {\"" + (High ? "High" : "Low") + "\": " + (High ? qw.High : qw.Low) + "}";
            if (!outp.Contains(value))
                outp.Add(value);
        }

        return outp.Join(",");
    }

    private void ProcessDebugCommand(string command, string arguments)
    {
        if (OpenSetupIfNeeded(arguments))
            return;

        string[] parts = arguments.Split(' ');
        switch (parts[0])
        {
            case "abandon-duty":
                _gameFunctions.AbandonDuty();
                break;

            case "unlock-links":
                IReadOnlyList<uint>? unlockedUnlockLinks = _gameFunctions.GetUnlockLinks();
                if (unlockedUnlockLinks != null)
                {
                    _chatGui.Print(_LF("Saved {0} unlock links to log.", unlockedUnlockLinks.Count), MessageTag, TagColor);

                    List<uint> newUnlockLinks = unlockedUnlockLinks.Except(_previouslyUnlockedUnlockLinks).ToList();
                    if (_previouslyUnlockedUnlockLinks.Count > 0 && newUnlockLinks.Count > 0)
                        _chatGui.Print(_LF("New unlock links: {0}", string.Join(", ", newUnlockLinks)), MessageTag, TagColor);

                    _previouslyUnlockedUnlockLinks = unlockedUnlockLinks;
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
                _chatGui.PrintError(_LF("Quest {0} is locked.", questId) + (reasons != null ? string.Join(',',reasons) : ""),
                    MessageTag, TagColor);
            else if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                _questController.SetNextQuest(quest);
                _chatGui.Print(_LF("Set next quest to {0} ({1}).", questId, quest.Info.Name) + (reasons != null ? string.Join(',',reasons) : ""),
                    MessageTag, TagColor);
            }
            else
                _chatGui.PrintError(_LF("Unknown quest {0}.", questId) + (reasons != null ? string.Join(',',reasons) : ""),
                    MessageTag, TagColor);
        }
        else
        {
            _questController.SetNextQuest(null);
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

                _questController.SimulateQuest(quest, sequenceId, stepId);
                _chatGui.Print(_LF("Simulating quest {0} ({1}).", questId, quest.Info.Name), MessageTag, TagColor);
            }
            else
                _chatGui.PrintError(_LF("Unknown quest {0}.", questId), MessageTag, TagColor);
        }
        else
        {
            _questController.StopSimulate();
            _chatGui.Print(_L("Cleared simulated quest."), MessageTag, TagColor);
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

    private void OnLogout(int type, int code) => _previouslyUnlockedUnlockLinks = [];
}
