using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.PathData;
using Questionable.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.QuestComponents;

internal sealed partial class ActiveQuestComponent
(
    QuestController questController,
    MovementController movementController,
    CombatController combatController,
    GatheringController gatheringController,
    QuestFunctions questFunctions,
    ICommandManager commandManager,
    Configuration configuration,
    QuestRegistry questRegistry,
    PriorityWindow priorityWindow,
    UiUtils uiUtils,
    IChatGui chatGui,
    PathDataUpdater pathDataUpdater,
    ILogger<ActiveQuestComponent> logger)
{
    private readonly IChatGui _chatGui = chatGui;
    private readonly CombatController _combatController = combatController;
    private readonly ICommandManager _commandManager = commandManager;
    private readonly Configuration _configuration = configuration;
    private readonly GatheringController _gatheringController = gatheringController;
    private readonly ILogger<ActiveQuestComponent> _logger = logger;
    private readonly MovementController _movementController = movementController;
    private readonly PriorityWindow _priorityWindow = priorityWindow;

    private readonly QuestController _questController = questController;
    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly UiUtils _uiUtils = uiUtils;
    [GeneratedRegex(@"\s\s+", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MultipleWhitespaceRegex();

    public event EventHandler? Reload;

    public void Draw(bool isMinimized)
    {
        (QuestController.QuestProgress Progress, QuestController.ECurrentQuestType Type)? currentQuestDetails = _questController.CurrentQuestDetails;
        QuestController.QuestProgress? currentQuest = currentQuestDetails?.Progress;
        QuestController.ECurrentQuestType? currentQuestType = currentQuestDetails?.Type;
        if (pathDataUpdater.WaitingForPluginUpdate)
        {
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.Text(_L("New version available!"));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                _commandManager.ProcessCommand("/xlplugins");
        }
        if (currentQuest != null)
        {
            DrawQuestNames(currentQuest, currentQuestType);
            QuestProgressInfo? questWork = DrawQuestWork(currentQuest, isMinimized);

            if (_combatController.IsRunning)
                ImGui.TextColored(ImGuiColors.DalamudOrange, _L("In Combat"));
            else if (_questController.CurrentTaskState is { } currentTaskState)
            {
                using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.TextUnformatted(currentTaskState);
            }
            else
            {
                using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                ImGui.TextUnformatted(_questController.DebugState ?? string.Empty);
            }

            try
            {
                QuestSequence? currentSequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
                QuestStep? currentStep = currentSequence?.FindStep(currentQuest.Step);
                if (!isMinimized)
                {
                    using (ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, currentStep is { InteractionType: EInteractionType.Instruction or EInteractionType.WaitForManualProgress or EInteractionType.Snipe }))
                    {
                        ImGui.TextUnformatted(currentStep?.Comment ??
                                              currentSequence?.Comment ??
                                              currentQuest.Quest.Root.Comment ?? string.Empty);
                    }

                    //var nextStep = _questController.GetNextStep();
                    //ImGui.BeginDisabled(nextStep.Step == null);
                    ImGui.Text(_questController.ToStatString());
                    //ImGui.EndDisabled();
                }

                DrawQuestButtons(currentQuest, currentStep, questWork, isMinimized);
            }
            catch (Exception e)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, e.ToString());
                _logger.LogError(e, "Could not handle active quest buttons");
            }

            DrawSimulationControls();

            ImGui.SameLine();
            bool editButtonLeft = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit);
            bool editButtonRight = ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if (editButtonLeft)
            {
                (bool success, string filename) = currentQuest != null ? QuestRegistry.OpenEditor(currentQuest.Quest.Info) : _questRegistry.OpenEditor();
                _logger.LogDebug("OpenEditor {Success}: {Filename}", success, filename);
            }
            else if (editButtonRight)
            {
                QuestRegistry.OpenFolder();
                _logger.LogDebug("OpenFolder executed");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Clicking this button writes the quest path to a file and opens it in your default\n" +
                                    "text editor. After making a change, click Reload Data below. To revert to the\n" +
                                    "official version, delete the file and click Reload Data again.\n" +
                                    "Left click: Open this quest in your default .json text editor\n" +
                                    "Right click: Open Quests folder"));
            if (_configuration.Advanced.Debug)
            {
                ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Ban) && currentQuest != null)
                {
                    GameMain.ExecuteCommand((int)GameCommand.AbandonQuest, (int)currentQuest.Quest.Id.Value);
                    _logger.LogDebug("AbandonQuest fired");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Ask the game to abandon this quest");
            }
        }
        else
        {
            if (pathDataUpdater.Status != _L("Idle") && (DateTime.Now - pathDataUpdater.StatusLastChanged).TotalSeconds < 30 )
                ImGui.Text(pathDataUpdater.Status);
            else
                ImGui.Text(_L("No active quest"));
            if (!isMinimized)
            {
                var color = ImGuiColors.DalamudGrey;
                if (_questRegistry.Count < 500)
                    color = ImGuiColors.DalamudRed;
                ImGui.TextColored(color, _LF("{0} quests loaded", _questRegistry.Count));
                if (ImGui.IsItemClicked())
                {
                    _configuration.PathData.InstalledDataVersion = 0;
                    pathDataUpdater.CheckForUpdatesManually();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Click to reload quest path data from server"));
            }

            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Stop))
            {
                _movementController.Stop();
                _questController.Stop(_L("Manual (no active quest)"));
                _gatheringController.Stop(_L("Manual (no active quest)"));
            }

            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.SortAmountDown))
                _priorityWindow.ToggleOrUncollapse();
        }

