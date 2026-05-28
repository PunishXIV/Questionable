using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Controller.Steps.Shared;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
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
        if (currentQuest != null)
        {
            DrawQuestNames(currentQuest, currentQuestType);
            QuestProgressInfo? questWork = DrawQuestWork(currentQuest, isMinimized);

            if (_combatController.IsRunning)
                ImGui.TextColored(ImGuiColors.DalamudOrange, "In Combat");
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
        }
        else
        {
            ImGui.Text("No active quest");
            if (!isMinimized)
                ImGui.TextColored(ImGuiColors.DalamudGrey, $"{_questRegistry.Count} quests loaded");

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
            {
                _movementController.Stop();
                _questController.Stop("Manual (no active quest)");
                _gatheringController.Stop("Manual (no active quest)");
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.SortAmountDown))
                _priorityWindow.ToggleOrUncollapse();
        }

#if REPORTING
        if (!_configuration.General.ReportsDisabled)
        {
            Vector4? reportButtonColor = _configuration.General.DismissedReportWarning ? null : ImGuiColors.DalamudRed;
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ExclamationCircle, reportButtonColor))
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
            ImGui.TextUnformatted(
                $"Simulated Quest: {Shorten(currentQuest.Quest.Info.Name)} ({currentQuest.Quest.Id}) / {currentQuest.Sequence} / {currentQuest.Step}");
        }
        else if (currentQuestType == QuestController.ECurrentQuestType.Gathering)
        {
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
            ImGui.TextUnformatted(
                $"Gathering: {Shorten(currentQuest.Quest.Info.Name)} ({currentQuest.Quest.Id}) / {currentQuest.Sequence} / {currentQuest.Step}");
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
                            "This quest is loaded from your 'pluginConfigs\\Questionable\\Quests' directory.\nThis gets loaded even if Questionable ships with a newer/different version of the quest.");
                    }
                }

                ImGui.TextUnformatted(
                    $"Quest: {Shorten(startedQuest.Quest.Info.Name)} ({startedQuest.Quest.Id}) / {startedQuest.Sequence} / {startedQuest.Step}");

                if (startedQuest.Quest.Root.Disabled)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, "Disabled");
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
                        ImGui.Text("Stop Conditions:");
                        ImGui.Separator();

                        // Level stop condition
                        if (hasLevelCondition)
                        {
                            unsafe
                            {
                                int currentLevel = PlayerState.Instance()->CurrentLevel;
                                ImGui.BulletText($"Stop at level {_configuration.Stop.TargetLevel}");
                                if (currentLevel > 0)
                                {
                                    ImGui.SameLine();
                                    if (currentLevel >= _configuration.Stop.TargetLevel)
                                        ImGui.TextColored(ImGuiColors.ParsedGreen, $"(Current: {currentLevel} - Reached!)");
                                    else
                                        ImGui.TextColored(ImGuiColors.ParsedBlue, $"(Current: {currentLevel})");
                                }
                            }
                        }

                        // Quest stop conditions
                        if (hasCompleteQuestConditions)
                        {
                            if (hasLevelCondition)
                                ImGui.Spacing();

                            ImGui.BulletText("Stop after completing any of these quests:");
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

                            ImGui.BulletText("Stop after accepting any of these quests:");
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
                            "Certain priority quest (e.g. class quests) may be started/completed by\nthe plugin prior to continuing, usually at a teleport step.");
                        ImGui.Separator();
                        ImGui.Text("Available priority quests:");
                        if (availablePriorityQuests.Count > 0)
                        {
                            foreach (ElementId questId in availablePriorityQuests)
                            {
                                if (_questRegistry.TryGetQuest(questId, out Quest? quest))
                                    ImGui.BulletText($"{quest.Info.Name} ({questId})");
                            }
                        }
                        else
                            ImGui.BulletText("(none)");

                        if (unavailablePriorityQuests.Count > 0)
                        {
                            ImGui.Text("Unavailable priority quests:");
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
                    $"Next Quest: {Shorten(nextQuest.Quest.Info.Name)} ({nextQuest.Quest.Id}) / {nextQuest.Sequence} / {nextQuest.Step}");
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
                _chatGui.Print($"Copied '{progressText}' to clipboard");
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
                ImGui.TextUnformatted("(Next quest in story line not accepted)");
            else
                ImGui.TextUnformatted("(Not accepted)");
        }

        return questWork;
    }

    private void DrawQuestButtons(QuestController.QuestProgress currentQuest, QuestStep? currentStep,
        QuestProgressInfo? questProgressInfo, bool isMinimized)
    {
        using (ImRaii.Disabled(_questController.IsRunning))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            {
                // if we haven't accepted this quest, mark it as next quest so that we can optionally use aetherytes to travel
                if (questProgressInfo == null)
                    _questController.SetNextQuest(currentQuest.Quest);

                _questController.Start("UI start");
            }

            ImGui.SameLine();

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.StepForward, "Step"))
                _questController.StartSingleStep("UI step");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Execute next step and then stop.");
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
        {
            _movementController.Stop();
            _questController.Stop("UI stop");
            _gatheringController.Stop("UI stop");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Stop all actions now.");

        using (ImRaii.Disabled(!_questController.IsRunning))
        {
            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.FlagCheckered,
                    _questController.StopAfterCurrentQuest ? ImGuiColors.DalamudOrange : null))
                _questController.StopAfterCurrentQuest = !_questController.StopAfterCurrentQuest;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopAfterCurrentQuest
                    ? "Cancel scheduled stop after current quest."
                    : "Stop after the current quest completes.");

            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play,
                    _questController.StopAfterAcceptingCurrentQuest ? ImGuiColors.DalamudOrange : null))
                _questController.StopAfterAcceptingCurrentQuest = !_questController.StopAfterAcceptingCurrentQuest;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopAfterAcceptingCurrentQuest
                    ? "Cancel scheduled stop after accepting current quest."
                    : "Stop after the current quest is accepted.");

            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.MapMarkerAlt,
                    _questController.StopBeforeTeleport ? ImGuiColors.DalamudOrange : null))
                _questController.StopBeforeTeleport = !_questController.StopBeforeTeleport;

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_questController.StopBeforeTeleport
                    ? "Cancel scheduled stop before teleport."
                    : "Stop before the next aetheryte teleport or item use.");
        }

        if (isMinimized)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.RedoAlt))
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
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ArrowCircleRight, "Skip"))
                    {
                        _movementController.Stop();
                        _questController.Skip(currentQuest.Quest.Id, currentQuest.Sequence);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Skip the current step of the quest path.");
                }
            }

            if (_commandManager.Commands.ContainsKey("/questinfo"))
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Atlas))
                    _commandManager.ProcessCommand($"/questinfo {currentQuest.Quest.Id}");

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Show information about '{currentQuest.Quest.Info.Name}' in Quest Map plugin.");
            }

