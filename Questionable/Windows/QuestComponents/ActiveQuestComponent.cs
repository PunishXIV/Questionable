using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.QuestComponents;

// TODO: refactor — heavy nesting (43 lines indented ≥6 levels, max indent ~13 levels).
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
    PathEditorWindow pathEditorWindow,
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
    private readonly PathEditorWindow _pathEditorWindow = pathEditorWindow;

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
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
            ImGui.Text(_L("New version available!"));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                _commandManager.ProcessCommand("/xlplugins");
        }
        if (currentQuest != null)
        {
            DrawQuestNames(currentQuest, currentQuestType);
            if (!isMinimized)
                QstWidgets.ThinProgressBar(CalculateQuestProgress(currentQuest),
                    _questController.IsRunning ? QstTheme.Success : QstTheme.Amber);
            QuestProgressInfo? questWork = DrawQuestWork(currentQuest, isMinimized);

            if (_combatController.IsRunning)
                ImGui.TextColored(QstTheme.Accent, _L("In Combat"));
            else if (_questController.CurrentTaskState is { } currentTaskState)
            {
                using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
                using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                ImGui.TextUnformatted(currentTaskState);
            }
            else
            {
                using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                ImGui.TextUnformatted(_questController.DebugState ?? string.Empty);
            }

            try
            {
                QuestSequence? currentSequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
                QuestStep? currentStep = currentSequence?.FindStep(currentQuest.Step);
                if (!isMinimized)
                {
                    string comment = currentStep?.Comment ??
                                     currentSequence?.Comment ??
                                     currentQuest.Quest.Root.Comment ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(comment))
                    {
                        bool manualStep = currentStep is { InteractionType: EInteractionType.Instruction or EInteractionType.WaitForManualProgress or EInteractionType.Snipe };
                        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.Text, manualStep ? QstTheme.Accent : QstTheme.TextMuted);
                        using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                        ImGui.TextUnformatted(comment);
                    }

                    string stats = _questController.ToStatString();
                    if (!string.IsNullOrWhiteSpace(stats))
                    {
                        using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                        using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                        ImGui.TextUnformatted(stats);
                    }
                }

                var builtNavmeshPercent = _movementController.BuiltNavmeshPercent;
                var navmeshBarColor = QstTheme.Success;
                if (builtNavmeshPercent > 100)
                {
                    navmeshBarColor = QstTheme.Amber;
                    builtNavmeshPercent %= builtNavmeshPercent;
                }
                if (builtNavmeshPercent == 0)
                    builtNavmeshPercent = 100;
                QstWidgets.ThinProgressBar((float)builtNavmeshPercent / 100, navmeshBarColor);

                DrawQuestButtons(currentQuest, currentStep, questWork, isMinimized);
            }
            catch (Exception e)
            {
                using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Danger);
                using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                ImGui.TextUnformatted(e.ToString());
                _logger.LogError(e, "Could not handle active quest buttons");
            }

            DrawSimulationControls();

            if (_configuration.Advanced.Debug)
            {
                ImGui.SameLine();
                bool editButtonLeft = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit);
                bool editButtonRight = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (editButtonLeft)
                    _pathEditorWindow.Open(currentQuest.Quest.Id);
                else if (editButtonRight)
                {
                    QuestRegistry.OpenFolder();
                    _logger.LogDebug("OpenFolder executed");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Left click: Open in Path Editor\nRight click: Open Quests folder"));
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
            if (pathDataUpdater.Status != _L("Idle") && (DateTime.Now - pathDataUpdater.StatusLastChanged).TotalSeconds < 30)
                ImGui.Text(pathDataUpdater.Status);
            else
                ImGui.Text(_L("No active quest"));
            if (!isMinimized)
            {
                var color = QstTheme.TextMuted;
                if (_questRegistry.Count < 500)
                    color = QstTheme.Danger;
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
            Vector4? reportButtonColor = _configuration.General.DismissedReportWarning ? null : QstTheme.Danger;
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
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Danger);
            ImGui.TextUnformatted(_L("Simulated Quest: ") +
                _LF("{0} ({1}) / {2} / {3}",
                    Shorten(currentQuest.Quest.Info.Name),
                    currentQuest.Quest.Id,
                    currentQuest.Sequence,
                    currentQuest.Step));
        }
        else if (currentQuestType == QuestController.ECurrentQuestType.Gathering)
        {
            using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Amber);
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
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.TextColored(QstTheme.Danger, FontAwesomeIcon.FilePen.ToIconString());
                    }

                    ImGui.SameLine(0);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            _L("This quest is loaded from your 'pluginConfigs\\Questionable\\Quests' directory. This gets loaded even if Questionable ships with a newer/different version of the quest."));
                    }
                }

                ImGui.TextUnformatted(_L("Quest: ") + Shorten(currentQuest.Quest.Info.Name));
                ImGui.SameLine();
                QstWidgets.Chip($"#{currentQuest.Quest.Id}", QstTheme.Info);

                if (startedQuest.Quest.Root.Disabled)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(QstTheme.Danger, _L("Disabled"));
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

                List<PriorityQuestInfo> priorityQuests = _questFunctions.NextPriorityQuestsThatCanBeAccepted;
                bool anyAvailable = false;
                bool anyUnavailable = false;
                foreach (var p in priorityQuests)
                {
                    if (p.IsAvailable) anyAvailable = true;
                    else anyUnavailable = true;
                    if (anyAvailable && anyUnavailable) break;
                }

                bool showStopClock = hasLevelCondition || hasCompleteQuestConditions || hasAcceptQuestConditions;
                bool showPriorityCrystal = anyAvailable || anyUnavailable;
                if (showStopClock || showPriorityCrystal)
                    ImGui.SameLine();

                if (showStopClock)
                {
                    // Tooltip color based on status
                    Vector4 iconColor = QstTheme.Special;

                    if (hasLevelCondition)
                    {
                        unsafe
                        {
                            short currentLevel = PlayerState.Instance()->CurrentLevel;
                            if (currentLevel > 0 && currentLevel >= _configuration.Stop.TargetLevel)
                                iconColor = QstTheme.Success;
                            else if (currentLevel > 0)
                                iconColor = QstTheme.Info;
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
                                        ImGui.TextColored(QstTheme.Success, _LF("(Current: {0} - Reached!)", currentLevel));
                                    else
                                        ImGui.TextColored(QstTheme.Info, _LF("(Current: {0})", currentLevel));
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


                if (showPriorityCrystal)
                {
                    if (showStopClock)
                        ImGui.SameLine();
                    ImGui.TextColored(QstTheme.Amber, SeIconChar.Hyadelyn.ToIconString());
                    if (ImGui.IsItemHovered())
                    {
                        List<ElementId> availablePriorityQuests = priorityQuests
                            .Where(x => x.IsAvailable)
                            .Select(x => x.QuestId)
                            .ToList();
                        List<PriorityQuestInfo> unavailablePriorityQuests = priorityQuests
                            .Where(x => !x.IsAvailable)
                            .ToList();
                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                        ImGui.Text(
                            _L("Certain priority quest (e.g. class quests) may be started/completed by the plugin prior to continuing, usually at a teleport step."));
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

                QuestSequence? metaSequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
                using (ImRaii.Disabled())
                {
                    ImGui.TextUnformatted(_LF("Seq {0} · Step {1}/{2}",
                        currentQuest.Sequence, currentQuest.Step, metaSequence?.Steps.Count ?? 0));
                }

                if (metaSequence?.FindStep(currentQuest.Step) is { } metaStep)
                {
                    QstWidgets.Chip(metaStep.InteractionType.ToString(), QstTheme.Accent);
                    if (metaStep.DataId is { } metaDataId)
                    {
                        ImGui.SameLine();
                        QstWidgets.Chip(metaDataId.ToString(CultureInfo.InvariantCulture), QstTheme.TextMuted);
                    }
                }
            }

            QuestController.QuestProgress? nextQuest = _questController.NextQuest;
            if (nextQuest != null)
            {
                using ImRaii.ColorDisposable _ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Amber);
                ImGui.TextUnformatted(
                    _L("Next Quest: ") +
                    _LF("{0} ({1}) / {2} / {3}",
                        Shorten(nextQuest.Quest.Info.Name),
                        nextQuest.Quest.Id,
                        nextQuest.Sequence,
                        nextQuest.Step));
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
                    color = QstTheme.Accent;
            }

            using ImRaii.ColorDisposable styleColor = ImRaii.PushColor(ImGuiCol.Text, color);
            using (ImRaii.TextWrapPos(0))
                ImGui.TextUnformatted($"{questWork}");

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
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.Text(FontAwesomeIcon.Copy.ToIconString());
                }
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
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play, QstTheme.Accent))
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

            bool anyStopArmed = _questController.StopAfterCurrentQuest
                                || _questController.StopAfterAcceptingNextQuest
                                || _questController.StopBeforeTeleport;
            using (QstWidgets.SegmentGroup(anyStopArmed))
            {
                if (QstWidgets.SegmentToggle(FontAwesomeIcon.FlagCheckered, _questController.StopAfterCurrentQuest,
                        _L("Cancel scheduled stop after current quest."),
                        _L("Stop after the current quest completes.")))
                    _questController.StopAfterCurrentQuest = !_questController.StopAfterCurrentQuest;

                ImGui.SameLine();

                if (QstWidgets.SegmentToggle(FontAwesomeIcon.Check, _questController.StopAfterAcceptingNextQuest,
                        _L("Cancel scheduled stop after accepting the next quest."),
                        _L("Stop after accepting the next quest.")))
                    _questController.StopAfterAcceptingNextQuest = !_questController.StopAfterAcceptingNextQuest;

                if (!isMinimized)
                    ImGui.SameLine();

                if (QstWidgets.SegmentToggle(FontAwesomeIcon.MapMarkerAlt, _questController.StopBeforeTeleport,
                        _L("Cancel scheduled stop before teleport."),
                        _L("Stop before the next aetheryte teleport or item use.")))
                    _questController.StopBeforeTeleport = !_questController.StopBeforeTeleport;
            }
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
                using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.Success, colored))
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
        ImGui.TextColored(QstTheme.Danger, _L("Quest sim active (experimental)"));
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

    public void DrawTitleBarPill(string windowTitle)
    {
        if (_combatController.IsRunning)
            QstWidgets.TitleBarPill(_L("Combat"), QstTheme.Accent, windowTitle);
        else if (_questController.IsRunning
                 && (_questController.StopAfterCurrentQuest
                     || _questController.StopAfterAcceptingNextQuest
                     || _questController.StopBeforeTeleport))
            QstWidgets.TitleBarPill(_L("Stopping"), QstTheme.Amber, windowTitle);
        else if (_questController.IsRunning)
            QstWidgets.TitleBarPill(_L("Running"), QstTheme.Success, windowTitle);
        else
            QstWidgets.TitleBarPill(_L("Idle"), QstTheme.TextMuted, windowTitle);
    }

    private static float CalculateQuestProgress(QuestController.QuestProgress progress)
    {
        List<QuestSequence> sequences = progress.Quest.Root.QuestSequence;
        int totalSteps = sequences.Sum(x => x.Steps.Count);
        if (totalSteps == 0)
            return 0f;

        int currentSequenceSteps = sequences.FirstOrDefault(x => x.Sequence == progress.Sequence)?.Steps.Count ?? 0;
        int doneSteps = sequences.Where(x => x.Sequence < progress.Sequence).Sum(x => x.Steps.Count)
                        + Math.Min(progress.Step, currentSequenceSteps);
        return Math.Clamp(doneSteps / (float)totalSteps, 0f, 1f);
    }

    private static string Shorten(string text)
    {
        if (text.Length > 30)
            return string.Concat(text.AsSpan(0, 25).Trim(), ((SeIconChar)57434).ToIconString());

        return text;
    }
}