#if REPORTING
        if (!_configuration.General.ReportsDisabled)
        {
            Vector4? reportButtonColor = _configuration.General.DismissedReportWarning ? null : ImGuiColors.DalamudRed;
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle, reportButtonColor))
            {
                // TODO report
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Report issue to QST developers");
        }
#endif
    }

    private void DrawQuestNames(QuestController.QuestProgress currentQuest,
        QuestController.ECurrentQuestType? currentQuestType)
    {
        if (currentQuestType == QuestController.ECurrentQuestType.Simulated)
        {
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextUnformatted(_L("Simulated Quest: ") +
                _LF("{0} ({1}) / {2} / {3}",
                    Shorten(currentQuest.Quest.Info.Name),
                    currentQuest.Quest.Id,
                    currentQuest.Sequence,
                    currentQuest.Step));
        }
        else if (currentQuestType == QuestController.ECurrentQuestType.Gathering)
        {
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
            ImGui.TextUnformatted(_LF("Gathering: ") +
                _LF("{0} ({1}) / {2} / {3}",
                    Shorten(currentQuest.Quest.Info.Name),
                    currentQuest.Quest.Id,
                    currentQuest.Sequence,
                    currentQuest.Step));
        }
        else
        {
            QuestController.QuestProgress? startedQuest = _questController.StartedQuest;
            if (startedQuest != null)
            {
                if (startedQuest.Quest.Source == Quest.ESource.UserDirectory)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.FilePen.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine(0);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            _L("This quest is loaded from your 'pluginConfigs\\Questionable\\Quests' directory.\nThis gets loaded even if Questionable ships with a newer/different version of the quest."));
                    }
                }

                ImGui.TextUnformatted(_L($"Quest: ") +
                    _LF("{0} ({1}) / {2} / {3}",
                        Shorten(currentQuest.Quest.Info.Name),
                        currentQuest.Quest.Id,
                        currentQuest.Sequence,
                        currentQuest.Step));

                if (startedQuest.Quest.Root.Disabled)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, _L("Disabled"));
                }

                bool hasLevelCondition = _configuration.Stop.Enabled && _configuration.Stop.LevelToStopAfter;
                bool hasCompleteQuestConditions = _configuration.Stop.Enabled &&
                                                  _configuration.Stop.QuestsToStopAfter.Any(x =>
                                                      !_questFunctions.IsQuestComplete(x) &&
                                                      !_questFunctions.IsQuestUnobtainable(x));
                bool hasAcceptQuestConditions = _configuration.Stop.Enabled &&
                                                _configuration.Stop.QuestsToStopWhenAccepted.Any(x =>
                                                    !_questFunctions.IsQuestAcceptedOrComplete(x) &&
                                                    !_questFunctions.IsQuestUnobtainable(x));

                if (hasLevelCondition || hasCompleteQuestConditions || hasAcceptQuestConditions)
                {
                    ImGui.SameLine();

                    // Tooltip color based on status
                    Vector4 iconColor = ImGuiColors.ParsedPurple;

                    if (hasLevelCondition)
                    {
                        unsafe
                        {
                            short currentLevel = PlayerState.Instance()->CurrentLevel;
                            if (currentLevel > 0 && currentLevel >= _configuration.Stop.TargetLevel)
                                iconColor = ImGuiColors.ParsedGreen;
                            else if (currentLevel > 0)
                                iconColor = ImGuiColors.ParsedBlue;
                        }
                    }

                    ImGui.TextColored(iconColor, SeIconChar.Clock.ToIconString());
                    if (ImGui.IsItemHovered())
                    {
                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                        ImGui.Text(_L("Stop Conditions:"));
                        ImGui.Separator();

                        // Level stop condition
                        if (hasLevelCondition)
                        {
                            unsafe
                            {
                                int currentLevel = PlayerState.Instance()->CurrentLevel;
                                ImGui.BulletText(_LF("Stop at level {0}", _configuration.Stop.TargetLevel));
                                if (currentLevel > 0)
                                {
                                    ImGui.SameLine();
                                    if (currentLevel >= _configuration.Stop.TargetLevel)
                                        ImGui.TextColored(ImGuiColors.ParsedGreen, _LF("(Current: {0} - Reached!)", currentLevel));
                                    else
                                        ImGui.TextColored(ImGuiColors.ParsedBlue, _LF("(Current: {0})", currentLevel));
                                }
                            }
                        }

                        // Quest stop conditions
                        if (hasCompleteQuestConditions)
                        {
                            if (hasLevelCondition)
                                ImGui.Spacing();

                            ImGui.BulletText(_L("Stop after completing any of these quests:"));
                            ImGui.Indent();
                            foreach (ElementId questId in _configuration.Stop.QuestsToStopAfter)
                            {
                                if (_questRegistry.TryGetQuest(questId, out Quest? quest))
                                {
                                    (Vector4 color, FontAwesomeIcon icon, string _) = _uiUtils.GetQuestStyle(questId);
                                    _uiUtils.ChecklistItem($"{quest.Info.Name} ({questId})", color, icon);
                                }
                            }

                            ImGui.Unindent();
                        }

                        if (hasAcceptQuestConditions)
                        {
                            if (hasLevelCondition || hasCompleteQuestConditions)
                                ImGui.Spacing();

                            ImGui.BulletText(_L("Stop after accepting any of these quests:"));
                            ImGui.Indent();
                            foreach (ElementId questId in _configuration.Stop.QuestsToStopWhenAccepted)
                            {
                                if (_questRegistry.TryGetQuest(questId, out Quest? quest))
                                {
                                    (Vector4 color, FontAwesomeIcon icon, string _) = _uiUtils.GetQuestStyle(questId);
                                    _uiUtils.ChecklistItem($"{quest.Info.Name} ({questId})", color, icon);
                                }
                            }

                            ImGui.Unindent();
                        }
                    }
                }


                List<PriorityQuestInfo> priorityQuests = _questFunctions.GetNextPriorityQuestsThatCanBeAccepted();
                List<ElementId> availablePriorityQuests = priorityQuests
                    .Where(x => x.IsAvailable)
                    .Select(x => x.QuestId)
                    .ToList();
                List<PriorityQuestInfo> unavailablePriorityQuests = priorityQuests
                    .Where(x => !x.IsAvailable)
                    .ToList();
                if (availablePriorityQuests.Count > 0 || unavailablePriorityQuests.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, SeIconChar.Hyadelyn.ToIconString());
                    if (ImGui.IsItemHovered())
                    {
                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                        ImGui.Text(
                            _L("Certain priority quest (e.g. class quests) may be started/completed by\nthe plugin prior to continuing, usually at a teleport step."));
                        ImGui.Separator();
                        ImGui.Text(_L("Available priority quests:"));
                        if (availablePriorityQuests.Count > 0)
                        {
                            foreach (ElementId questId in availablePriorityQuests)
                            {
                                if (_questRegistry.TryGetQuest(questId, out Quest? quest))
                                    ImGui.BulletText($"{quest.Info.Name} ({questId})");
                            }
                        }
                        else
                            ImGui.BulletText(_L("(none)"));

                        if (unavailablePriorityQuests.Count > 0)
                        {
                            ImGui.Text(_L("Unavailable priority quests:"));
                            foreach ((ElementId questId, string? reason) in unavailablePriorityQuests)
                            {
                                if (_questRegistry.TryGetQuest(questId, out Quest? quest))
                                    ImGui.BulletText($"{quest.Info.Name} ({questId}) - {reason}");
                            }
                        }
                    }
                }
            }

            QuestController.QuestProgress? nextQuest = _questController.NextQuest;
            if (nextQuest != null)
            {
                using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextUnformatted(
                    _L("Next Quest: ") +
                    _LF("{0} ({1}) / {2} / {3}",
                        Shorten(currentQuest.Quest.Info.Name),
                        currentQuest.Quest.Id,
                        currentQuest.Sequence,
                        currentQuest.Step));
            }
        }
    }

    private QuestProgressInfo? DrawQuestWork(QuestController.QuestProgress currentQuest, bool isMinimized)
    {
        QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(currentQuest.Quest.Id);

        if (questWork != null)
        {
            if (isMinimized)
                return questWork;


            Vector4 color;
            unsafe
            {
                Vector4* ptr = ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled);
                if (ptr != null)
                    color = *ptr;
                else
                    color = ImGuiColors.ParsedOrange;
            }

            using ImRaii.ColorDisposable styleColor = ImRaii.PushColor(ImGuiCol.Text, color);
            ImGui.Text($"{questWork}");

            if (ImGui.IsItemClicked())
            {
                string progressText = MultipleWhitespaceRegex().Replace(questWork.ToString(), " ");
                ImGui.SetClipboardText(progressText);
                _chatGui.Print(_LF("Copied '{0}' to clipboard", progressText));
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(questWork.Tooltip);
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Copy.ToIconString());
                ImGui.PopFont();
            }

            if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            {
                ImGui.SameLine();
                ImGui.Text($"/ {questWork.ClassJob} {currentQuest.Quest.Info.AlliedSociety.ToString()}");
            }
        }
        else if (currentQuest.Quest.Id is QuestId)
        {
            using ImRaii.DisabledDisposable disabled = ImRaii.Disabled();

            if (currentQuest.Quest.Id == _questController.NextQuest?.Quest.Id)
                ImGui.TextUnformatted(_L("(Next quest in story line not accepted)"));
            else
                ImGui.TextUnformatted(_L("(Not accepted)"));
        }

        return questWork;
    }

    private void DrawQuestButtons(QuestController.QuestProgress currentQuest, QuestStep? currentStep,
        QuestProgressInfo? questProgressInfo, bool isMinimized)
    {
        using (ImRaii.Disabled(_questController.IsRunning))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play))
            {
                // if we haven't accepted this quest, mark it as next quest so that we can optionally use aetherytes to travel
                if (questProgressInfo == null)
                    _questController.SetNextQuest(currentQuest.Quest);

                _questController.Start(_L("UI start"));
            }

            ImGui.SameLine();

            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.StepForward, _L("Step")))
                _questController.StartSingleStep(_L("UI step"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Execute next step and then stop."));
        }

        ImGui.SameLine();

        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Stop))
        {
            _movementController.Stop();
            _questController.Stop(_L("UI stop"));
            _gatheringController.Stop(_L("UI stop"));
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Stop all actions now."));

        using (ImRaii.Disabled(!_questController.IsRunning))
        {
            ImGui.SameLine();

            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.FlagCheckered,
                    _questController.StopAfterCurrentQuest ? ImGuiColors.DalamudOrange : null))
                _questController.StopAfterCurrentQuest = !_questController.StopAfterCurrentQuest;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopAfterCurrentQuest
                    ? _L("Cancel scheduled stop after current quest.")
                    : _L("Stop after the current quest completes."));

            ImGui.SameLine();

            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Check,
                    _questController.StopAfterAcceptingNextQuest ? ImGuiColors.DalamudOrange : null))
                _questController.StopAfterAcceptingNextQuest = !_questController.StopAfterAcceptingNextQuest;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopAfterAcceptingNextQuest
                    ? _L("Cancel scheduled stop after accepting the next quest.")
                    : _L("Stop after accepting the next quest."));

            ImGui.SameLine();

            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MapMarkerAlt,
                    _questController.StopBeforeTeleport ? ImGuiColors.DalamudOrange : null))
                _questController.StopBeforeTeleport = !_questController.StopBeforeTeleport;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopBeforeTeleport
                    ? _L("Cancel scheduled stop before teleport.")
                    : _L("Stop before the next aetheryte teleport or item use."));
        }

        if (isMinimized)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.RedoAlt))
                Reload?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            bool lastStep = currentStep ==
                            currentQuest.Quest.FindSequence(currentQuest.Sequence)?.Steps.LastOrDefault();
            bool colored = currentStep != null
                           && !lastStep
                           && currentStep.InteractionType == EInteractionType.Instruction
                           && _questController.HasCurrentTaskMatching(out WaitAtEnd.WaitNextStepOrSequence? _);

            using (ImRaii.Disabled(lastStep))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen, colored))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.ArrowCircleRight, _L("Skip")))
                    {
                        _movementController.Stop();
                        _questController.Skip(currentQuest.Quest.Id, currentQuest.Sequence);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(_L("Skip the current step of the quest path."));
                }
            }

            if (_commandManager.Commands.ContainsKey("/questinfo"))
            {
                ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Atlas))
                    _commandManager.ProcessCommand($"/questinfo {currentQuest.Quest.Id}");

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_LF("Show information about '{0}' in Quest Map plugin.", currentQuest.Quest.Info.Name));
            }
        }
    }

    private void DrawSimulationControls()
    {
        if (_questController.SimulatedQuest == null)
            return;

        QuestController.QuestProgress? simulatedQuest = _questController.SimulatedQuest;

        ImGui.Separator();
        ImGui.TextColored(ImGuiColors.DalamudRed, _L("Quest sim active (experimental)"));
        ImGui.Text(_LF("Sequence: {0}", simulatedQuest.Sequence));

        using (ImRaii.Disabled(simulatedQuest.Sequence == 0))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Minus))
            {
                _movementController.Stop();
                _questController.Stop("Sim-");

                byte oldSequence = simulatedQuest.Sequence;
                byte newSequence = simulatedQuest.Quest.Root.QuestSequence
                    .Select(x => x.Sequence)
                    .LastOrDefault(x => x < oldSequence, byte.MinValue);

                _questController.SimulatedQuest.SetSequence(newSequence);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(simulatedQuest.Sequence >= 255))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Plus))
            {
                _movementController.Stop();
                _questController.Stop("Sim+");

                byte oldSequence = simulatedQuest.Sequence;
                byte newSequence = simulatedQuest.Quest.Root.QuestSequence
                    .Select(x => x.Sequence)
                    .FirstOrDefault(x => x > oldSequence, byte.MaxValue);

                simulatedQuest.SetSequence(newSequence);
            }
        }

        QuestSequence? simulatedSequence = simulatedQuest.Quest.FindSequence(simulatedQuest.Sequence);
        if (simulatedSequence != null)
        {
            using ImRaii.IdDisposable _ = ImRaii.PushId("SimulatedStep");

            ImGui.Text(_LF("Step: {0} / {1}", simulatedQuest.Step, simulatedSequence.Steps.Count - 1));

            using (ImRaii.Disabled(simulatedQuest.Step == 0))
            {
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Minus))
                {
                    _movementController.Stop();
                    _questController.Stop("SimStep-");

                    simulatedQuest.SetStep(Math.Min(simulatedQuest.Step - 1,
                        simulatedSequence.Steps.Count - 1));
                }
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(simulatedQuest.Step >= simulatedSequence.Steps.Count))
            {
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Plus))
                {
                    _movementController.Stop();
                    _questController.Stop("SimStep+");

                    simulatedQuest.SetStep(
                        simulatedQuest.Step == simulatedSequence.Steps.Count - 1
                            ? 255
                            : (simulatedQuest.Step + 1));
                }
            }

            if (ImGui.Button(_L("Skip current task")))
                _questController.SkipSimulatedTask();

            ImGui.SameLine();
            if (ImGui.Button(_L("Clear sim")))
            {
                _questController.StopSimulate();

                _movementController.Stop();
                _questController.Stop(_L("Clear sim"));
            }
        }
    }

    private static string Shorten(string text)
    {
        if (text.Length > 35)
            return string.Concat(text.AsSpan(0, 30).Trim(), ((SeIconChar)57434).ToIconString());

        return text;
    }
}
