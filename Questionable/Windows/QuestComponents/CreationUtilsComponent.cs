using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Questionable.Windows.QuestComponents;

internal sealed class CreationUtilsComponent
{
#if false
    private readonly QuestController _questController;
#endif
    private readonly MovementController _movementController;
    private readonly GameFunctions _gameFunctions;
    private readonly QuestFunctions _questFunctions;
    private readonly TerritoryData _territoryData;
    private readonly QuestData _questData;
    private readonly QuestSelectionWindow _questSelectionWindow;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly ICondition _condition;
    private readonly IGameGui _gameGui;
    private readonly Configuration _configuration;
    private readonly ILogger<CreationUtilsComponent> _logger;

    public CreationUtilsComponent(
#if false
        QuestController questController,
#endif
        MovementController movementController,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        TerritoryData territoryData,
        QuestData questData,
        QuestSelectionWindow questSelectionWindow,
        IClientState clientState,
        ITargetManager targetManager,
        ICondition condition,
        IGameGui gameGui,
        Configuration configuration,
        ILogger<CreationUtilsComponent> logger)
    {
#if false
        _questController = questController;
#endif
        _movementController = movementController;
        _gameFunctions = gameFunctions;
        _questFunctions = questFunctions;
        _territoryData = territoryData;
        _questData = questData;
        _questSelectionWindow = questSelectionWindow;
        _clientState = clientState;
        _targetManager = targetManager;
        _condition = condition;
        _gameGui = gameGui;
        _configuration = configuration;
        _logger = logger;
    }

    public void Draw()
    {
        Debug.Assert(_clientState.LocalPlayer != null, "_clientState.LocalPlayer != null");

        string territoryName = _territoryData.GetNameAndId(_clientState.TerritoryType);
        ImGui.Text(territoryName);

        if (_gameFunctions.IsFlyingUnlockedInCurrentZone())
        {
            ImGui.SameLine();
            ImGui.Text(SeIconChar.BotanistSprout.ToIconString());
        }

        if (_configuration.Advanced.AdditionalStatusInformation)
        {
            var q = _questFunctions.GetCurrentQuest();
            ImGui.Text($"Current Quest: {q.CurrentQuest} → {q.Sequence}");
        }

#if false
        unsafe {
            var questManager = QuestManager.Instance();
            if (questManager != null)
            {
                for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
                {
                    var trackedQuest = questManager->TrackedQuests[i];
                    switch (trackedQuest.QuestType)
                    {
                        default:
                            ImGui.Text($"Tracked quest {i}: {trackedQuest.QuestType}, {trackedQuest.Index}");
                            break;

                        case 1:
                            //_questRegistry.TryGetQuest(questManager->NormalQuests[trackedQuest.Index].QuestId,
                            //    out var quest);
                            ImGui.Text(
                                $"Quest: {questManager->NormalQuests[trackedQuest.Index].QuestId}, {trackedQuest.Index}");
                            break;

                        case 2:
                            break;
                    }
                }
            }
        }
#endif

#if false
        unsafe
        {
            var questManager = QuestManager.Instance();
            if (questManager != null)
            {
                for (int i = 0; i < questManager->DailyQuests.Length; ++i)
                {
                    var dailyQuest = questManager->DailyQuests[i];
                    ImGui.Text($"Daily Quest {i}: {dailyQuest.QuestId}, {dailyQuest.IsCompleted}");
                }
            }
        }
#endif

#if false
        unsafe
        {
            var director = UIState.Instance()->DirectorTodo.Director;
            if (director != null)
            {
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
#endif

#if false
        if (_configuration.Advanced.AdditionalStatusInformation)
        {
            unsafe
            {
                var actionManager = ActionManager.Instance();
                ImGui.Text(
                    $"A1: {actionManager->CastActionId} ({actionManager->LastUsedActionSequence} → {actionManager->LastHandledActionSequence})");
                ImGui.Text($"A2: {actionManager->CastTimeElapsed} / {actionManager->CastTimeTotal}");
                ImGui.Text($"{_questController.TaskQueue.CurrentTaskExecutor?.ProgressContext}");
            }
        }
#endif

        if (_targetManager.Target != null)
        {
            DrawTargetDetails(_targetManager.Target);
            DrawInteractionButtons(_targetManager.Target);
            ImGui.SameLine();
            DrawCopyButton(_targetManager.Target);
        }
        else
        {
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
        ImGui.Text(string.Create(CultureInfo.InvariantCulture,
            $"Target: {target.Name}  ({target.ObjectKind}; {target.DataId}{nameId})"));

        if (_clientState.LocalPlayer != null)
        {
            ImGui.Text(string.Create(CultureInfo.InvariantCulture,
                $"Distance: {(target.Position - _clientState.LocalPlayer.Position).Length():F2}"));
            ImGui.SameLine();

            float verticalDistance = target.Position.Y - _clientState.LocalPlayer.Position.Y;
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
                _movementController.NavigateTo(EMovementType.DebugWindow, target.DataId,
                    target.Position,
                    fly: _condition[ConditionFlag.Mounted] && _gameFunctions.IsFlyingUnlockedInCurrentZone(),
                    sprint: true);
            }
        }
        else
        {
            if (ImGui.Button("Cancel pathfinding"))
                _movementController.ResetPathfinding();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!_questData.IsIssuerOfAnyQuest(target.DataId));
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
            ulong result = TargetSystem.Instance()->InteractWithObject(
                (GameObject*)target.Address, false);
            _logger.LogInformation("XXXXX Interaction Result: {Result}", result);
        }

        ImGui.EndDisabled();
    }

    private unsafe void DrawCopyButton(IGameObject target)
    {
        GameObject* gameObject = (GameObject*)target.Address;
        bool copy = ImGuiComponents.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Left click: Copy target position as JSON.\nRight click: Copy target position as C# code.");
        if (copy)
        {
            if (target.ObjectKind == ObjectKind.GatheringPoint)
            {
                ImGui.SetClipboardText($$"""
                                         "DataId": {{target.DataId}},
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
                    _ => "Interact",
                };
                ImGui.SetClipboardText($$"""
                                         "DataId": {{target.DataId}},
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
                EAetheryteLocation location = (EAetheryteLocation)target.DataId;
                ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                    $"{{EAetheryteLocation.{location}, new({target.Position.X}f, {target.Position.Y}f, {target.Position.Z}f)}},"));
            }
            else
                ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                    $"new({target.Position.X}f, {target.Position.Y}f, {target.Position.Z}f)"));
        }
    }

    private void DrawCopyButton()
    {
        if (_clientState.LocalPlayer == null)
            return;

        bool copy = ImGuiComponents.IconButton(FontAwesomeIcon.Copy);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Left click: Copy your position as JSON.\nRight click: Copy your position as C# code.");
        if (copy)
        {
            ImGui.SetClipboardText($$"""
                                     "Position": {
                                         "X": {{_clientState.LocalPlayer.Position.X.ToString(CultureInfo.InvariantCulture)}},
                                         "Y": {{_clientState.LocalPlayer.Position.Y.ToString(CultureInfo.InvariantCulture)}},
                                         "Z": {{_clientState.LocalPlayer.Position.Z.ToString(CultureInfo.InvariantCulture)}}
                                     },
                                     "TerritoryId": {{_clientState.TerritoryType}},
                                     "InteractionType": ""
                                     """);
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Vector3 position = _clientState.LocalPlayer!.Position;
            ImGui.SetClipboardText(string.Create(CultureInfo.InvariantCulture,
                $"new({position.X}f, {position.Y}f, {position.Z}f)"));
        }
    }
}
