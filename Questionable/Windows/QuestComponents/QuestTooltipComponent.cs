using System.Numerics;
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
using Questionable.Model.Questing;
using Questionable.Windows.Utils;
using static ECommons.Automation.Chat;
namespace Questionable.Windows.QuestComponents;

internal sealed class QuestTooltipComponent
(
    QuestRegistry questRegistry,
    QuestData questData,
    TerritoryData territoryData,
    QuestFunctions questFunctions,
    UiUtils uiUtils,
    RedoUtil redoUtil,
    Configuration configuration)
{

    public void Draw(IQuestInfo questInfo)
    {
        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        DrawInner(questInfo, true);
    }

    public void DrawInner(IQuestInfo questInfo, bool showItemRewards)
    {
        ImGui.Text($"{SeIconChar.LevelEn.ToIconString()}{questInfo.Level}");
        ImGui.SameLine();

        (Vector4 color, FontAwesomeIcon _, string tooltipText) = uiUtils.GetQuestStyle(questInfo.QuestId);
        ImGui.TextColored(color, tooltipText);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{questInfo.QuestId}");

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

        if (questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
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

            if (quest.Root.Comment != null)
                ImGui.Text($"Comment: {quest.Root.Comment.Split('\n', 2)[0]}");

            RedoIndex redoIndex = redoUtil.GetChapter(quest.Id.Value);
            if (redoIndex.Index != -1)
                ImGui.Text($"NG+: {redoIndex}");

            if (quest.Root.LastChecked.Date != null)
                ImGui.Text($"Last checked: {quest.Root.LastChecked.Date} by {quest.Root.LastChecked.Username}");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudRed, "NoQuestPath");
            if (questInfo is QuestInfo questInfo1)
                ImGui.Text($"{questInfo1.IssuerLocation.Territory.PlaceName.Value.Name}");
        }

        if (questInfo.AlliedSociety != EAlliedSociety.None)
            ImGui.Text($"Society: {questInfo.AlliedSociety}");

        if (questInfo is QuestInfo qInfo && qInfo.AlliedSocietyRank != EAlliedSocietyRank.None)
            ImGui.Text($"Rank: {qInfo.AlliedSocietyRank}{(!qInfo.IsRepeatable ? " (maxed)" : "")}");

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

            foreach (PreviousQuestInfo q in questInfo.PreviousQuests)
            {
                if (questData.TryGetQuestInfo(q.QuestId, out IQuestInfo? qInfo))
                {
                    (Vector4 iconColor, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(q.QuestId);
                    if (!questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = ImGuiColors.DalamudGrey;

                    uiUtils.ChecklistItem(
                        FormatQuestUnlockName(qInfo,
                            questFunctions.IsQuestComplete(q.QuestId) ? byte.MinValue : q.Sequence), iconColor, icon);

                    if (qInfo is QuestInfo qstInfo && (counter <= 2 || icon != FontAwesomeIcon.Check))
                        DrawQuestUnlocks(qstInfo, counter + 1, false);
                }
                else
                {
                    using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                    uiUtils.ChecklistItem($"Unknown Quest ({q.QuestId})", ImGuiColors.DalamudGrey,
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

                foreach (QuestId q in actualQuestInfo.QuestLocks)
                {
                    IQuestInfo qInfo = questData.GetQuestInfo(q);
                    (Vector4 iconColor, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(q);
                    if (!questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = ImGuiColors.DalamudGrey;

                    uiUtils.ChecklistItem(FormatQuestUnlockName(qInfo), iconColor, icon);
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

                foreach (ushort instanceId in actualQuestInfo.PreviousInstanceContent)
                {
                    string instanceName = territoryData.GetInstanceName(instanceId) ?? "?";
                    (Vector4 iconColor, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
                    uiUtils.ChecklistItem(instanceName, iconColor, icon);
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
                    var _ => "None"
                };

                GrandCompany currentGrandCompany = questFunctions.GetGrandCompany();
                uiUtils.ChecklistItem($"Grand Company: {gcName}", actualQuestInfo.GrandCompany == currentGrandCompany);
            }

            if (showItemRewards && actualQuestInfo.ItemRewards.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text("Item Rewards:");
                foreach (ItemReward reward in actualQuestInfo.ItemRewards)
                    ImGui.BulletText(reward.Name);
            }

            bool unlocksText = false;
            if (showItemRewards && actualQuestInfo.InstanceContentUnlock != 0)
            {
                ImGui.Separator();
                if (!unlocksText)
                {
                    ImGui.Text("Unlocks:");
                    unlocksText = true;
                }
                string instanceName = territoryData.GetInstanceName(actualQuestInfo.InstanceContentUnlock) ?? "?";
                (Vector4 iconColor, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(actualQuestInfo.InstanceContentUnlock);
                uiUtils.ChecklistItem(instanceName, iconColor, icon);
            }

            if (showItemRewards && actualQuestInfo.ActionUnlock.Count > 0)
            {
                ImGui.Separator();
                if (!unlocksText)
                {
                    ImGui.Text("Unlocks:");
                    unlocksText = true;
                }
                foreach (string reward in actualQuestInfo.ActionUnlock)
                    ImGui.BulletText(reward);
            }
        }

        if (counter > 0)
            ImGui.Unindent();
    }

    private string FormatQuestUnlockName(IQuestInfo questInfo, byte sequence = 0)
    {
        string name = questInfo.Name;
        if (configuration.Advanced.AdditionalStatusInformation && sequence != 0)
            name += $" {SeIconChar.ItemLevel.ToIconString()}";

        if (questInfo.IsMainScenarioQuest)
            name += $" ({questInfo.QuestId}, MSQ)";
        else
            name += $" ({questInfo.QuestId})";

        return name;
    }
}
