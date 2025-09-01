using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using LLib.GameUI;
using LLib.ImGui;
using Questionable.Controller;
using Questionable.Controller.GameUi;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Questionable.Windows;

internal sealed class QuestSelectionWindow : LWindow
{
    private const string WindowId = "###QuestionableQuestSelection";
    private readonly QuestData _questData;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chatGui;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestController _questController;
    private readonly QuestRegistry _questRegistry;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly UiUtils _uiUtils;
    private readonly QuestTooltipComponent _questTooltipComponent;

    private List<IQuestInfo> _quests = [];
    private List<IQuestInfo> _offeredQuests = [];
    private bool _onlyAvailableQuests = true;

    public QuestSelectionWindow(
        QuestData questData,
        IGameGui gameGui,
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
            MinimumSize = new Vector2(500, 200),
        };
    }

    public unsafe void OpenForTarget(IGameObject? gameObject)
    {
        if (gameObject != null)
        {
            var targetId = gameObject.DataId;
            var targetName = gameObject.Name.ToString();
            WindowName = $"Quests starting with {targetName} [{targetId}]{WindowId}";

            _quests = _questData.GetAllByIssuerDataId(targetId);
            if (_gameGui.TryGetAddonByName<AddonSelectIconString>("SelectIconString", out var addonSelectIconString))
            {
                var answers = InteractionUiController.GetChoices(addonSelectIconString);
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
        var territoryId = _clientState.TerritoryType;
        var territoryName = _territoryData.GetNameAndId(territoryId);
        WindowName = $"Quests starting in {territoryName}{WindowId}";

        _quests = _questRegistry.AllQuests
            .Where(x => x.FindSequence(0)?.FindStep(0)?.TerritoryId == territoryId)
            .Select(x => _questData.GetQuestInfo(x.Id))
            .ToList();

        foreach (var unacceptedQuest in Map.Instance()->UnacceptedQuestMarkers)
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

        using var table = ImRaii.Table("QuestSelection", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
        if (!table)
        {
            ImGui.Text("Not table");
            return;
        }

        float statusIconSize;
        using (var _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            statusIconSize = ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X;
        }

        ImGui.PushFont(UiBuilder.IconFont);
        var actionIconSize = ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X +
                             ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X +
                             ImGui.CalcTextSize(FontAwesomeIcon.Copy.ToIconString()).X +
                             6 * ImGui.GetStyle().FramePadding.X +
                             2 * ImGui.GetStyle().ItemSpacing.X;
        ImGui.PopFont();

        ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 50 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, statusIconSize);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 200);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionIconSize);
        ImGui.TableHeadersRow();

        foreach (IQuestInfo quest in (_offeredQuests.Count != 0 && _onlyAvailableQuests) ? _offeredQuests : _quests)
        {
            ImGui.TableNextRow();

            string questId = quest.QuestId.ToString();
            bool isKnownQuest = _questRegistry.TryGetQuest(quest.QuestId, out var knownQuest);

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(questId);
            }

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                var (color, icon, _) = _uiUtils.GetQuestStyle(quest.QuestId);
                using (var _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
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
                    using var _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
                    ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.Ban.ToIconString());
                    ImGui.SameLine();
                }

                ImGui.TextUnformatted(quest.Name);
            }

            if (ImGui.TableNextColumn())
            {
                using var id = ImRaii.PushId(questId);

                bool copy = ImGuiComponents.IconButton(FontAwesomeIcon.Copy);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Copy as file name");
                if (copy)
                    CopyToClipboard(quest, true);
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    CopyToClipboard(quest, false);

                ImGui.SameLine();

                if (knownQuest != null &&
                    knownQuest.FindSequence(0)?.LastStep()?.InteractionType is EInteractionType.AcceptQuest &&
                    _questFunctions.IsReadyToAcceptQuest(quest.QuestId))
                {
                    ImGui.BeginDisabled(_questController.NextQuest != null || _questController.SimulatedQuest != null);

                    bool startNextQuest = ImGuiComponents.IconButton(FontAwesomeIcon.Play);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Start as next quest");
                    if (startNextQuest)
                    {
                        _questController.SetNextQuest(knownQuest);
                        _questController.Start("QuestSelectionWindow");
                    }

                    ImGui.SameLine();

                    bool setNextQuest = ImGuiComponents.IconButton(FontAwesomeIcon.AngleDoubleRight);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Set as next quest");
                    if (setNextQuest)
                        _questController.SetNextQuest(knownQuest);

                    ImGui.EndDisabled();
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
