using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using static Questionable.Domain.QuestInfo;
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
        DrawInner(questInfo, showItemRewards: true);
    }

    public void DrawInner(IQuestInfo questInfo, bool showItemRewards)
    {
        unsafe
        {
            string lvlString = $"{SeIconChar.LevelEn.ToIconString()}{questInfo.Level}";
            if (PlayerState.Instance()->CurrentLevel < questInfo.Level)
                ImGui.TextColored(QstTheme.Danger, lvlString);
            else
                ImGui.Text(lvlString);
        }
        ImGui.SameLine();

        (Vector4 color, FontAwesomeIcon _, string tooltipText) = uiUtils.GetQuestStyle(questInfo.QuestId);
        ImGui.TextColored(color, tooltipText);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{questInfo.QuestId}");

        if (questInfo is QuestInfo { IsSeasonalEvent: true })
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_L("Event"));
        }

        if (questInfo.IsRepeatable)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_L("Repeatable"));
        }

        if (questInfo is QuestInfo { CompletesInstantly: true })
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_L("Instant"));
        }

        if (questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
        {
            if (quest.Root.Disabled)
            {
                ImGui.SameLine();
                ImGui.TextColored(QstTheme.Danger, _L("Disabled"));
            }

            if (quest.Root.Author.Count == 1)
                ImGui.Text(_LF("Author: {0}", quest.Root.Author[0]));
            else
                ImGui.Text(_LF("Authors: {0}", string.Join(", ", quest.Root.Author)));

            if (quest.Root.Comment != null)
                ImGui.Text(_LF("Comment: {0}", quest.Root.Comment.Split('\n', 2)[0]));

            RedoIndex redoIndex = redoUtil.GetChapter(quest.Id.Value);
            if (redoIndex.Index != -1)
                ImGui.Text(_LF("NG+: {0}", redoIndex));

            if (quest.Root.LastChecked.Date != null)
                ImGui.Text(_LF("Last checked: {0} by {1}", quest.Root.LastChecked.Date, quest.Root.LastChecked.Username?.ToString() ?? ""));
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(QstTheme.Danger, _L("NoQuestPath"));
            if (questInfo is QuestInfo questInfo1)
                ImGui.Text($"{questInfo1.IssuerLocation.Territory.PlaceName.Value.Name}");
        }

        if (questInfo.AlliedSociety != EAlliedSociety.None)
            ImGui.Text(_LF("Society: {0}", questInfo.AlliedSociety));

        if (questInfo is QuestInfo qInfo && qInfo.AlliedSocietyRank != EAlliedSocietyRank.None)
            ImGui.Text(_LF("Rank: {0}{1}", qInfo.AlliedSocietyRank, (!qInfo.IsRepeatable ? " (maxed)" : "")));

        DrawQuestUnlocks(questInfo, 0, showItemRewards);
    }

    private void PopulatePrereqCache(ushort topLevelId, IQuestInfo questInfo, int depth = 0)
    {
        if (depth >= 20) return;
        if (depth != 0 && questInfo.IsMainScenarioQuest) return;

        foreach (PreviousQuestInfo q in questInfo.PreviousQuests)
        {
            if (!questData.TryGetQuestInfo(q.QuestId, out IQuestInfo? qInfo)) continue;

            // Populate the top-level entry (full tree)
            questFunctions.prereqCache[topLevelId].Add(qInfo);

            // Recursively ensure this node's own subtree is also fully cached
            if (!questFunctions.prereqCache.ContainsKey(qInfo.QuestId.Value))
            {
                questFunctions.prereqCache[qInfo.QuestId.Value] = [];
                PopulatePrereqCache(qInfo.QuestId.Value, qInfo, 0); // full depth for subtree
            }

            if (qInfo is QuestInfo qstInfo)
                PopulatePrereqCache(topLevelId, qstInfo, depth + 1);
        }
    }

    private readonly HashSet<IQuestInfo> _shownAlready = [];
    private IQuestInfo _currentTopLevel;
    private void DrawQuestUnlocks(IQuestInfo questInfo, int counter, bool showItemRewards)
    {
        if (counter == 0)
        {
            _shownAlready.Clear();
            _currentTopLevel = questInfo;
            if (!questFunctions.prereqCache.ContainsKey(_currentTopLevel.QuestId.Value))
            {
                questFunctions.prereqCache[_currentTopLevel.QuestId.Value] = [];
                PopulatePrereqCache(_currentTopLevel.QuestId.Value, _currentTopLevel);
            }
        }
        if (counter >= 20)
            return;

        if (counter != 0 && questInfo.IsMainScenarioQuest)
            return;

        if (counter > 0)
            ImGui.Indent();

        if (questInfo.PreviousQuests.Count > 0)
        {
            if (counter == 0)
                ImGui.Separator();

            if (questInfo.PreviousQuests.Count > 1 && counter < 10)
            {
                if (questInfo.PreviousQuestJoin == EQuestJoin.All && questInfo.PreviousQuests.Count > 2)
                    ImGui.Text(_L("Requires all:"));
                else if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne)
                    ImGui.Text(_L("Requires:"));
            }

            foreach (PreviousQuestInfo q in questInfo.PreviousQuests)
            {
                if (questData.TryGetQuestInfo(q.QuestId, out IQuestInfo? qInfo))
                {
                    questFunctions.prereqCache[_currentTopLevel.QuestId.Value].Add(qInfo);
                    (Vector4 iconColor, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(q.QuestId);
                    if (!questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = QstTheme.TextMuted;

                    if (!_shownAlready.Contains(qInfo))
                    {
                        uiUtils.ChecklistItem(
                            FormatQuestUnlockName(qInfo,
                                questFunctions.IsQuestComplete(q.QuestId) ? byte.MinValue : q.Sequence), iconColor, icon);
                        _shownAlready.Add(qInfo);
                    }

                    if (qInfo is QuestInfo qstInfo && (counter <= 2 || icon != FontAwesomeIcon.Check))
                        DrawQuestUnlocks(qstInfo, counter + 1, showItemRewards: false);
                }
                else
                {
                    using ImRaii.DisabledDisposable _ = ImRaii.Disabled();
                    uiUtils.ChecklistItem(_LF("Unknown Quest ({0})", q.QuestId), QstTheme.TextMuted,
                        FontAwesomeIcon.Question);
                }
            }
        }

        if (questInfo is QuestInfo actualQuestInfo)
        {
            if (actualQuestInfo.MoogleDeliveryLevel > 0 || actualQuestInfo.IsMoogleDeliveryQuest)
                ImGui.Text(_LF("Requires Carrier Level {0}", actualQuestInfo.MoogleDeliveryLevel));


            if (counter == 0 && actualQuestInfo.QuestLocks.Count > 0)
            {
                ImGui.Separator();
                if (actualQuestInfo.QuestLocks.Count > 1)
                {
                    if (actualQuestInfo.QuestLockJoin == EQuestJoin.All)
                        ImGui.Text(_L("Blocked by (if all completed):"));
                    else if (actualQuestInfo.QuestLockJoin == EQuestJoin.AtLeastOne)
                        ImGui.Text(_L("Blocked by (if at least completed):"));
                }
                else
                    ImGui.Text(_L("Blocked by (if completed):"));

                foreach (QuestId q in actualQuestInfo.QuestLocks)
                {
                    IQuestInfo qInfo = questData.GetQuestInfo(q);
                    (Vector4 iconColor, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(q);
                    if (!questRegistry.IsKnownQuest(qInfo.QuestId))
                        iconColor = QstTheme.TextMuted;

                    uiUtils.ChecklistItem(FormatQuestUnlockName(qInfo), iconColor, icon);
                }
            }

            if (counter == 0 && actualQuestInfo.PreviousInstanceContent.Count > 0)
            {
                ImGui.Separator();
                if (actualQuestInfo.PreviousInstanceContent.Count > 1)
                {
                    if (questInfo.PreviousQuestJoin == EQuestJoin.All)
                        ImGui.Text(_L("Requires all:"));
                    else if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne)
                        ImGui.Text(_L("Requires one:"));
                }
                else
                    ImGui.Text(_L("Requires:"));

                foreach (ushort instanceId in actualQuestInfo.PreviousInstanceContent)
                {
                    string instanceName = territoryData.GetInstanceName(instanceId) ?? _L("?");
                    (Vector4 iconColor, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
                    uiUtils.ChecklistItem(instanceName, iconColor, icon);
                }
            }

            if (counter == 0 && actualQuestInfo.GrandCompany != GrandCompany.None)
            {
                ImGui.Separator();
                string gcName = actualQuestInfo.GrandCompany.ToFormattedText();

                GrandCompany currentGrandCompany = questFunctions.GetGrandCompany();
                uiUtils.ChecklistItem(_LF("Grand Company: {0}", gcName), actualQuestInfo.GrandCompany == currentGrandCompany);
            }

            if (counter == 0 && actualQuestInfo.GrandCompanyRank != EGrandCompanyRank.None)
            {
                ImGui.Separator();
                string gcRankName = actualQuestInfo.GrandCompanyRank.ToFormattedText();

                EGrandCompanyRank currentGrandCompanyRank = questFunctions.GetGrandCompanyRank();
                uiUtils.ChecklistItem(_LF("GC Rank: {0} (#{1} >= #{2})", gcRankName, (byte)actualQuestInfo.GrandCompanyRank, (byte)questFunctions.GetGrandCompanyRank()), actualQuestInfo.GrandCompanyRank == currentGrandCompanyRank);
            }

            if (showItemRewards && actualQuestInfo.ItemRewards.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text(_L("Item Rewards:"));
                foreach (ItemReward reward in actualQuestInfo.ItemRewards)
                    ImGui.BulletText(reward.Name);
            }

            bool unlocksText = false;
            if (showItemRewards && actualQuestInfo.InstanceContentUnlock != 0)
            {
                ImGui.Separator();
                if (!unlocksText)
                {
                    ImGui.Text(_L("Unlocks:"));
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
                    ImGui.Text(_L("Unlocks:"));
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
