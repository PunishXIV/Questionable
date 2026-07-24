using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Common;
namespace Questionable.Windows.JournalComponents;

internal sealed class QuestRewardComponent
(
    QuestRegistry questRegistry,
    QuestData questData,
    QuestTooltipComponent questTooltipComponent,
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
    }

    private void DrawGroup(string label, EItemRewardType type)
    {
        if (!ImGui.CollapsingHeader($"{label}###Reward{type}"))
            return;

        var results = questData.RedeemableItems.Where(x => x.Type == type)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (results.Count == 0)
            ImGui.Text("No results");

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
                    ? ImGuiColors.DalamudGrey
                    : complete
                        ? ImGuiColors.ParsedGreen
                        : ImGuiColors.DalamudRed;
                FontAwesomeIcon icon = complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
                if (uiUtils.ChecklistItem(name, color, icon))
                {
                    using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                    if (item.Type is not EItemRewardType.TripleTriadCard)
                        ImGui.Text(_LF("Obtained from: {0}", questInfo.Name));
                    using (ImRaii.PushIndent())
                    {
                        questTooltipComponent.DrawInner(questInfo, showItemRewards: false);
                    }
                }
            }
        }
    }
}
