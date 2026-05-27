using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Text.ReadOnly;
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

namespace Questionable.Windows.QuestComponents;

internal sealed class CreationUtilsComponent
(
    QuestController questController,
    MovementController movementController,
    GameFunctions gameFunctions,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    CameraFunctions cameraFunctions,
    TerritoryData territoryData,
    QuestData questData,
    QuestSelectionWindow questSelectionWindow,
    PriorityWindow priorityWindow,
    IClientState clientState,
    IObjectTable objectTable,
    ITargetManager targetManager,
    ICondition condition,
    IGameGui gameGui,
    Configuration configuration,
    ILogger<CreationUtilsComponent> logger)
{
    private readonly CameraFunctions _cameraFunctions = cameraFunctions;
    private readonly IClientState _clientState = clientState;
    private readonly ICondition _condition = condition;
    private readonly Configuration _configuration = configuration;
    private readonly GameFunctions _gameFunctions = gameFunctions;
    private readonly IGameGui _gameGui = gameGui;
    private readonly ILogger<CreationUtilsComponent> _logger = logger;
    private readonly MovementController _movementController = movementController;
    private readonly IObjectTable _objectTable = objectTable;
    private readonly PriorityWindow _priorityWindow = priorityWindow;
    private readonly QuestController _questController = questController;
    private readonly QuestData _questData = questData;
    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly QuestSelectionWindow _questSelectionWindow = questSelectionWindow;
    private readonly RedoUtil _redoUtil = new();
    private readonly ITargetManager _targetManager = targetManager;
    private readonly TerritoryData _territoryData = territoryData;

    public void Draw()
    {
        if (_objectTable[0] == null)
            return;

        string territoryName = _territoryData.GetNameAndId(_clientState.TerritoryType);
        ImGui.Text(territoryName);

        if (_gameFunctions.IsFlyingUnlockedInCurrentZone())
        {
            ImGui.SameLine();
            ImGui.Text(SeIconChar.BotanistSprout.ToIconString());
        }

        if (_configuration.Advanced.AdditionalStatusInformation)
        {
            ImGui.Separator();
            QuestReference q = _questFunctions.GetCurrentQuest();
            ImGui.Text($"QST prio: {q.CurrentQuest} → {q.Sequence}");
            Quest? simQ = _questController.SimulatedQuest?.Quest;
            if (simQ != null)
                ImGui.Text($"Sim: {simQ.Id} → {_questController.SimulatedQuest?.Sequence}");
            unsafe
            {
                if (_configuration.Advanced.ShowNewGamePlus)
                {
                    uint qid = (uint)(q.CurrentQuest?.Value ?? 0) + 65536;
                    if (simQ != null)
                        qid = (uint)simQ.Id.Value + 65536;
                    Tuple<ReadOnlySeString, int> chapter = _redoUtil.GetChapter(qid);
                    string isSim = simQ != null ? " (sim)" : "";
                    if (!chapter.Item1.IsEmpty)
                        ImGui.Text($"NG+{isSim}: {chapter.Item1} (#{chapter.Item2 + 1})");
                }

                if (_configuration.Advanced.ShowDailies || _configuration.Advanced.ShowTracked)
                {
                    QuestManager* questManager = QuestManager.Instance();
                    if (questManager != null)
                    {
                        if (_configuration.Advanced.ShowTracked)
                        {
                            for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
                            {
                                TrackingWork trackedQuest = questManager->TrackedQuests[i];
                                switch (trackedQuest.QuestType)
                                {
                                    default:
                                        if (trackedQuest.QuestType != 0 || trackedQuest.Index != 0)
                                            ImGui.Text($"Tracked Quest {i}: {trackedQuest.QuestType}, {trackedQuest.Index}");
                                        break;

                                    case 1:
                                        //_questRegistry.TryGetQuest(questManager->NormalQuests[trackedQuest.Index].QuestId,
                                        //    out var quest);
                                        ImGui.Text(
                                            $"Tracked Quest: {questManager->NormalQuests[trackedQuest.Index].QuestId} → {questManager->NormalQuests[trackedQuest.Index].Sequence}");
                                        break;

                                    case 2:
                                        break;
                                }
                            }
                        }

                        if (_configuration.Advanced.ShowDailies)
                        {
                            for (int i = 0; i < questManager->DailyQuests.Length; ++i)
                            {
                                DailyQuestWork dailyQuest = questManager->DailyQuests[i];
                                if (dailyQuest.QuestId != 0 && !dailyQuest.IsCompleted)
                                {
                                    ImGui.Text($"Daily Quest {i}: {dailyQuest.QuestId}, C:{dailyQuest.IsCompleted}");
                                    if (_questRegistry.TryGetQuest(new QuestId(dailyQuest.QuestId), out Quest? quest))
                                    {
                                        if (ImGui.IsItemHovered())
                                            ImGui.SetTooltip($"{quest.Info.Name} ({quest.Info.AlliedSociety})");

                                        if (ImGui.IsItemClicked())
                                        {
                                            _questController.PriorityManager.Add(quest.Id);
                                            if (!_priorityWindow.IsOpen)
                                                _priorityWindow.ToggleOrUncollapse();
                                            _priorityWindow.BringToFront();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (_configuration.Advanced.ShowDirector)
                {
                    Director* director = UIState.Instance()->DirectorTodo.Director;
                    if (director != null)
                    {
                        ImGui.Separator();
                        ImGui.Text($"Director: {director->ContentId}");
                        ImGui.Text($"Seq: {director->Sequence}");
                        ImGui.Text($"Ico: {director->IconId}");
                        if (director->EventHandlerInfo != null)
                        {
                            ImGui.Text($"  EHI CI: {director->Info.EventId.ContentId}");
                            ImGui.Text($"  EHI EI: {director->Info.EventId.Id}");
                            ImGui.Text($"  EHI EEI: {director->Info.EventId.EntryId}");
                            ImGui.Text($"  EHI F: {director->Info.Flags}");
                        }
                    }
                }

                if (_configuration.Advanced.ShowActionManager)
                {
                    ImGui.Separator();
                    ActionManager* actionManager = ActionManager.Instance();
                    ImGui.Text(
                        $"A1: {actionManager->CastActionId} ({actionManager->LastUsedActionSequence} → {actionManager->LastHandledActionSequence})");
                    ImGui.Text($"A2: {actionManager->CastTimeElapsed} / {actionManager->CastTimeTotal}");
                    ImGui.Text($"PC: {_questController.TaskQueue.CurrentTaskExecutor?.ProgressContext}");
                }
            }
        }

        if (_targetManager.Target != null)
        {
            DrawTargetDetails(_targetManager.Target);
            DrawInteractionButtons(_targetManager.Target);
            ImGui.SameLine();
            DrawCopyButton(_targetManager.Target);
        }
        else
        {
            ImGui.Separator();
            DrawCopyButton();
        }

        ulong hoveredItemId = _gameGui.HoveredItem;
        if (hoveredItemId != 0)
        {
            ImGui.Separator();
            ImGui.Text($"Hovered Item: {hoveredItemId}");
        }
    }

    private unsafe void DrawTargetDetails(IGameObject target)
    {
        string nameId = string.Empty;
        if (target is ICharacter { NameId: > 0 } character)
            nameId = $"; n={character.NameId}";

        ImGui.Separator();
        ImGui.Text($"Target: {target.Name}");
        ImGui.Text($"  ({target.ObjectKind}; {GameFunctions.GetBaseID(target)}{nameId})");

        if (_objectTable[0] != null)
        {
            ImGui.Text(string.Create(CultureInfo.InvariantCulture,
                $"Distance: {(target.Position - _objectTable[0]!.Position).Length():F2}" +
                $"({Math.Floor(target.Position.DistanceTo_XZ(_objectTable[0]!.Position))-1}y)"));
            ImGui.SameLine();

            float verticalDistance = target.Position.Y - _objectTable[0]!.Position.Y;
            string verticalDistanceText = string.Create(CultureInfo.InvariantCulture, $"Y: {verticalDistance:F2}");
            if (Math.Abs(verticalDistance) >= MovementController.DefaultVerticalInteractionDistance)
                ImGui.TextColored(ImGuiColors.DalamudOrange, verticalDistanceText);
            else
                ImGui.Text(verticalDistanceText);

            ImGui.SameLine();
        }

        GameObject* gameObject = (GameObject*)target.Address;
        ImGui.Text($"QM: {gameObject->NamePlateIconId}");
    }

    private unsafe void DrawInteractionButtons(IGameObject target)
    {
        ImGui.BeginDisabled(!_movementController.IsNavmeshReady || _gameFunctions.IsOccupied());
        if (!_movementController.IsPathfinding)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bullseye, "To Target"))
            {
                _movementController.NavigateTo(EMovementType.DebugWindow, GameFunctions.GetBaseID(target),
                    target.Position, new()
                    {
                        Fly = _condition[ConditionFlag.Mounted] && _gameFunctions.IsFlyingUnlockedInCurrentZone(),
                        Sprint = true,
                    });
            }
        }
        else
        {
            if (ImGui.Button("Cancel pathfinding"))
                _movementController.ResetPathfinding();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!_questData.IsIssuerOfAnyQuest(GameFunctions.GetBaseID(target)));
        bool showQuests = ImGuiComponents.IconButton(FontAwesomeIcon.MapMarkerAlt);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show all Quests starting with your current target.");
        if (showQuests)
            _questSelectionWindow.OpenForTarget(_targetManager.Target);

        ImGui.EndDisabled();

        ImGui.BeginDisabled(_gameFunctions.IsOccupied());
        ImGui.SameLine();
        bool interact = ImGuiComponents.IconButton(FontAwesomeIcon.MousePointer);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Interact with your current target.");
        if (interact)
        {
            _cameraFunctions.Face(target.Position);
            ulong result = TargetSystem.Instance()->InteractWithObject(
                (GameObject*)target.Address, false);
            _logger.LogInformation("Interaction Result: {Result}", result);
        }

        ImGui.EndDisabled();
    }

    private string GetCurrentQuestInfoAsString()
    {
        QuestReference q = _questFunctions.GetCurrentQuest();
        string qw;
        if (q.CurrentQuest is QuestId)
        {
            QuestProgressInfo? progressInfo = QuestFunctions.GetQuestProgressInfo(q.CurrentQuest);
            qw = progressInfo != null ? progressInfo.ToString() : "QW: -";
        }
        else
            return "No active quest";

        return $"{q.CurrentQuest} → {q.Sequence} - {qw}";
    }

    private unsafe void DrawCopyButton(IGameObject target)
    {
        GameObject* gameObject = (GameObject*)target.Address;
        bool copy = ImGuiComponents.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Left click: Copy target position as JSON.\nRight click: Copy target position as C# code.");
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
                                                   },
                                                   "TerritoryId": {{_clientState.TerritoryType}},
                                                   "InteractionType": "{{interactionType}}"
                                         """);
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
        if (_objectTable[0] == null)
            return;

        bool copy = ImGuiComponents.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Left click: Copy your position as JSON.\nRight click: Copy your position as C# code.");
        }

        if (copy)
        {
            ImGui.SetClipboardText($$"""
                                     "Position": {
                                                 "X": {{_objectTable[0]!.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                                 "Y": {{_objectTable[0]!.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                                 "Z": {{_objectTable[0]!.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                               },
                                               "TerritoryId": {{_clientState.TerritoryType}},
                                               "InteractionType": ""
                                     """);
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Vector3 position = _objectTable[0]!.Position;
            ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                $"new({position.X}f, {position.Y}f, {position.Z}f)"));
        }
    }
}
