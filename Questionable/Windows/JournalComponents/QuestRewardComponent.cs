using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
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
    UiUtils uiUtils,
    AetheryteData aetheryteData,
    AetheryteFunctions aetheryteFunctions,
    ILogger<QuestRewardComponent> logger)
{
    private bool _showEventRewards;
    private bool _hideCompleted;
    private volatile uint _generation;
    private OrderedDictionary<EAetheryteLocation, List<QuestInfo>> _aetheryteUnlocks = [];
    private enum ELoadState { NotStarted, Loading, Ready }
    private volatile ELoadState _aetheryteLoadState = ELoadState.NotStarted;
    internal void RefreshCounts()
    {
        _generation++;
        _aetheryteUnlocks = [];
        _aetheryteLoadState = ELoadState.NotStarted;
    }

    public void DrawItemRewards()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Unlocks"));
        if (!tab)
            return;

        ImGui.Checkbox(_L("Show rewards from seasonal event quests"), ref _showEventRewards);
        ImGui.Checkbox(_L("Hide unlocked items"), ref _hideCompleted);
        ImGui.Spacing();

        ImGui.BulletText(
            _L("Only untradeable items are listed (e.g. the Wind-up Airship can be sold on the market board)."));

        DrawAetheryteGroup();
        DrawGroup(_L("Duties"), EItemRewardType.Duty);
        DrawGroup(_L("Fashion Accessories"), EItemRewardType.FashionAccessory);
        DrawGroup(_L("Minions"), EItemRewardType.Minion);
        DrawGroup(_L("Mounts"), EItemRewardType.Mount);
        DrawGroup(_L("Orchestrion Rolls"), EItemRewardType.OrchestrionRoll);
        DrawGroup(_L("Triple Triad Cards"), EItemRewardType.TripleTriadCard);
    }
    private void DrawAetheryteGroup()
    {
        if (!ImGui.CollapsingHeader($"{_T<HowTo>(15)}###RewardComponent"))
            return;
        switch (_aetheryteLoadState)
        {
            case ELoadState.NotStarted:
                _aetheryteLoadState = ELoadState.Loading;
                StartAetheryteBuild();
                ImGui.Text(_L("Loading..."));
                return;
            case ELoadState.Loading:
                ImGui.Text(_L("Loading..."));
                return;
        }
        foreach (EAetheryteLocation aetheryteLocation in AetheryteData.Aetherytes)
        {
            if (aetheryteLocation is EAetheryteLocation.None) continue;
            if (!_aetheryteUnlocks.TryGetValue(aetheryteLocation, out var results)) continue;
            if (aetheryteLocation is EAetheryteLocation.None)
                continue;
            if ((results.Count == 0 && aetheryteLocation.IsAethernetShard()))
                continue;
            if (_hideCompleted && aetheryteFunctions.IsAetheryteUnlocked(aetheryteLocation))
                continue;
            if (!AetheryteConverter.Values.TryGetValue(aetheryteLocation, out string? aetheryteName))
                aetheryteName = aetheryteLocation.ToString();
            ImGui.Text(aetheryteName);
            if (ImGui.IsItemHovered() && aetheryteData.TerritoryIds.TryGetValue(aetheryteLocation, out var tId))
            {
                ImGui.SetTooltip(TerritoryData.GetNameAndId(tId));
            }
            foreach (QuestInfo q in results)
            {
                using var _ = ImRaii.PushId($"###{(int)aetheryteLocation}-{q.QuestId.Value}");
                (Vector4 color, FontAwesomeIcon icon, string status) = uiUtils.GetQuestStyle(q.QuestId);
                if (uiUtils.ChecklistItem(q.Name, color, icon, iconOverride: QuestJournalUtils.GetIconOverride(q, icon)))
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
            ImGui.Separator();
        }
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
                if (_hideCompleted && complete)
                    continue;
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
    private void StartAetheryteBuild()
    {
        var currentGeneration = _generation;
        var obtainableQuests = questRegistry.AllQuests.Where(x => !questFunctions.IsQuestUnobtainable(x.Id)).ToList();
        Task.Factory.StartNew(() =>
        {
            logger.LogInformation("StartAetheryteBuild{Generation}", currentGeneration);
            try
            {
                var dict = new OrderedDictionary<EAetheryteLocation, List<QuestInfo>>();
                foreach (EAetheryteLocation loc in AetheryteData.Aetherytes)
                {
                    if (loc is EAetheryteLocation.None) continue;
                    dict[loc] = obtainableQuests
                        .Where(x => x.AllSteps().Any(a =>
                            (a.Step.InteractionType is EInteractionType.AttuneAetheryte && a.Step.Aetheryte.Equals(loc)) ||
                            (a.Step.InteractionType is EInteractionType.AttuneAethernetShard && a.Step.AethernetShard.Equals(loc))))
                        .Select(x => (QuestInfo)x.Info)
                        .ToList();
                    logger.LogTrace("AetheryteBuild{Generation}: Found {Count} for {Loc}", currentGeneration, dict[loc].Count, loc);
                    Thread.MemoryBarrier();
                    if (_generation != currentGeneration)
                    {
                        logger.LogInformation("AetheryteBuild{Generation}: Quests were reloaded, discarding build.", currentGeneration);
                        return;
                    }
                }
                if (_generation == currentGeneration)
                {
                    _aetheryteUnlocks = dict;
                    _aetheryteLoadState = ELoadState.Ready;
                }
                else
                    logger.LogInformation("AetheryteBuild{Generation}: Quests were reloaded, discarding build.", currentGeneration);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to build aetheryte unlock list");
                if (_generation == currentGeneration)
                    _aetheryteLoadState = ELoadState.Ready;
            }
            logger.LogInformation("AetheryteBuild{Generation} complete", currentGeneration);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
