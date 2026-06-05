using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Questionable.Controller;
using Questionable.Controller.GameUi;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.Common;
using Questionable.Windows.QuestComponents;
namespace Questionable.Windows;

internal sealed class QuestSelectionWindow : LWindow
{
    private const string WindowId = "###QuestionableQuestSelection";
    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly IGameGuiAdapter _gameGui;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly TerritoryData _territoryData;
    private readonly UiUtils _uiUtils;
    private List<IQuestInfo> _offeredQuests = [];
    private bool _onlyAvailableQuests = true;

    private List<IQuestInfo> _quests = [];

    public QuestSelectionWindow(
        QuestData questData,
        IGameGuiAdapter gameGui,
        IChatGui chatGui,
        QuestFunctions questFunctions,
        QuestController questController,
        QuestRegistry questRegistry,
        IDalamudPluginInterface pluginInterface,
        TerritoryData territoryData,
        IClientState clientState,
        UiUtils uiUtils,
        QuestTooltipComponent questTooltipComponent)
        : base($"Quest Selection{WindowId}")
    {
        _questData = questData;
        _gameGui = gameGui;
        _chatGui = chatGui;
        _questFunctions = questFunctions;
        _questController = questController;
        _questRegistry = questRegistry;
        _pluginInterface = pluginInterface;
        _territoryData = territoryData;
        _clientState = clientState;
        _uiUtils = uiUtils;
        _questTooltipComponent = questTooltipComponent;

        Size = new Vector2(500, 200);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(500, 200)
        };
    }

    public unsafe void OpenForTarget(IGameObject? gameObject, uint targetId)
    {
        if (gameObject != null)
        {
            targetId = GameFunctions.GetBaseID(gameObject);
            string targetName = gameObject.Name.ToString();
            WindowName = $"Quests starting with {targetName} [{targetId}]{WindowId}";

            _quests = _questData.GetAllByIssuerDataId(targetId);
            if (_gameGui.TryGetAddonByName("SelectIconString", out AddonSelectIconString* addonSelectIconString))
            {
                List<string?> answers = DialogueChoiceHandler.GetChoices(addonSelectIconString);
                _offeredQuests = _quests
                    .Where(x => answers.Any(y => GameFunctions.GameStringEquals(x.Name, y)))
                    .ToList();
            }
            else
                _offeredQuests = [];
        }
        else
        {
            _quests = [];
            _offeredQuests = [];
        }

        IsOpenAndUncollapsed = _quests.Count > 0;
    }

    public unsafe void OpenForCurrentZone()
    {
        uint territoryId = _clientState.TerritoryType;
        string territoryName = _territoryData.GetNameAndId(territoryId);
        WindowName = $"Quests starting in {territoryName}{WindowId}";

        _quests = _questRegistry.AllQuests
            .Where(x => x.FindSequence(0)?.FindStep(0)?.TerritoryId == territoryId)
            .Select(x => _questData.GetQuestInfo(x.Id))
            .ToList();

        foreach (MarkerInfo unacceptedQuest in Map.Instance()->UnacceptedQuestMarkers)
        {
            QuestId questId = QuestId.FromRowId(unacceptedQuest.ObjectiveId);
            if (_quests.All(q => q.QuestId != questId))
                _quests.Add(_questData.GetQuestInfo(questId));
        }

        _offeredQuests = [];
        IsOpenAndUncollapsed = true;
    }

    public override void OnClose()
    {
        _quests = [];
        _offeredQuests = [];
    }

    public override void DrawContent()
    {
        if (_offeredQuests.Count != 0)
            ImGui.Checkbox("Only show quests currently offered", ref _onlyAvailableQuests);

        using ImRaii.TableDisposable table = ImRaii.Table("QuestSelection", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
        if (!table)
        {
            ImGui.Text("Not table");
            return;
        }

        float statusIconSize;
        using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            statusIconSize = ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X;
        }

        ImGui.PushFont(UiBuilder.IconFont);
        uint buttonCount = 5;
        float actionIconSize = ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X * buttonCount +
                               ImGui.GetStyle().FramePadding.X * buttonCount*2 +
                               ImGui.GetStyle().ItemSpacing.X * buttonCount;
        ImGui.PopFont();

        ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 50 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, statusIconSize);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionIconSize);
        ImGui.TableHeadersRow();

        foreach (IQuestInfo quest in (_offeredQuests.Count != 0 && _onlyAvailableQuests) ? _offeredQuests : _quests)
        {
            ImGui.TableNextRow();

            string questId = quest.QuestId.ToString();
            bool isKnownQuest = _questRegistry.TryGetQuest(quest.QuestId, out Quest? knownQuest);

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(questId);
            }

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                (Vector4 color, FontAwesomeIcon icon, string _) = _uiUtils.GetQuestStyle(quest.QuestId);
                using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    if (isKnownQuest)
                        ImGui.TextColored(color, icon.ToIconString());
                    else
                        ImGui.TextColored(ImGuiColors.DalamudGrey, icon.ToIconString());
                }

                if (ImGui.IsItemHovered())
                    _questTooltipComponent.Draw(quest);
            }

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();

                if (knownQuest != null && knownQuest.Root.Disabled)
                {
                    using IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
                    ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.Ban.ToIconString());
                    ImGui.SameLine();
                }

                ImGui.TextUnformatted(quest.Name);
            }

            if (ImGui.TableNextColumn())
            {
                // If button is added/removed, update buttonCount

                using ImRaii.IdDisposable id = ImRaii.PushId(questId);

                bool priority = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Add to priority quests");
                if (priority)
                    _questController.PriorityManager.Add(quest.QuestId);
                ImGui.SameLine();

                bool copy = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Copy);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Copy as file name");
                if (copy)
                    CopyToClipboard(quest, true);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    CopyToClipboard(quest, false);
                ImGui.SameLine();
#if DEBUG
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit))
                    (bool success, string filename) = QuestRegistry.OpenEditor(quest);
                ImGui.SameLine();
#endif

                if (knownQuest != null &&
                    knownQuest.FindSequence(0)?.LastStep()?.InteractionType is EInteractionType.AcceptQuest &&
                    _questFunctions.IsReadyToAcceptQuest(quest.QuestId))
                {
                    using (ImRaii.Disabled(_questController.NextQuest != null || _questController.SimulatedQuest != null))
                    {
                        bool startNextQuest = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Start as next quest");
                        if (startNextQuest)
                        {
                            _questController.SetNextQuest(knownQuest);
                            _questController.Start("QuestSelectionWindow");
                        }
                        ImGui.SameLine();

                        bool setNextQuest = ImGuiComponentsLocal.IconButton(FontAwesomeIcon.AngleDoubleRight);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Set as next quest");
                        if (setNextQuest)
                            _questController.SetNextQuest(knownQuest);
                    }
                }
            }
        }
    }

    private void CopyToClipboard(IQuestInfo quest, bool suffix)
    {
        string fileName = $"{quest.QuestId}_{quest.SimplifiedName}{(suffix ? ".json" : "")}";
        ImGui.SetClipboardText(fileName);
        _chatGui.Print($"Copied '{fileName}' to clipboard");
    }
}
