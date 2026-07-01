using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.Utils;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using static Questionable.Utils.LocalizeShortcut;

namespace Questionable.Windows.QuestComponents;

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
    RedoUtil redoUtil,
    IClientState clientState,
    IObjectTable objectTable,
    ITargetManager targetManager,
    ICondition condition,
    IGameGui gameGui,
    Configuration configuration,
    ILogger<CreationUtilsComponent> logger)
{

    public void Draw()
    {
        if (objectTable[0] == null)
            return;

        string territoryName = TerritoryData.GetNameAndId(clientState.TerritoryType);
        ImGui.Text(territoryName);

        if (gameFunctions.IsFlyingUnlockedInCurrentZone())
        {
            ImGui.SameLine();
            ImGui.Text(SeIconChar.BotanistSprout.ToIconString());
        }

        if (configuration.Advanced.AdditionalStatusInformation)
        {
            ImGui.Separator();
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
                        ImGui.Separator();
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
                    ImGui.Separator();
                    ActionManager* actionManager = ActionManager.Instance();
                    ImGui.Text(
                        $"A1: {actionManager->CastActionId} ({actionManager->LastUsedActionSequence} → {actionManager->LastHandledActionSequence})");
                    ImGui.Text($"A2: {actionManager->CastTimeElapsed} / {actionManager->CastTimeTotal}");
                    ImGui.Text($"PC: {questController.TaskQueue.CurrentTaskExecutor?.ProgressContext}");
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
            ImGui.Separator();
            DrawInteractionButtons();
            ImGui.SameLine();
            DrawCopyButton();
        }

        ulong hoveredItemId = gameGui.HoveredItem;
        if (hoveredItemId != 0)
        {
            ImGui.Separator();
            ImGui.Text(_LF("Hovered Item: {0}", hoveredItemId));
        }
    }

    private unsafe void DrawTargetDetails(IGameObject target)
    {
        string nameId = string.Empty;
        if (target is ICharacter { NameId: > 0 } character)
            nameId = $"; n={character.NameId}";

        ImGui.Separator();
        ImGui.Text(_LF("Target: {0}", target.Name));
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
                ImGui.TextColored(ImGuiColors.DalamudOrange, verticalDistanceText);
            else
                ImGui.Text(verticalDistanceText);

            ImGui.SameLine();
        }

        GameObject* gameObject = (GameObject*)target.Address;
        ImGui.Text($"QM: {gameObject->NamePlateIconId}");
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
                        (GameObject*)target.Address, false);
                    logger.LogInformation("Interaction Result: {Result}", result);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Interact with your current target."));
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

    private unsafe void DrawCopyButton(IGameObject target)
    {
        GameObject* gameObject = (GameObject*)target.Address;
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
                string interactionType = gameObject->NamePlateIconId switch
                {
                    71201 or 71211 or 71221 or 71231 or 71341 or 71351 => "AcceptQuest",
                    71202 or 71212 or 71222 or 71232 or 71342 or 71352 => "AcceptQuest", // repeatable
                    71205 or 71215 or 71225 or 71235 or 71345 or 71355 => "CompleteQuest",
                    var _ => "Interact"
                };
                ImGui.SetClipboardText($$"""
                                         "DataId": {{GameFunctions.GetBaseID(target)}},
                                                   "Position": {
                                                     "X": {{target.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                                     "Y": {{target.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                                     "Z": {{target.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                                   }
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
