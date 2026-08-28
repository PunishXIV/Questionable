using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Questionable.Windows.QuestComponents;

[RegisterSingleton]
internal sealed class CreationUtilsComponent
(
    QuestController questController,
    MovementController movementController,
    GameFunctions gameFunctions,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    CameraFunctions cameraFunctions,
    QuestData questData,
    QuestSelectionWindow questSelectionWindow,
    PriorityWindow priorityWindow,
    PathEditorWindow pathEditorWindow,
    RedoUtil redoUtil,
    IClientState clientState,
    IObjectTable objectTable,
    ITargetManager targetManager,
    ICondition condition,
    IGameGui gameGui,
    Configuration configuration,
    ILogger<CreationUtilsComponent> logger)
{
    private Vector3? _savedPos;

    public void Draw()
    {
        if (objectTable[0] == null)
            return;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(QstTheme.TextMuted, FontAwesomeIcon.MapMarkerAlt.ToIconString());
        }

        ImGui.SameLine();
        string territoryName = TerritoryData.GetNameAndId(clientState.TerritoryType);
        ImGui.Text(territoryName);

        if (gameFunctions.IsFlyingUnlockedInCurrentZone())
        {
            ImGui.SameLine();
            ImGui.Text(SeIconChar.BotanistSprout.ToIconString());
        }

        if (configuration.Advanced.AdditionalStatusInformation)
        {
            ImGui.Spacing();
            QuestReference q = questFunctions.GetCurrentQuest();
            ImGui.Text(_LF("QST prio: {0} → {1}", q.CurrentQuest?.ToString() ?? "", q.Sequence));
            Quest? simQ = questController.SimulatedQuest?.Quest;
            if (simQ != null)
                ImGui.Text(_LF("Sim: {0} → {1}", simQ.Id, questController.SimulatedQuest?.Sequence.ToString(CultureInfo.InvariantCulture) ?? ""));
            unsafe
            {
                if (configuration.Advanced.ShowNewGamePlus)
                {
                    uint qid = q.CurrentQuest?.Value ?? 0;
                    if (simQ != null)
                        qid = simQ.Id.Value;

                    RedoIndex chapter = redoUtil.GetChapter((ushort)qid);
                    string isSim = simQ != null ? " (sim)" : "";
                    if (chapter.Index != -1)
                        ImGui.Text(_LF("NG+{0}: {1}", isSim, chapter));
                }

                if (configuration.Advanced.ShowDailies || configuration.Advanced.ShowTracked)
                {
                    QuestManager* questManager = QuestManager.Instance();
                    if (questManager != null)
                    {
                        if (configuration.Advanced.ShowTracked)
                        {
                            for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
                            {
                                TrackingWork trackedQuest = questManager->TrackedQuests[i];
                                switch (trackedQuest.QuestType)
                                {
                                    default:
                                        if (trackedQuest.QuestType != 0 || trackedQuest.Index != 0)
                                            ImGui.Text(_LF("Tracked Quest {0}: {1}, {2}", i, trackedQuest.QuestType, trackedQuest.Index));
                                        break;

                                    case 1:
                                        //_questRegistry.TryGetQuest(questManager->NormalQuests[trackedQuest.Index].QuestId,
                                        //    out var quest);
                                        ImGui.Text(
                                            _LF("Tracked Quest: {0} → {1}",
                                                    questManager->NormalQuests[trackedQuest.Index].QuestId,
                                                    questManager->NormalQuests[trackedQuest.Index].Sequence));
                                        break;

                                    case 2:
                                        break;
                                }
                            }
                        }

                        if (configuration.Advanced.ShowDailies)
                        {
                            for (int i = 0; i < questManager->DailyQuests.Length; ++i)
                            {
                                DailyQuestWork dailyQuest = questManager->DailyQuests[i];
                                if (dailyQuest.QuestId != 0 && !dailyQuest.IsCompleted)
                                {
                                    ImGui.Text(_LF("Daily Quest {0}: {1}, C:{2}", i, dailyQuest.QuestId, dailyQuest.IsCompleted));
                                    if (questRegistry.TryGetQuest(new QuestId(dailyQuest.QuestId), out Quest? quest))
                                    {
                                        if (ImGui.IsItemHovered())
                                            ImGui.SetTooltip($"{quest.Info.Name} ({quest.Info.AlliedSociety})");

                                        if (ImGui.IsItemClicked())
                                        {
                                            questController.PriorityManager.Add(quest.Id);
                                            if (!priorityWindow.IsOpen)
                                                priorityWindow.ToggleOrUncollapse();
                                            priorityWindow.BringToFront();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (configuration.Advanced.ShowDirector)
                {
                    Director* director = UIState.Instance()->DirectorTodo.Director;
                    if (director != null)
                    {
                        ImGui.Spacing();
                        ImGui.Text(_LF("Director: {0}", director->ContentId));
                        ImGui.Text(_LF("Seq: {0}", director->Sequence));
                        ImGui.Text(_LF("Ico: {0}", director->IconId));
                        if (director->EventHandlerInfo != null)
                        {
                            ImGui.Text($"  EHI CI: {director->Info.EventId.ContentId}");
                            ImGui.Text($"  EHI EI: {director->Info.EventId.Id}");
                            ImGui.Text($"  EHI EEI: {director->Info.EventId.EntryId}");
                            ImGui.Text($"  EHI F: {director->Info.Flags}");
                        }
                    }
                }

                if (configuration.Advanced.ShowActionManager)
                {
                    ImGui.Spacing();
                    ActionManager* actionManager = ActionManager.Instance();
                    ImGui.Text(
                        $"A1: {actionManager->CastActionId} ({actionManager->LastUsedActionSequence} → {actionManager->LastHandledActionSequence})");
                    ImGui.Text($"A2: {actionManager->CastTimeElapsed} / {actionManager->CastTimeTotal}");
                    ImGui.Text($"PC: {questController.TaskQueue.CurrentTaskExecutor?.ProgressContext}");
                }

                if (configuration.Advanced.ShowHoveredItem)
                {
                    ulong hoveredItemId = gameGui.HoveredItem;
                    string hq = hoveredItemId >= 1000000 && hoveredItemId < 2000000 ? ((char)SeIconChar.HighQuality).ToString() : "";
                    ImGui.Text(_LF("Hovered Item: {0}", !hq.IsNullOrEmpty() ? hoveredItemId % 1000000 : hoveredItemId) + hq);
                }
            }
        }

        if (targetManager.Target != null)
        {
            DrawTargetDetails(targetManager.Target);
            DrawInteractionButtons(targetManager.Target);
            ImGui.SameLine();
            DrawCopyButton(targetManager.Target);
        }
        else
        {
            ImGui.Spacing();
            DrawInteractionButtons();
            ImGui.SameLine();
            DrawCopyButton();
        }
        DrawSavedDetails();
    }

    internal void DrawPathEditorButton(ElementId? currentQuest, bool sameLine = false)
    {
        using (ImRaii.Disabled(currentQuest == null))
        {
            if (currentQuest != null)
            {
                if (sameLine)
                    ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit))
                    pathEditorWindow.Open(currentQuest);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    questRegistry.OpenEditor(currentQuest.Value);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_L("Left click: Open in Path Editor\nRight click: Open in your default .json text editor"));
    }

    private unsafe void DrawTargetDetails(IGameObject target)
    {
        string nameId = string.Empty;
        if (target is ICharacter { NameId: > 0 } character)
            nameId = $"; n={character.NameId}";

        ImGui.Spacing();
        using (QstWidgets.Card())
        {
            ImGui.Text(_LF("Target: {0}", target.Name));
            ImGui.SameLine();
            if (QstWidgets.Chip(QuestStepCapture.GuessInteractionType(target).ToString(), QstTheme.Info))
                ImGui.SetTooltip(_L("Interaction type detected from the target's nameplate icon."));

            ImGui.Text(_LF("  ({0}; {1}{2})",
                                target.ObjectKind, GameFunctions.GetBaseID(target), nameId));

            if (objectTable[0] != null)
            {
                ImGui.Text(_LF("Distance: {0:F2} ({1}y)",
                    (target.Position - objectTable[0]!.Position).Length(),
                    Math.Floor(target.Position.DistanceTo_XZ(objectTable[0]!.Position)) - 1));
                ImGui.SameLine();

                float verticalDistance = target.Position.Y - objectTable[0]!.Position.Y;
                string verticalDistanceText = _LF("Y: {0:F2}", verticalDistance);
                if (Math.Abs(verticalDistance) >= MovementController.DefaultVerticalInteractionDistance)
                    ImGui.TextColored(QstTheme.Accent, verticalDistanceText);
                else
                    ImGui.Text(verticalDistanceText);

                ImGui.SameLine();
            }

            GameObject* gameObject = (GameObject*)target.Address;
            ImGui.Text($"QM: {gameObject->NamePlateIconId}");
        }
    }

    private unsafe void DrawSavedDetails()
    {
        if (_savedPos == null)
            return;
        ImGui.Spacing();
        using (QstWidgets.Card())
        {
            if (objectTable[0] != null)
            {
                ImGui.Text(_LF("Distance: {0:F2} ({1}y)",
                    (_savedPos.Value - objectTable[0]!.Position).Length(),
                    Math.Floor(_savedPos.Value.DistanceTo_XZ(objectTable[0]!.Position)) - 1));
                ImGui.SameLine();

                float verticalDistance = _savedPos.Value.Y - objectTable[0]!.Position.Y;
                string verticalDistanceText = _LF("Y: {0:F2}", verticalDistance);
                if (Math.Abs(verticalDistance) >= MovementController.DefaultVerticalInteractionDistance)
                    ImGui.TextColored(QstTheme.Accent, verticalDistanceText);
                else
                    ImGui.Text(verticalDistanceText);
            }
        }
    }

    private unsafe void DrawInteractionButtons(IGameObject? target = null)
    {
        if (target != null)
        {
            using (ImRaii.Disabled(!movementController.IsNavmeshReady || gameFunctions.IsOccupied()))
            {
                if (!movementController.IsPathfinding)
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Bullseye, _L("To Target")))
                    {
                        movementController.NavigateTo(EMovementType.DebugWindow, GameFunctions.GetBaseID(target),
                            target.Position, new()
                            {
                                Fly = condition[ConditionFlag.Mounted] && gameFunctions.IsFlyingUnlockedInCurrentZone(),
                                Sprint = true,
                            });
                    }
                }
                else
                {
                    if (ImGui.Button(_L("Cancel pathfinding")))
                        movementController.ResetPathfinding();
                }
            }
            ImGui.SameLine();
        }

        uint targetId = GameFunctions.GetBaseID(target);
        //logger.LogDebug($"Current target: {target.Name} {targetId}");
        if (target != null)
        {
            using (ImRaii.Disabled(!questData.IsIssuerOfAnyQuest(targetId)))
            {
                bool showQuests = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MapMarkerAlt);
                if (showQuests)
                {
                    logger.LogDebug($"Opening questselectionwindow for {target.Name} {targetId}");
                    questSelectionWindow.OpenForTarget(target, targetId);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Show all Quests starting with your current target."));
        }
        else
        {
            bool showZoneQuests = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MapMarkerAlt);
            if (showZoneQuests)
            {
                logger.LogDebug($"Opening questselectionwindow for current zone {TerritoryData.GetNameAndId(clientState.TerritoryType)}");
                questSelectionWindow.OpenForCurrentZone();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Show all (currently visible) Quests starting in this zone."));
        }

        if (target != null)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(gameFunctions.IsOccupied()))
            {
                bool interact = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MousePointer);
                if (interact)
                {
                    cameraFunctions.Face(target.Position);
                    ulong result = TargetSystem.Instance()->InteractWithObject(
                        (GameObject*)target.Address, checkLineOfSight: false);
                    logger.LogInformation("Interaction Result: {Result}", result);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Interact with your current target."));
        }

        if (objectTable[0] != null)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MapPin))
            {
                if (_savedPos == null)
                    _savedPos = objectTable[0]!.Position;
                else
                    _savedPos = null;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Save/clear current position as reference"));
        }
    }

    private string GetCurrentQuestInfoAsString()
    {
        QuestReference q = questFunctions.GetCurrentQuest();
        string qw;
        if (q.CurrentQuest is QuestId)
        {
            QuestProgressInfo? progressInfo = QuestFunctions.GetQuestProgressInfo(q.CurrentQuest);
            qw = progressInfo != null ? progressInfo.ToString() : "QW: -";
        }
        else
            return _L("No active quest");

        return $"{q.CurrentQuest} → {q.Sequence} - {qw}";
    }

    private void DrawCopyButton(IGameObject target)
    {
        bool copy = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                _L("Left click: Copy target position as JSON.\nRight click: Copy target position as C# code."));
        }

        if (copy)
        {
            if (target.ObjectKind == ObjectKind.GatheringPoint)
            {
                ImGui.SetClipboardText($$"""
                                         "DataId": {{GameFunctions.GetBaseID(target)}},
                                                   "Position": {
                                                     "X": {{target.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                                     "Y": {{target.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                                     "Z": {{target.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                                   }
                                         """);
            }
            else
            {
                string interactionType = QuestStepCapture.GuessInteractionType(target).ToString();
                ImGui.SetClipboardText($$"""
                                         "DataId": {{GameFunctions.GetBaseID(target)}},
                                                   "Position": {
                                                     "X": {{target.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                                     "Y": {{target.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                                     "Z": {{target.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                                   },
                                                   "TerritoryId": {{clientState.TerritoryType}},

                                         """ + (GameFunctions.IsFlyingUnlocked(clientState.TerritoryType) ?
                                       $$"""
                                                   "InteractionType": "{{interactionType}}",
                                                   "Fly": true
                                         """ :
                                       $$"""
                                                   "InteractionType": "{{interactionType}}"
                                         """));
            }
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (target.ObjectKind == ObjectKind.Aetheryte)
            {
                EAetheryteLocation location = (EAetheryteLocation)GameFunctions.GetBaseID(target);
                ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                    $"{{EAetheryteLocation.{location}, new({target.Position.X}f, {target.Position.Y}f, {target.Position.Z}f)}},"));
            }
            else
            {
                ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                    $"new({target.Position.X}f, {target.Position.Y}f, {target.Position.Z}f)"));
            }
        }
    }

    private void DrawCopyButton()
    {
        if (objectTable[0] == null)
            return;

        bool copy = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                _L("Left click: Copy your position as JSON.\nRight click: Copy your position as C# code."));
        }

        if (copy)
        {
            ImGui.SetClipboardText($$"""
                                     "Position": {
                                                 "X": {{objectTable[0]!.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                                 "Y": {{objectTable[0]!.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                                 "Z": {{objectTable[0]!.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                               },
                                               "TerritoryId": {{clientState.TerritoryType}},

                                     """ + (GameFunctions.IsFlyingUnlocked(clientState.TerritoryType) ?
                                   $$"""
                                               "InteractionType": "",
                                               "Fly": true
                                     """ :
                                   $$"""
                                               "InteractionType": ""
                                     """));
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Vector3 position = objectTable[0]!.Position;
            ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                $"new({position.X}f, {position.Y}f, {position.Z}f)"));
        }
    }
}
