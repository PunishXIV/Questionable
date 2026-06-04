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
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Windows;
using Quest = Questionable.Model.Quest;

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
        Configuration configuration)
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

        _clientState.Logout += OnLogout;
        _commandManager.AddHandler("/qst", new(ProcessCommand)
        {
            HelpMessage = string.Join($"{Environment.NewLine}\t",
                "Opens the Questing window",
                "/qst help - displays simplified commands",
                "/qst help-all - displays all available commands",
                "/qst config - opens the configuration window",
                "/qst start - starts doing quests",
                "/qst stop - stops doing quests")
        });
#if DEBUG
        _commandManager.AddHandler("/qst@", new(ProcessDebugCommand)
        {
            ShowInHelp = false
        });
#endif
    }

    public void Dispose()
    {
#if DEBUG
        _commandManager.RemoveHandler("/qst@");
#endif
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
                _chatGui.Print("Available commands:", MessageTag, TagColor);
                _chatGui.Print("/qst - toggles the Questing window", MessageTag, TagColor);
                _chatGui.Print("/qst help - displays simplified commands", MessageTag, TagColor);
                _chatGui.Print("/qst help-all - displays all available commands", MessageTag, TagColor);
                _chatGui.Print("/qst config - opens the configuration window", MessageTag, TagColor);
                _chatGui.Print("/qst start - starts doing quests", MessageTag, TagColor);
                _chatGui.Print("/qst stop - stops doing quests", MessageTag, TagColor);
                _chatGui.Print("/qst reload - reload all quest data", MessageTag, TagColor);
                break;

            case "ha":
            case "help-all":
                _chatGui.Print("Available commands:", MessageTag, TagColor);
                _chatGui.Print("/qst - toggles the Questing window", MessageTag, TagColor);
                _chatGui.Print("/qst help - displays available commands", MessageTag, TagColor);
                _chatGui.Print("/qst help-all - displays all available commands", MessageTag, TagColor);
                _chatGui.Print("/qst config - opens the configuration window", MessageTag, TagColor);
                _chatGui.Print("/qst start - starts doing quests", MessageTag, TagColor);
                _chatGui.Print("/qst stop - stops doing quests", MessageTag, TagColor);
                _chatGui.Print("/qst reload - reload all quest data", MessageTag, TagColor);
                _chatGui.Print("/qst do <questId> - highlights the specified quest in the debug overlay (requires debug overlay to be enabled)", MessageTag, TagColor);
                _chatGui.Print("/qst do - clears the highlighted quest in the debug overlay (requires debug overlay to be enabled)", MessageTag, TagColor);
                _chatGui.Print("/qst next <questId> - sets the next quest to do (or clears it if no questId is specified)", MessageTag, TagColor);
                _chatGui.Print("/qst sim <questId> [sequence] [step] - simulates the specified quest (or clears it if no questId is specified)", MessageTag, TagColor);
                _chatGui.Print("/qst which - shows all quests starting with your selected target", MessageTag, TagColor);
                _chatGui.Print("/qst zone - shows all quests starting in the current zone (only includes quests with a known quest path, and currently visible unaccepted quests)", MessageTag, TagColor);
                _chatGui.Print("/qst journal - toggles the Journal Progress window", MessageTag, TagColor);
                _chatGui.Print("/qst priority - toggles the Priority window", MessageTag, TagColor);
                _chatGui.Print("/qst mountid - prints information about your current mount", MessageTag, TagColor);
                _chatGui.Print("/qst handle-interrupt - makes Questionable handle queued interrupts immediately (useful if you manually start combat)", MessageTag, TagColor);
                break;

            case "c":
            case "config":
                _configWindow.ToggleOrUncollapse();
                break;

            case "start":
                _questWindow.IsOpenAndUncollapsed = true;
                _questController.Start("Start command");
                break;

            case "stop":
                _movementController.Stop();
                _questController.Stop("Stop command");
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
                _questSelectionWindow.OpenForCurrentZone();
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
                _chatGui.PrintError($"Unknown subcommand {parts[0]}", MessageTag, TagColor);
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
                    _chatGui.Print($"Saved {unlockedUnlockLinks.Count} unlock links to log.", MessageTag, TagColor);

                    List<uint> newUnlockLinks = unlockedUnlockLinks.Except(_previouslyUnlockedUnlockLinks).ToList();
                    if (_previouslyUnlockedUnlockLinks.Count > 0 && newUnlockLinks.Count > 0)
                        _chatGui.Print($"New unlock links: {string.Join(", ", newUnlockLinks)}", MessageTag, TagColor);

                    _previouslyUnlockedUnlockLinks = unlockedUnlockLinks;
                }
                else
                    _chatGui.PrintError("Could not query unlock links.", MessageTag, TagColor);

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

                    _chatGui.Print("Locked taxi stands:", MessageTag, TagColor);
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

                    _chatGui.Print($"Active festivals: {string.Join(", ", activeFestivals)}", MessageTag, TagColor);
                }

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
                _chatGui.PrintError("Please complete the one-time setup first.", MessageTag, TagColor);
            return true;
        }

        return false;
    }

    private void ConfigureDebugOverlay(string[] arguments)
    {
        if (!_debugOverlay.DrawConditions())
        {
            _chatGui.PrintError("You don't have the debug overlay enabled.", MessageTag, TagColor);
            return;
        }

        if (arguments.Length >= 1 && ElementId.TryFromString(arguments[0], out ElementId? questId) && questId != null)
        {
            if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                _debugOverlay.HighlightedQuest = quest.Id;
                _chatGui.Print($"Set highlighted quest to {questId} ({quest.Info.Name}).", MessageTag, TagColor);
            }
            else
                _chatGui.PrintError($"Unknown quest {questId}.", MessageTag, TagColor);
        }
        else
        {
            _debugOverlay.HighlightedQuest = null;
            _chatGui.Print("Cleared highlighted quest.", MessageTag, TagColor);
        }
    }

    private void SetNextQuest(string[] arguments)
    {
        if (arguments.Length >= 1 && ElementId.TryFromString(arguments[0], out ElementId? questId) && questId != null)
        {
            if (_questFunctions.IsQuestLocked(questId))
                _chatGui.PrintError($"Quest {questId} is locked.", MessageTag, TagColor);
            else if (_questRegistry.TryGetQuest(questId, out Quest? quest))
            {
                _questController.SetNextQuest(quest);
                _chatGui.Print($"Set next quest to {questId} ({quest.Info.Name}).", MessageTag, TagColor);
            }
            else
                _chatGui.PrintError($"Unknown quest {questId}.", MessageTag, TagColor);
        }
        else
        {
            _questController.SetNextQuest(null);
            _chatGui.Print("Cleared next quest.", MessageTag, TagColor);
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
                _chatGui.Print($"Simulating quest {questId} ({quest.Info.Name}).", MessageTag, TagColor);
            }
            else
                _chatGui.PrintError($"Unknown quest {questId}.", MessageTag, TagColor);
        }
        else
        {
            _questController.StopSimulate();
            _chatGui.Print("Cleared simulated quest.", MessageTag, TagColor);
        }
    }

    private void PrintMountId()
    {
        ushort? mountId = _gameFunctions.GetMountId();
        if (mountId != null)
        {
            Mount? row = _dataManager.GetExcelSheet<Mount>().GetRowOrDefault(mountId.Value);
            _chatGui.Print(
                $"Mount ID: {mountId}, Name: {row?.Singular}, Obtainable: {(row?.Order == -1 ? "No" : "Yes")}",
                MessageTag, TagColor);
        }
        else
            _chatGui.Print("You are not mounted.", MessageTag, TagColor);
    }

    private void OnLogout(int type, int code) => _previouslyUnlockedUnlockLinks = [];
}
