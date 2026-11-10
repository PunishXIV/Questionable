using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
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
    IDataManager dataManager,
    Configuration configuration,
    DebugOverlay debugOverlay,
    ILogger<CreationUtilsComponent> logger)
{

    public void Draw()
    {
        if (objectTable[0] == null)
            return;

        // Pathmaker info; the status lines below have their own toggles.
        if (configuration.Advanced.Debug)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(QstTheme.TextMuted, FontAwesomeIcon.MapMarkerAlt.ToIconString());
            }

            ImGui.SameLine();
            string territoryName = TerritoryData.GetNameAndId(clientState.TerritoryType);
            ImGui.Text(territoryName);

            if (gameFunctions.IsFlyingUnlockedHere())
            {
                ImGui.SameLine();
                ImGui.Text(SeIconChar.BotanistSprout.ToIconString());
            }
        }

        if (configuration.Advanced.AdditionalStatusInformation)
        {
            ImGui.Spacing();
            QuestReference current = questFunctions.GetCurrentQuest();
            ImGui.Text(_LF("QST prio: {0} → {1}", current.CurrentQuest?.ToString() ?? "", current.Sequence));
            Quest? simQuest = questController.SimulatedQuest?.Quest;
            if (simQuest != null)
                ImGui.Text(_LF("Sim: {0} → {1}", simQuest.Id, questController.SimulatedQuest?.Sequence.ToString(CultureInfo.InvariantCulture) ?? ""));
            unsafe
            {
                if (configuration.Advanced.ShowNewGamePlus)
                {
                    uint questId = current.CurrentQuest?.Value ?? 0;
                    if (simQuest != null)
                        questId = simQuest.Id.Value;

                    RedoIndex chapter = redoUtil.GetChapter((ushort)questId);
                    string simSuffix = simQuest != null ? " (sim)" : "";
                    if (chapter.Index != -1)
                        ImGui.Text(_LF("NG+{0}: {1}", simSuffix, chapter));
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
                                        {
                                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                                            ImGui.SetTooltip($"{quest.Info.Name} ({quest.Info.AlliedSociety})");
                                        }

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
                    string hqSuffix = hoveredItemId >= 1000000 && hoveredItemId < 2000000 ? ((char)SeIconChar.HighQuality).ToString() : "";
                    ImGui.Text(_LF("Hovered Item: {0}", !hqSuffix.IsNullOrEmpty() ? hoveredItemId % 1000000 : hoveredItemId) + hqSuffix);
                }
            }
        }

        bool debug = configuration.Advanced.Debug;
        IGameObject? target = targetManager.Target;
        if (debug)
        {
            if (target != null)
                DrawCopyButton(target);
            else
                DrawCopyButton();
            ImGui.SameLine();
        }

        DrawInteractionButtons(target);
        if (debug)
        {
            if (target != null)
                DrawTargetDetails(target);
            DrawSavedDetails();
        }
    }

    internal void DrawPathEditorButton(ElementId? questId, bool sameLine = false)
    {
        using (ImRaii.Disabled(questId == null))
        {
            if (questId != null)
            {
                if (sameLine)
                    ImGui.SameLine();
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit))
                    pathEditorWindow.Open(questId);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    questRegistry.OpenEditor(questId.Value);
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
                                target.ObjectKind, GameFunctions.GetBaseId(target), nameId));

            if (objectTable[0] != null)
            {
                ImGui.Text(_LF("Distance: {0:F2} ({1}y)",
                    (target.Position - objectTable[0]!.Position).Length(),
                    Math.Floor(target.Position.DistanceToXZ(objectTable[0]!.Position)) - 1));
                ImGui.SameLine();

                float verticalDistance = target.Position.Y - objectTable[0]!.Position.Y;
                string verticalDistanceText = _LF("Y: {0:F2}", verticalDistance);
                if (Math.Abs(verticalDistance) >= MovementController.DefaultVerticalDistance)
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
        if (debugOverlay.SavedPos == null || objectTable[0] == null)
            return;
        ImGui.Spacing();
        using (QstWidgets.Card())
        {
            var pos = objectTable[0]!.Position;
            ImGui.Text($"<{pos.X:F3},{pos.Y:F3},{pos.Z:F3}>");
            ImGui.Text($"<{debugOverlay.SavedPos.Value.X:F3},{debugOverlay.SavedPos.Value.Y:F3},{debugOverlay.SavedPos.Value.Z:F3}>");
            ImGui.Text(_LF("Distance: {0:F2} ({1}y)",
                (debugOverlay.SavedPos.Value - pos).Length(),
                Math.Floor(debugOverlay.SavedPos.Value.DistanceToXZ(pos)) - 1));
            ImGui.SameLine();

            float verticalDistance = debugOverlay.SavedPos.Value.Y - pos.Y;
            string verticalDistanceText = _LF("Y: {0:F2}", verticalDistance);
            if (Math.Abs(verticalDistance) >= MovementController.DefaultVerticalDistance)
                ImGui.TextColored(QstTheme.Accent, verticalDistanceText);
            else
                ImGui.Text(verticalDistanceText);
        }
    }

    private unsafe void DrawInteractionButtons(IGameObject? target)
    {
        if (configuration.Advanced.Debug && objectTable[0] != null)
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MapPin))
            {
                if (debugOverlay.SavedPos == null)
                    debugOverlay.SavedPos = objectTable[0]!.Position;
                else
                    debugOverlay.SavedPos = null;
                logger.LogDebug($"SavedPos: {debugOverlay.SavedPos?.ToString("G5", CultureInfo.InvariantCulture)}");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Save/clear current position as reference"));
            ImGui.SameLine();
        }

        uint targetId = GameFunctions.GetBaseId(target);
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
            using (ImRaii.Disabled(!movementController.IsNavmeshReady || gameFunctions.IsOccupied()))
            {
                if (!movementController.IsPathfinding)
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Bullseye, _L("To Target")))
                    {
                        movementController.NavigateTo(EMovementType.DebugWindow, GameFunctions.GetBaseId(target),
                            target.Position, new()
                            {
                                Fly = condition[ConditionFlag.Mounted] && gameFunctions.IsFlyingUnlockedHere(),
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
    }

    private readonly HashSet<uint> taxiStandIds = dataManager.GetExcelSheet<Lumina.Excel.Sheets.ChocoboTaxiStand>().Select(x => x.RowId).ToHashSet();
    private uint? GetTaxiStandId(IGameObject target)
    {
        if (target.ObjectKind == ObjectKind.EventNpc)
        {
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcBase>().TryGetRow(target.BaseId, out var row))
                return null;

            return row.ENpcData.Where(x => taxiStandIds.Contains(x.RowId)).FirstOrNull()?.RowId;
        }
        if (target.ObjectKind == ObjectKind.EventObj)
        {
            if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.EObj>().TryGetRow(target.BaseId, out var row))
                return null;

            return taxiStandIds.Contains(row.Data.RowId) ? row.Data.RowId : null;
        }
        return null;
    }

    private void DrawCopyButton(IGameObject target)
    {
        var position = target.Position;
        if (objectTable[0] != null)
            position = objectTable[0]!.Position;
        bool copy = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                _L("Left click: Copy your position as JSON.\nRight click: Copy your position as C# code."));
        }

        if (copy)
        {
            string positionJson =
                $$"""
                  "DataId": {{GameFunctions.GetBaseId(target)}},
                            "Position": {
                              "X": {{position.X.ToString(CultureInfo.InvariantCulture)}},
                              "Y": {{position.Y.ToString(CultureInfo.InvariantCulture)}},
                              "Z": {{position.Z.ToString(CultureInfo.InvariantCulture)}}
                            },
                  """;
            uint? taxiStandId = GetTaxiStandId(target);
            string interactionType = taxiStandId != null ? "UnlockTaxiStand" : QuestStepCapture.GuessInteractionType(target).ToString();
            string stepJson =
                $$"""

                            "TerritoryId": {{clientState.TerritoryType}},
                            "StopDistance": 1,
                            "InteractionType": "{{interactionType}}"
                  """ + (taxiStandId == null && GameFunctions.IsFlyingUnlocked(clientState.TerritoryType) ?
                $$"""
                  ,
                            "Fly": true
                  """ : "") + (taxiStandId != null ?
                $$"""
                  ,
                            "TaxiStandId": {{taxiStandId}}
                  """ : "");
            if (target.ObjectKind == ObjectKind.GatheringPoint)
            {
                ImGui.SetClipboardText(positionJson);
            }
            else
            {
                ImGui.SetClipboardText(positionJson + stepJson);
            }
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (target.ObjectKind == ObjectKind.Aetheryte)
            {
                EAetheryteLocation location = (EAetheryteLocation)GameFunctions.GetBaseId(target);
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
                                               "InteractionType": "WalkTo",
                                               "StopDistance": 1,
                                               "Fly": true
                                     """ :
                                   $$"""
                                               "InteractionType": "WalkTo",
                                               "StopDistance": 1
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
