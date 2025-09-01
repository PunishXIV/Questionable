using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Controller.Steps.Shared;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Questionable.Windows.QuestComponents;

internal sealed partial class ActiveQuestComponent
{
    [GeneratedRegex(@"\s\s+", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MultipleWhitespaceRegex();

    private readonly QuestController _questController;
    private readonly MovementController _movementController;
    private readonly CombatController _combatController;
    private readonly GatheringController _gatheringController;
    private readonly QuestFunctions _questFunctions;
    private readonly ICommandManager _commandManager;
    private readonly Configuration _configuration;
    private readonly QuestRegistry _questRegistry;
    private readonly PriorityWindow _priorityWindow;
    private readonly UiUtils _uiUtils;
    private readonly IChatGui _chatGui;
    private readonly ILogger<ActiveQuestComponent> _logger;

    public ActiveQuestComponent(
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
        _questController = questController;
        _movementController = movementController;
        _combatController = combatController;
        _gatheringController = gatheringController;
        _questFunctions = questFunctions;
        _commandManager = commandManager;
        _configuration = configuration;
        _questRegistry = questRegistry;
        _priorityWindow = priorityWindow;
        _uiUtils = uiUtils;
        _chatGui = chatGui;
        _logger = logger;
    }

    public event EventHandler? Reload;

    public void Draw(bool isMinimized)
    {
        var currentQuestDetails = _questController.CurrentQuestDetails;
        QuestController.QuestProgress? currentQuest = currentQuestDetails?.Progress;
        QuestController.ECurrentQuestType? currentQuestType = currentQuestDetails?.Type;
        if (currentQuest != null)
        {
            DrawQuestNames(currentQuest, currentQuestType);
            var questWork = DrawQuestWork(currentQuest, isMinimized);

            if (_combatController.IsRunning)
                ImGui.TextColored(ImGuiColors.DalamudOrange, "In Combat");
            else if (_questController.CurrentTaskState is { } currentTaskState)
            {
                using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.TextUnformatted(currentTaskState);
            }
            else
            {
                using var _ = ImRaii.Disabled();
                ImGui.TextUnformatted(_questController.DebugState ?? string.Empty);
            }

            try
            {
                QuestSequence? currentSequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
                QuestStep? currentStep = currentSequence?.FindStep(currentQuest.Step);
                if (!isMinimized)
                {
                    using (var color = new ImRaii.Color())
                    {
                        bool colored = currentStep is
                        {
                            InteractionType: EInteractionType.Instruction or EInteractionType.WaitForManualProgress
                            or EInteractionType.Snipe
                        };
                        if (colored)
                            color.Push(ImGuiCol.Text, ImGuiColors.DalamudOrange);

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
    }

    private void DrawQuestNames(QuestController.QuestProgress currentQuest,
        QuestController.ECurrentQuestType? currentQuestType)
    {
        if (currentQuestType == QuestController.ECurrentQuestType.Simulated)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextUnformatted(
                $"Simulated Quest: {Shorten(currentQuest.Quest.Info.Name)} ({currentQuest.Quest.Id}) / {currentQuest.Sequence} / {currentQuest.Step}");
        }
        else if (currentQuestType == QuestController.ECurrentQuestType.Gathering)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
            ImGui.TextUnformatted(
                $"Gathering: {Shorten(currentQuest.Quest.Info.Name)} ({currentQuest.Quest.Id}) / {currentQuest.Sequence} / {currentQuest.Step}");
        }
        else
        {
            var startedQuest = _questController.StartedQuest;
            if (startedQuest != null)
            {
                if (startedQuest.Quest.Source == Quest.ESource.UserDirectory)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.FilePen.ToIconString());
                    ImGui.PopFont();
                    ImGui.SameLine(0);

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(
                            "This quest is loaded from your 'pluginConfigs\\Questionable\\Quests' directory.\nThis gets loaded even if Questionable ships with a newer/different version of the quest.");
                }

                ImGui.TextUnformatted(
                    $"Quest: {Shorten(startedQuest.Quest.Info.Name)} ({startedQuest.Quest.Id}) / {startedQuest.Sequence} / {startedQuest.Step}");

                if (startedQuest.Quest.Root.Disabled)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, "Disabled");
                }

                if (_configuration.Stop.Enabled &&
                    _configuration.Stop.QuestsToStopAfter.Any(x => !_questFunctions.IsQuestComplete(x) && !_questFunctions.IsQuestUnobtainable(x)))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.ParsedPurple, SeIconChar.Clock.ToIconString());
                    if (ImGui.IsItemHovered())
                    {
                        using var tooltip = ImRaii.Tooltip();
                        if (tooltip)
                        {
                            ImGui.Text("Questionable will stop after completing any of the following quests:");
                            foreach (var questId in _configuration.Stop.QuestsToStopAfter)
                            {
                                if (_questRegistry.TryGetQuest(questId, out var quest))
                                {
                                    (Vector4 color, FontAwesomeIcon icon, _) = _uiUtils.GetQuestStyle(questId);
                                    _uiUtils.ChecklistItem($"{quest.Info.Name} ({questId})", color, icon);
                                }
                            }
                        }
                    }
                }

                if (_configuration.Advanced.AdditionalStatusInformation && _questController.IsInterruptible())
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, SeIconChar.Hyadelyn.ToIconString());
                    if (ImGui.IsItemHovered())
                    {
                        using var tooltip = ImRaii.Tooltip();
                        if (tooltip)
                        {
                            ImGui.Text("This quest sequence starts with a teleport to an Aetheryte.");
                            ImGui.Text(
                                "Certain priority quest (e.g. class quests) may be started/completed by the plugin prior to continuing with this quest.");
                            ImGui.Separator();
                            ImGui.Text("Available priority quests:");

                            List<PriorityQuestInfo> priorityQuests = _questFunctions.GetNextPriorityQuestsThatCanBeAccepted();
                            var availablePriorityQuests = priorityQuests
                                .Where(x => x.IsAvailable)
                                .Select(x => x.QuestId)
                                .ToList();
                            if (availablePriorityQuests.Count > 0)
                            {
                                foreach (var questId in availablePriorityQuests)
                                {
                                    if (_questRegistry.TryGetQuest(questId, out var quest))
                                        ImGui.BulletText($"{quest.Info.Name} ({questId})");
                                }
                            }
                            else
                                ImGui.BulletText("(none)");

                            if (_configuration.Advanced.AdditionalStatusInformation)
                            {
                                var unavailablePriorityQuests = priorityQuests
                                    .Where(x => !x.IsAvailable)
                                    .ToList();
                                if (unavailablePriorityQuests.Count > 0)
                                {
                                    ImGui.Text("Unavailable priority quests:");
                                    foreach (var (questId, reason) in unavailablePriorityQuests)
                                    {
                                        if (_questRegistry.TryGetQuest(questId, out var quest))
                                            ImGui.BulletText($"{quest.Info.Name} ({questId}) - {reason}");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var nextQuest = _questController.NextQuest;
            if (nextQuest != null)
            {
                using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextUnformatted(
                    $"Next Quest: {Shorten(nextQuest.Quest.Info.Name)} ({nextQuest.Quest.Id}) / {nextQuest.Sequence} / {nextQuest.Step}");
            }
        }
    }

    private QuestProgressInfo? DrawQuestWork(QuestController.QuestProgress currentQuest, bool isMinimized)
    {
        var questWork = _questFunctions.GetQuestProgressInfo(currentQuest.Quest.Id);

        if (questWork != null)
        {
            if (isMinimized)
                return questWork;


            Vector4 color;
            unsafe
            {
                var ptr = ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled);
                if (ptr != null)
                    color = *ptr;
                else
                    color = ImGuiColors.ParsedOrange;
            }

            using var styleColor = ImRaii.PushColor(ImGuiCol.Text, color);
            ImGui.Text($"{questWork}");

            if (ImGui.IsItemClicked())
            {
                string progressText = MultipleWhitespaceRegex().Replace(questWork.ToString(), " ");
                ImGui.SetClipboardText(progressText);
                _chatGui.Print($"Copied '{progressText}' to clipboard");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Copy.ToIconString());
                ImGui.PopFont();
            }

            if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            {
                ImGui.SameLine();
                ImGui.Text($"/ {questWork.ClassJob}");
            }
        }
        else if (currentQuest.Quest.Id is QuestId)
        {
            using var disabled = ImRaii.Disabled();

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

            if (!isMinimized)
            {
                ImGui.SameLine();

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.StepForward, "Step"))
                {
                    _questController.StartSingleStep("UI step");
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Stop))
        {
            _movementController.Stop();
            _questController.Stop("UI stop");
            _gatheringController.Stop("UI stop");
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
                           && _questController.HasCurrentTaskMatching<WaitAtEnd.WaitNextStepOrSequence>(out _);

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
        }
    }

    private void DrawSimulationControls()
    {
        if (_questController.SimulatedQuest == null)
            return;

        var simulatedQuest = _questController.SimulatedQuest;

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

        var simulatedSequence = simulatedQuest.Quest.FindSequence(simulatedQuest.Sequence);
        if (simulatedSequence != null)
        {
            using var _ = ImRaii.PushId("SimulatedStep");

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
            {
                _questController.SkipSimulatedTask();
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear sim"))
            {
                _questController.SimulateQuest(null, 0, 0);

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
