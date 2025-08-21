using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;

namespace Questionable.Windows.QuestComponents;

internal sealed class QuestTooltipComponent
{
    private readonly QuestRegistry _questRegistry;
    private readonly QuestData _questData;
    private readonly TerritoryData _territoryData;
    private readonly QuestFunctions _questFunctions;
    private readonly UiUtils _uiUtils;
    private readonly Configuration _configuration;

    public QuestTooltipComponent(
        QuestRegistry questRegistry,
        QuestData questData,
        TerritoryData territoryData,
        QuestFunctions questFunctions,
        UiUtils uiUtils,
        Configuration configuration)
    {
        _questRegistry = questRegistry;
        _questData = questData;
        _territoryData = territoryData;
        _questFunctions = questFunctions;
        _uiUtils = uiUtils;
        _configuration = configuration;
    }

    public void Draw(IQuestInfo questInfo)
    {
        using var tooltip = ImRaii.Tooltip();
        if (tooltip)
            DrawInner(questInfo, true);
    }

    public void DrawInner(IQuestInfo questInfo, bool showItemRewards)
    {
        ImGui.Text($"{SeIconChar.LevelEn.ToIconString()}{questInfo.Level}");
        ImGui.SameLine();

        var (color, _, tooltipText) = _uiUtils.GetQuestStyle(questInfo.QuestId);
        ImGui.TextColored(color, tooltipText);

        if (questInfo is QuestInfo { IsSeasonalEvent: true })
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("Event");
        }

        if (questInfo.IsRepeatable)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("Repeatable");
        }

        if (questInfo is QuestInfo { CompletesInstantly: true })
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("Instant");
        }

        if (_questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
        {
            if (quest.Root.Disabled)
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudRed, "Disabled");
            }

            if (quest.Root.Author.Count == 1)
                ImGui.Text($"Author: {quest.Root.Author[0]}");
            else
                ImGui.Text($"Authors: {string.Join(", ", quest.Root.Author)}");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudRed, "NoQuestPath");
        }

        DrawQuestUnlocks(questInfo, 0, showItemRewards);
    }

    private void DrawQuestUnlocks(IQuestInfo questInfo, int counter, bool showItemRewards)
    {
        if (counter >= 10)
            return;

        if (counter != 0 && questInfo.IsMainScenarioQuest)
            return;

        if (counter > 0)
            ImGui.Indent();

        if (questInfo.PreviousQuests.Count > 0)
        {
            if (counter == 0)
                ImGui.Separator();

            if (questInfo.PreviousQuests.Count > 1)
            {
                if (questInfo.PreviousQuestJoin == EQuestJoin.All)
                    ImGui.Text("Requires all:");
                else if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne)
                    ImGui.Text("Requires one:");
            }

            foreach (var q in questInfo.PreviousQuests)
            {
                if (_questData.TryGetQuestInfo(q.QuestId, out var qInfo))
                {
                    var (iconColor, icon, _) = _uiUtils.GetQuestStyle(q.QuestId);
                    if (!_questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = ImGuiColors.DalamudGrey;

                    _uiUtils.ChecklistItem(
                        FormatQuestUnlockName(qInfo,
                            _questFunctions.IsQuestComplete(q.QuestId) ? byte.MinValue : q.Sequence), iconColor, icon);

                    if (qInfo is QuestInfo qstInfo && (counter <= 2 || icon != FontAwesomeIcon.Check))
                        DrawQuestUnlocks(qstInfo, counter + 1, false);
                }
                else
                {
                    using var _ = ImRaii.Disabled();
                    _uiUtils.ChecklistItem($"Unknown Quest ({q.QuestId})", ImGuiColors.DalamudGrey,
                        FontAwesomeIcon.Question);
                }
            }
        }

        if (questInfo is QuestInfo actualQuestInfo)
        {
            if (actualQuestInfo.MoogleDeliveryLevel > 0)
                ImGui.Text($"Requires Carrier Level {actualQuestInfo.MoogleDeliveryLevel}");


            if (counter == 0 && actualQuestInfo.QuestLocks.Count > 0)
            {
                ImGui.Separator();
                if (actualQuestInfo.QuestLocks.Count > 1)
                {
                    if (actualQuestInfo.QuestLockJoin == EQuestJoin.All)
                        ImGui.Text("Blocked by (if all completed):");
                    else if (actualQuestInfo.QuestLockJoin == EQuestJoin.AtLeastOne)
                        ImGui.Text("Blocked by (if at least completed):");
                }
                else
                    ImGui.Text("Blocked by (if completed):");

                foreach (var q in actualQuestInfo.QuestLocks)
                {
                    var qInfo = _questData.GetQuestInfo(q);
                    var (iconColor, icon, _) = _uiUtils.GetQuestStyle(q);
                    if (!_questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = ImGuiColors.DalamudGrey;

                    _uiUtils.ChecklistItem(FormatQuestUnlockName(qInfo), iconColor, icon);
                }
            }

            if (counter == 0 && actualQuestInfo.PreviousInstanceContent.Count > 0)
            {
                ImGui.Separator();
                if (actualQuestInfo.PreviousInstanceContent.Count > 1)
                {
                    if (questInfo.PreviousQuestJoin == EQuestJoin.All)
                        ImGui.Text("Requires all:");
                    else if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne)
                        ImGui.Text("Requires one:");
                }
                else
                    ImGui.Text("Requires:");

                foreach (var instanceId in actualQuestInfo.PreviousInstanceContent)
                {
                    string instanceName = _territoryData.GetInstanceName(instanceId) ?? "?";
                    var (iconColor, icon) = UiUtils.GetInstanceStyle(instanceId);
                    _uiUtils.ChecklistItem(instanceName, iconColor, icon);
                }
            }

            if (counter == 0 && actualQuestInfo.GrandCompany != GrandCompany.None)
            {
                ImGui.Separator();
                string gcName = actualQuestInfo.GrandCompany switch
                {
                    GrandCompany.Maelstrom => "Maelstrom",
                    GrandCompany.TwinAdder => "Twin Adder",
                    GrandCompany.ImmortalFlames => "Immortal Flames",
                    _ => "None",
                };

                GrandCompany currentGrandCompany = _questFunctions.GetGrandCompany();
                _uiUtils.ChecklistItem($"Grand Company: {gcName}", actualQuestInfo.GrandCompany == currentGrandCompany);
            }

            if (showItemRewards && actualQuestInfo.ItemRewards.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text("Item Rewards:");
                foreach (var reward in actualQuestInfo.ItemRewards)
                {
                    ImGui.BulletText(reward.Name);
                }
            }
        }

        if (counter > 0)
            ImGui.Unindent();
    }

    private string FormatQuestUnlockName(IQuestInfo questInfo, byte sequence = 0)
    {
        string name = questInfo.Name;
        if (_configuration.Advanced.AdditionalStatusInformation && sequence != 0)
            name += $" {SeIconChar.ItemLevel.ToIconString()}";

        if (questInfo.IsMainScenarioQuest)
            name += $" ({questInfo.QuestId}, MSQ)";
        else
            name += $" ({questInfo.QuestId})";

        return name;
    }
}
