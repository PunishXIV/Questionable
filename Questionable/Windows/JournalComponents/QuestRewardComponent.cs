using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Model.Common.Converter;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class QuestRewardComponent
(
    QuestRegistry questRegistry,
    QuestData questData,
    JournalTable journalTable,
    QuestFunctions questFunctions,
    AetheryteData aetheryteData,
    AetheryteFunctions aetheryteFunctions,
    IDataManager dataManager,
    ILogger<QuestRewardComponent> logger)
{
    private bool _showEventRewards;
    private bool _hideUnobtainable;
    private bool _hideCompleted;
    private volatile uint _generation;
    private OrderedDictionary<EAetheryteLocation, List<QuestInfo>> _aetheryteUnlocks = [];
    private enum ELoadState { NotStarted, Loading, Ready }
    private volatile ELoadState _aetheryteLoadState = ELoadState.NotStarted;
    internal void ClearCache()
    {
        _generation++;
        _aetheryteUnlocks = [];
        _aetheryteLoadState = ELoadState.NotStarted;
        _taxiStandUnlockQuests = [];
    }

    public void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Unlocks"));
        if (!tab)
            return;

        ImGui.Checkbox(_L("Show rewards from seasonal event quests"), ref _showEventRewards);
        ImGui.Checkbox(_L("Hide Unobtainable Quests"), ref _hideUnobtainable);
        ImGui.Checkbox(_L("Hide unlocked items"), ref _hideCompleted);
        ImGui.Spacing();

        ImGui.BulletText(
            _L("Only untradeable items are listed (e.g. the Wind-up Airship can be sold on the market board)."));

        DrawAetheryteGroup();
        DrawChocoboPorterGroup();
        DrawGroup(_L("Duties"), EItemRewardType.Duty);
        DrawGroup(_L("Fashion Accessories"), EItemRewardType.FashionAccessory);
        DrawGroup(_L("Minions"), EItemRewardType.Minion);
        DrawGroup(_L("Mounts"), EItemRewardType.Mount);
        DrawGroup(_L("Orchestrion Rolls"), EItemRewardType.OrchestrionRoll);
        DrawGroup(_L("Triple Triad Cards"), EItemRewardType.TripleTriadCard);
    }

    private readonly List<ChocoboTaxiStand> _taxiStands = dataManager.GetExcelSheet<ChocoboTaxiStand>().ToList();
    private Dictionary<uint, List<Domain.Quest>> _taxiStandUnlockQuests = [];
    private static readonly HashSet<uint> _excludedTaxiStands = new HashSet<uint> {
            1179648, // Reginald Eventman I
            1179649, // Reginald Eventman II
            1179678, // （空き）placeholder
        };
    private unsafe void DrawChocoboPorterGroup()
    {
        if (!ImGui.CollapsingHeader($"{_T<Addon>(2730)}###RewardChocoboPorter"))
            return;
        var total = 0;
        var uiState = UIState.Instance();
        if (_taxiStandUnlockQuests.Count == 0)
        {
            Dictionary<uint, List<Domain.Quest>> standQuests = [];
            foreach (Domain.Quest quest in questRegistry.AllQuests)
                foreach (var (_, _, step) in quest.AllSteps())
                    if (step.InteractionType is EInteractionType.UnlockTaxiStand && step.TaxiStandId != null)
                        if (standQuests.TryGetValue(step.TaxiStandId.Value, out var value))
                            value.Add(quest);
                        else
                            standQuests[step.TaxiStandId.Value] = [quest];
            _taxiStandUnlockQuests = standQuests;
        }

        using ImRaii.TableDisposable table = journalTable.Begin("RewardChocoboPorter");
        if (!table)
            return;

        foreach (ChocoboTaxiStand taxiStand in _taxiStands)
        {
            if (_excludedTaxiStands.Contains(taxiStand.RowId))
                continue;
            var complete = uiState->IsChocoboTaxiStandUnlocked(taxiStand.RowId);
            if (_hideCompleted && complete) continue;
            JournalTable.BeginRow();
            ImGui.Text(taxiStand.PlaceName.ToMacroString());
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                ImGui.SetClipboardText(taxiStand.RowId.ToString(CultureInfo.InvariantCulture));
            if (_taxiStandUnlockQuests.TryGetValue(taxiStand.RowId, out var value))
                foreach (var quest in value)
                {
                    bool isUnobtainable = questFunctions.IsQuestUnobtainable(quest.Id);
                    if (_hideUnobtainable && isUnobtainable)
                        continue;
                    var info = quest.GetQuestInfo();
                    bool isEventQuest = info.IsSeasonalEvent;
                    if (!_showEventRewards && isEventQuest)
                        continue;
                    using var _ = ImRaii.PushId($"###{(int)taxiStand.RowId}-{quest.Id.Value}");
                    journalTable.QuestRow(info, nameof(QuestRewardComponent));
                }

            total++;
        }
        if (total == 0)
            DrawNoResults();
    }

    private void DrawAetheryteGroup()
    {
        if (!ImGui.CollapsingHeader($"{_T<HowTo>(15)}###RewardAetheryte"))
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
        using ImRaii.TableDisposable table = journalTable.Begin("RewardAetheryte");
        if (!table)
            return;

        var total = 0;
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
            JournalTable.BeginRow();
            ImGui.Text(aetheryteName);
            if (ImGui.IsItemHovered() && aetheryteData.TerritoryIds.TryGetValue(aetheryteLocation, out var territoryId))
            {
                ImGui.SetTooltip(TerritoryData.GetNameAndId(territoryId));
            }
            foreach (QuestInfo info in results)
            {
                bool isEventQuest = info.IsSeasonalEvent;
                if (!_showEventRewards && isEventQuest)
                    continue;
                bool isUnobtainable = questFunctions.IsQuestUnobtainable(info.QuestId);
                if (_hideUnobtainable && isUnobtainable)
                    continue;
                using var _ = ImRaii.PushId($"###{(int)aetheryteLocation}-{info.QuestId.Value}");
                journalTable.QuestRow(info, nameof(QuestRewardComponent));
            }
            total++;
        }
        if (total == 0)
            DrawNoResults();
    }

    private void DrawGroup(string label, EItemRewardType type)
    {
        if (!ImGui.CollapsingHeader($"{label}###Reward{type}"))
            return;

        using ImRaii.TableDisposable table = journalTable.Begin($"Reward{type}");
        if (!table)
            return;

        int total = 0;
        if (type is EItemRewardType.Duty)
        {
            var dutyQuests = questRegistry.AllQuests
                    .Where(x => x.Id is QuestId &&
                           ((QuestInfo)x.Info).CfcUnlock != null &&
                           !questFunctions.IsQuestUnobtainable(x.Id))
                    .OrderBy(x => x.Id.Value).ToList();
            foreach (QuestInfo info in dutyQuests.Select(x => (QuestInfo)x.Info))
            {
                ContentFinderCondition cfc = info.CfcUnlock!.Value;
                if (cfc.Name.ByteLength == 0)
                    continue;
                bool complete = questFunctions.IsQuestComplete(info.QuestId);
                if (_hideCompleted && complete)
                    continue;
                journalTable.QuestRow(info, nameof(QuestRewardComponent), $"{cfc.Name.ToDalamudString()} ({cfc.RowId})", complete);
                total++;
            }
        }
        else
        {
            foreach (ItemReward item in questData.RedeemableItems.Where(x => x.Type == type)
                         .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!questData.TryGetQuestInfo(item.ElementId, out IQuestInfo? questInfo))
                    continue;
                bool isEventQuest = questInfo is QuestInfo { IsSeasonalEvent: true };
                if (!_showEventRewards && isEventQuest)
                    continue;
                if (_hideUnobtainable && questFunctions.IsQuestUnobtainable(item.ElementId))
                    continue;
                bool complete = item.IsUnlocked();
                if (_hideCompleted && complete)
                    continue;
                string name = isEventQuest ? $"{item.Name} {SeIconChar.Clock.ToIconString()}" : item.Name;
                journalTable.QuestRow(questInfo, nameof(QuestRewardComponent), name, complete);
                total++;
            }
        }

        if (total == 0)
            DrawNoResults();
    }

    private static void DrawNoResults()
    {
        JournalTable.BeginRow();
        ImGui.Text(_L("No results"));
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
                var unlocks = new OrderedDictionary<EAetheryteLocation, List<QuestInfo>>();
                foreach (EAetheryteLocation location in AetheryteData.Aetherytes)
                {
                    if (location is EAetheryteLocation.None) continue;
                    unlocks[location] = obtainableQuests
                        .Where(x => x.AllSteps().Any(entry =>
                            (entry.Step.InteractionType is EInteractionType.AttuneAetheryte && entry.Step.Aetheryte.Equals(location)) ||
                            (entry.Step.InteractionType is EInteractionType.AttuneAethernetShard && entry.Step.AethernetShard.Equals(location))))
                        .Select(x => (QuestInfo)x.Info)
                        .ToList();
                    logger.LogTrace("AetheryteBuild{Generation}: Found {Count} for {Loc}", currentGeneration, unlocks[location].Count, location);
                    Thread.MemoryBarrier();
                    if (_generation != currentGeneration)
                    {
                        logger.LogInformation("AetheryteBuild{Generation}: Quests were reloaded, discarding build.", currentGeneration);
                        return;
                    }
                }
                if (_generation == currentGeneration)
                {
                    _aetheryteUnlocks = unlocks;
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
