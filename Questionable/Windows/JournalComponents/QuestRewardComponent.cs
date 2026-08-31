using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class QuestRewardComponent
(
    QuestRegistry questRegistry,
    QuestData questData,
    QuestTooltipComponent questTooltipComponent,
    QuestFunctions questFunctions,
    QuestJournalUtils questJournalUtils,
    UiUtils uiUtils)
{
    private bool _showEventRewards;

    public void DrawItemRewards()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Item Rewards"));
        if (!tab)
            return;

        ImGui.Checkbox(_L("Show rewards from seasonal event quests"), ref _showEventRewards);
        ImGui.Spacing();

        ImGui.BulletText(
            _L("Only untradeable items are listed (e.g. the Wind-up Airship can be sold on the market board)."));

        DrawGroup(_L("Mounts"), EItemRewardType.Mount);
        DrawGroup(_L("Minions"), EItemRewardType.Minion);
        DrawGroup(_L("Orchestrion Rolls"), EItemRewardType.OrchestrionRoll);
        DrawGroup(_L("Triple Triad Cards"), EItemRewardType.TripleTriadCard);
        DrawGroup(_L("Fashion Accessories"), EItemRewardType.FashionAccessory);
        DrawGroup(_L("Duties"), EItemRewardType.Duty);
    }

    private void DrawGroup(string label, EItemRewardType type)
    {
        if (!ImGui.CollapsingHeader($"{label}###Reward{type}"))
            return;

        if (type is EItemRewardType.Duty)
        {
            var resultsDuties = questRegistry.AllQuests
                    .Where(x => x.Id is QuestId &&
                           ((QuestInfo)x.Info).CfcUnlock != null &&
                           !questFunctions.IsQuestUnobtainable(x.Id))
                    .OrderBy(x => x.Id.Value).ToList();
            if (resultsDuties.Count == 0)
                ImGui.Text(_L("No results"));
            foreach (QuestInfo q in resultsDuties.Select(x => (QuestInfo)x.Info))
            {
                ContentFinderCondition cfc = q.CfcUnlock!.Value;
                if (cfc.Name.ByteLength == 0)
                    continue;
                string name = $"{cfc.Name.ToDalamudString()} ({cfc.RowId})";
                bool complete = questFunctions.IsQuestComplete(q.QuestId);
                Vector4 color = !questRegistry.IsKnownQuest(q.QuestId)
                    ? QstTheme.TextMuted
                    : complete
                        ? QstTheme.Success
                        : QstTheme.Danger;
                FontAwesomeIcon icon = complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
                if (uiUtils.ChecklistItem(name, color, icon, iconOverride: QuestJournalUtils.GetIconOverride(q, icon)))
                {
                    using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                    ImGui.Text(_LF("Obtained from: {0}", q.Name));
                    using (ImRaii.PushIndent())
                    {
                        questTooltipComponent.DrawInner(q, showItemRewards: false);
                    }
                }
                questRegistry.TryGetQuest(q.QuestId, out Domain.Quest? quest);
                questJournalUtils.ShowContextMenu(q, quest, nameof(QuestRewardComponent));
            }
            return;
        }

        var results = questData.RedeemableItems.Where(x => x.Type == type)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (results.Count == 0)
            ImGui.Text(_L("No results"));

        foreach (ItemReward item in results)
        {
            if (questData.TryGetQuestInfo(item.ElementId, out IQuestInfo? questInfo))
            {
                bool isEventQuest = questInfo is QuestInfo { IsSeasonalEvent: true };
                if (!_showEventRewards && isEventQuest)
                    continue;

                string name = item.Name;
                if (isEventQuest)
                    name += $" {SeIconChar.Clock.ToIconString()}";

                bool complete = item.IsUnlocked();
                Vector4 color = !questRegistry.IsKnownQuest(item.ElementId)
                    ? QstTheme.TextMuted
                    : complete
                        ? QstTheme.Success
                        : QstTheme.Danger;
                FontAwesomeIcon icon = complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
                if (uiUtils.ChecklistItem(name, color, icon, iconOverride: QuestJournalUtils.GetIconOverride((QuestInfo)questInfo, icon)))
                {
                    using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                    if (item.Type is not EItemRewardType.TripleTriadCard)
                        ImGui.Text(_LF("Obtained from: {0}", questInfo.Name));
                    using (ImRaii.PushIndent())
                    {
                        questTooltipComponent.DrawInner(questInfo, showItemRewards: false);
                    }
                }
                questRegistry.TryGetQuest(questInfo.QuestId, out Domain.Quest? quest);
                questJournalUtils.ShowContextMenu(questInfo, quest, nameof(QuestRewardComponent));
            }
        }
    }
}