#if DEBUG
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
            {
                (bool success, string filename) = QuestRegistry.OpenEditor(currentQuest.Quest.Info);
                _logger.LogDebug("OpenEditor {Success}: {Filename}", success, filename);
            }
#endif
        }
    }

    private void DrawSimulationControls()
    {
        if (_questController.SimulatedQuest == null)
            return;

        QuestController.QuestProgress? simulatedQuest = _questController.SimulatedQuest;

        ImGui.Separator();
        ImGui.TextColored(ImGuiColors.DalamudRed, "Quest sim active (experimental)");
        ImGui.Text($"Sequence: {simulatedQuest.Sequence}");

        ImGui.BeginDisabled(simulatedQuest.Sequence == 0);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Minus))
        {
            _movementController.Stop();
            _questController.Stop("Sim-");

            byte oldSequence = simulatedQuest.Sequence;
            byte newSequence = simulatedQuest.Quest.Root.QuestSequence
                .Select(x => x.Sequence)
                .LastOrDefault(x => x < oldSequence, byte.MinValue);

            _questController.SimulatedQuest.SetSequence(newSequence);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(simulatedQuest.Sequence >= 255);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            _movementController.Stop();
            _questController.Stop("Sim+");

            byte oldSequence = simulatedQuest.Sequence;
            byte newSequence = simulatedQuest.Quest.Root.QuestSequence
                .Select(x => x.Sequence)
                .FirstOrDefault(x => x > oldSequence, byte.MaxValue);

            simulatedQuest.SetSequence(newSequence);
        }

        ImGui.EndDisabled();

        QuestSequence? simulatedSequence = simulatedQuest.Quest.FindSequence(simulatedQuest.Sequence);
        if (simulatedSequence != null)
        {
            using ImRaii.IdDisposable _ = ImRaii.PushId("SimulatedStep");

            ImGui.Text($"Step: {simulatedQuest.Step} / {simulatedSequence.Steps.Count - 1}");

            ImGui.BeginDisabled(simulatedQuest.Step == 0);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Minus))
            {
                _movementController.Stop();
                _questController.Stop("SimStep-");

                simulatedQuest.SetStep(Math.Min(simulatedQuest.Step - 1,
                    simulatedSequence.Steps.Count - 1));
            }

            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.BeginDisabled(simulatedQuest.Step >= simulatedSequence.Steps.Count);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _movementController.Stop();
                _questController.Stop("SimStep+");

                simulatedQuest.SetStep(
                    simulatedQuest.Step == simulatedSequence.Steps.Count - 1
                        ? 255
                        : (simulatedQuest.Step + 1));
            }

            ImGui.EndDisabled();

            if (ImGui.Button("Skip current task"))
                _questController.SkipSimulatedTask();

            ImGui.SameLine();
            if (ImGui.Button("Clear sim"))
            {
                _questController.StopSimulate();

                _movementController.Stop();
                _questController.Stop("ClearSim");
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
