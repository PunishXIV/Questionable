using ECommons.ExcelServices;
using static Questionable.Domain.QuestInfo;
using static Questionable.Utils.CacheUtils;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Functions;

[RegisterSingleton]
internal sealed class QuestFunctions
(
    IQuestCatalog catalog,
    IQuestGameState state,
    AlliedSocietyData alliedSocietyData,
    Configuration configuration,
    IChatGui chatGui)
{
    internal static readonly int[] WhiteWolfGateQuests = [439, 1080, 3870, 33];
    internal Dictionary<ushort, HashSet<IQuestInfo>> prereqCache = [];

    internal void PopulatePrereqCache(ushort topLevelId, IQuestInfo questInfo, int depth = 0)
    {
        if (depth >= 20) return;
        if (depth != 0 && questInfo.IsMainScenarioQuest) return;
        if (!prereqCache.ContainsKey(topLevelId))
            prereqCache[topLevelId] = [];

        foreach (PreviousQuestInfo q in questInfo.PreviousQuests)
        {
            if (!catalog.TryGetQuestInfo(q.QuestId, out IQuestInfo? qInfo)) continue;

            prereqCache[topLevelId].Add(qInfo);

            if (!prereqCache.ContainsKey(qInfo.QuestId.Value))
            {
                prereqCache[qInfo.QuestId.Value] = [];
                PopulatePrereqCache(qInfo.QuestId.Value, qInfo, 0); // full depth for subtree
            }

            if (qInfo is QuestInfo qstInfo)
                PopulatePrereqCache(topLevelId, qstInfo, depth + 1);
        }
    }

    private ushort? _randomCompanyQuest;
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
    public QuestReference GetCurrentQuest(bool allowNewMsq = true, bool skipPriority = false)
    {
        QuestReference internalQuest = GetCurrentQuestInternal(allowNewMsq, skipPriority);
        (ElementId? currentQuest, byte sequence, MainScenarioQuestState questState) = internalQuest;

        if (currentQuest == null || currentQuest.Value == 0)
        {
            if (state.TerritoryType == 181) // Starting in Limsa
            {
                return new(new QuestId(107), 0, MainScenarioQuestState.Available);
            }

            if (state.TerritoryType == 182) // Starting in Ul'dah
            {
                return new(new QuestId(594), 0, MainScenarioQuestState.Available);
            }

            if (state.TerritoryType == 183) // Starting in Gridania
            {
                return new(new QuestId(39), 0, MainScenarioQuestState.Available);
            }

            return QuestReference.NoQuest(questState);
        }

        if (currentQuest.Value == 681)
        {
            if (IsQuestAccepted(currentQuest) || IsQuestComplete(currentQuest))
                return new(currentQuest, sequence, questState);

            if (configuration.General.GrandCompany.Equals(GrandCompany.None))
            {
                if (_randomCompanyQuest == null)
                {
                    Random rand = new();
                    _randomCompanyQuest = (ushort)(rand.Next() % 2 + 680);
                }
                return new(new QuestId(_randomCompanyQuest.Value), 0, questState);
            }
            return configuration.General.GrandCompany switch
            {
                GrandCompany.TwinAdder => new(new QuestId(680), 0, questState),
                GrandCompany.Maelstrom => new(new QuestId(681), 0, questState),
                GrandCompany.ImmortalFlames => new(new QuestId(682), 0, questState),
                var _ => QuestReference.NoQuest(MainScenarioQuestState.Unavailable)
            };
        }

        if (currentQuest.Value == 3856 && !state.IsMountUnlocked(1)) // we come in peace
        {
            ushort chocoboQuest = (GrandCompany)state.GrandCompany switch
            {
                GrandCompany.TwinAdder => 700,
                GrandCompany.Maelstrom => 701,
                GrandCompany.ImmortalFlames => 702,
                var _ => 0
            };

            if (chocoboQuest != 0 && !state.IsQuestComplete(chocoboQuest))
                return new(new QuestId(chocoboQuest), state.GetQuestSequence(chocoboQuest), questState);
        }
        else if (WhiteWolfGateQuests.Contains(currentQuest.Value) &&
                 !state.IsAetheryteUnlocked(EAetheryteLocation.GridaniaWhiteWolfGate))
        {
            // quests that use white wolf gate, finish 'broadening horizons' to unlock it
            if (!state.IsAetheryteUnlocked(EAetheryteLocation.GridaniaBlueBadgerGate))
            {
                chatGui.Print(_L("This quest uses the White Wolf Gate, which requires that you unlock all aethernet shards in Gridania.\n") +
                                   _L("This should have happened as part of the quest \"Close To Home\" if starting in Gridania, or \"The Ul'dahn/Lominsan Envoy\" for the other cities.\n") +
                                   _L("Please unlock the aethernet shards, or complete the current quest sequence manually before continuing."),
                               CommandHandler.MessageTag, CommandHandler.TagColor);
                throw new TeleportLockedException("Required aethernet shard not unlocked");
            }

            QuestId broadeningHorizons = new(802);
            return new(broadeningHorizons, state.GetQuestSequence(broadeningHorizons.Value), questState);
        }

        return internalQuest;
    }

    public QuestReference GetCurrentQuestInternal(bool allowNewMsq, bool skipPriority = false)
    {
        if (!state.HasQuestManager)
            return QuestReference.NoQuest(MainScenarioQuestState.Unavailable);

        // Prefer accepting MSQ quests, or a turn-in sends the run off on side quests.
        (QuestReference msqQuest, string? _) = GetMainScenarioQuest();
        if (msqQuest.CurrentQuest != null && !catalog.IsKnownQuest(msqQuest.CurrentQuest) ||
            SetAsideQuests.Contains(msqQuest.CurrentQuest, msqQuest.Sequence))
            msqQuest = QuestReference.NoQuest(msqQuest.State);

        if (msqQuest.CurrentQuest != null && !IsQuestAccepted(msqQuest.CurrentQuest))
        {
            if (allowNewMsq)
                return !skipPriority && GetClassQuestBeforeMsq(msqQuest.CurrentQuest) is { } classQuest
                    ? new(classQuest, state.GetQuestSequence(classQuest.Value), msqQuest.State)
                    : msqQuest;

            msqQuest = QuestReference.NoQuest(msqQuest.State);
        }

        // Follow the to-do list order: its first item is the quest the player picked.
        List<(ElementId Quest, byte Sequence)> trackedQuests = [];
        foreach (ushort trackedQuestId in state.TrackedQuestIds)
        {
            ElementId currentQuest = new QuestId(trackedQuestId);
            byte sequence = state.GetQuestSequence(trackedQuestId);
            if (catalog.IsKnownQuest(currentQuest) && !SetAsideQuests.Contains(currentQuest, sequence))
                trackedQuests.Add((currentQuest, sequence));
        }

        if (configuration.General.SkipLowPriorityDuties && trackedQuests.Count > 0)
        {
            IReadOnlyList<(uint CfcId, ElementId QuestId, int Sequence)> lowPriorityQuests = catalog.LowPriorityCfcQuests;
            trackedQuests.RemoveAll(x => lowPriorityQuests.Any(y => x.Quest == y.QuestId && x.Sequence == y.Sequence));
        }

        if (trackedQuests.Count > 0)
        {
            (ElementId firstTrackedQuest, byte firstTrackedSequence) = trackedQuests[0];
            EAlliedSociety firstTrackedAlliedSociety =
                CommonTurnIn(firstTrackedQuest);
            if (firstTrackedAlliedSociety != EAlliedSociety.None)
            {
                // The to-do list only tracks a few quests; the society's other accepted dailies count too.
                List<(ElementId Quest, byte Sequence)> sameSocietyQuests = [.. trackedQuests.Skip(1)
                    .Concat(state.NormalQuests.Where(x => !x.IsHidden)
                        .Select(x => (Quest: (ElementId)new QuestId(x.QuestId), x.Sequence)))
                    .Where(quest => quest.Quest != firstTrackedQuest && catalog.IsKnownQuest(quest.Quest) &&
                                    CommonTurnIn(quest.Quest) == firstTrackedAlliedSociety)
                    .DistinctBy(x => x.Quest)];
                if (sameSocietyQuests.Count > 0)
                {
                    // Craft materials come with a timed buff (Ixal); craft before going off to another daily.
                    if (GetWaitingCraft([(firstTrackedQuest, firstTrackedSequence), .. sameSocietyQuests]) is { } craft)
                        return new(craft.Quest, craft.Sequence, msqQuest.State);

                    if (firstTrackedSequence == 255)
                    {
                        List<(ElementId Quest, byte Sequence)> unfinished = [.. sameSocietyQuests.Where(x =>
                            x.Sequence != 255 || (catalog.TryGetQuest(x.Quest, out Quest? quest) && !IsReadyToCompleteQuest(quest)))];
                        if (unfinished.Count > 0)
                        {
                            (ElementId questId, byte sequence) = PreferThisZone(unfinished);
                            return new(questId, sequence, msqQuest.State);
                        }
                    }
                    else if (!IsOnAlliedSocietyMount())
                    {
                        // Some Vanu dailies need a talk with an NPC near the issuer first.

                        sameSocietyQuests.Insert(0, (firstTrackedQuest, firstTrackedSequence));

                        alliedSocietyData.GetCommonNpcs(firstTrackedAlliedSociety,
                            out uint[]? normalNpcs,
                            out uint[] _);

                        if (normalNpcs.Length > 0)
                        {
                            (ElementId, byte)? talkToNormalNpcs = sameSocietyQuests
                                // Ixal's npcs stand in other zones; only the ones here are a quick stop.
                                .Where(x => x.Sequence < 255 && IsInteractSequence(x.Quest, x.Sequence, normalNpcs) &&
                                            StageZone(x.Quest, x.Sequence) == state.TerritoryType)
                                .Cast<(ElementId, byte)?>()
                                .FirstOrDefault();
                            if (talkToNormalNpcs != null)
                                return new(talkToNormalNpcs.Value.Item1, talkToNormalNpcs.Value.Item2, msqQuest.State);
                        }

                        (ElementId questId, byte sequence) = PreferThisZone([.. sameSocietyQuests.Where(x => x.Sequence < 255)]);
                        return new(questId, sequence, msqQuest.State);
                    }
                }
            }

            return new(firstTrackedQuest, firstTrackedSequence, msqQuest.State);
        }

        ElementId? priorityQuest = skipPriority ? null : NextPriorityQuests
            .Where(x => x.IsAvailable && !SetAsideQuests.Contains(x.QuestId, state.GetQuestSequence(x.QuestId.Value)))
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (priorityQuest != null)
        {
            return new(priorityQuest, state.GetQuestSequence(priorityQuest.Value), msqQuest.State);
        }

        if (msqQuest.CurrentQuest != null)
        {
            return msqQuest;
        }

        return QuestReference.NoQuest(msqQuest.State);
    }

    public (QuestReference, string?) GetMainScenarioQuestId() => ReadMainScenarioQuest(out _);

    private (QuestReference, string?) ReadMainScenarioQuest(out bool newGamePlus)
    {
        newGamePlus = false;
        if (configuration.Advanced.StopMSQ)
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), "Stop MSQ");
        if (state.IsQuestComplete(3759) && state.NewGamePlusQuestId is { } newGamePlusQuestId) // Memories Rekindled
        {
            newGamePlus = true;
            return (new(new QuestId(newGamePlusQuestId), state.GetQuestSequence(newGamePlusQuestId), MainScenarioQuestState.Available), "NG+");
        }

        ScenarioGuide guide = state.ReadScenarioGuide();
        if (guide.State == EScenarioGuideState.Missing)
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("No Scenario Tree"));

        if (guide.State == EScenarioGuideState.Loading)
            return (QuestReference.NoQuest(MainScenarioQuestState.LoadingScreen), _L("Scenario Tree Data is null"));

        QuestId currentQuest = new(guide.QuestId);
        return (new(currentQuest, state.GetQuestSequence(currentQuest.Value), MainScenarioQuestState.Available), $"sq: {currentQuest}");
    }

    public (IQuestInfo Quest, short PlayerLevel)? GetLevelLockedMainScenarioQuest()
    {
        (QuestReference source, string? _) = ReadMainScenarioQuest(out bool newGamePlus);
        if (newGamePlus || source.State != MainScenarioQuestState.Available ||
            source.CurrentQuest is not QuestId { Value: not 0 } questId ||
            !catalog.TryGetQuest(questId, out Quest? quest) || IsQuestAcceptedOrComplete(questId))
            return null;

        short playerLevel = state.CurrentLevel;
        if (playerLevel == 0 || quest.Info.Level <= playerLevel || !IsReadyToAcceptQuest(questId, ignoreLevel: true))
            return null;

        return (quest.Info, playerLevel);
    }

    public (QuestReference, string?) GetMainScenarioQuest()
    {
        (QuestReference source, string? extraData) = ReadMainScenarioQuest(out bool newGamePlus);
        if (newGamePlus || source.State != MainScenarioQuestState.Available)
            return (source, extraData);

        QuestId currentQuest = (QuestId)source.CurrentQuest!;
        if (currentQuest.Value == 0)
        {
            if (IsMainScenarioComplete())
                return (QuestReference.NoQuest(MainScenarioQuestState.Complete), _L("Main Scenario is complete"));

            List<QuestInfo> potentialQuests = catalog.MainScenarioQuests
                    .Where(x => (x.StartingCity == 0 || x.StartingCity == state.StartTown) &&
                                IsReadyToAcceptQuest(x.QuestId, ignoreLevel: true) &&
                                x.Expansion <= (EExpansionVersion)state.MaxExpansion)
                    .ToList();
            if (potentialQuests.Count == 0)
                return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("No potential quests found"));

            if (potentialQuests.Count > 1)
            {
                // The agent data can briefly not know the next quest; this picks the same one it would.
                if (potentialQuests.All(x => x.QuestId.Value is 680 or 681 or 682))
                {
                    currentQuest = new(681); // The Company You Keep; actual quest will be resolved later in GetCurrentQuest
                }
                else if (potentialQuests.Any(x => x.QuestId.Value == 1583))
                    currentQuest = new(1583); // HW: Over the Wall vs. Onwards and Upwards
                else if (potentialQuests.Any(x => x.QuestId.Value == 2451))
                    currentQuest = new(2451); // SB: A Friend of a Friend in Need vs. A Familiar Face Forgotten
                else if (potentialQuests.Any(x => x.QuestId.Value == 3282))
                    currentQuest = new(3282); // ShB: In Search of Alphinaud vs. In Search of Alisaie
                else if (potentialQuests.Any(x => x.QuestId.Value == 4359))
                    currentQuest = new(4359); // EW: Hitting the Books vs. For Thavnair Bound
                else if (potentialQuests.Any(x => x.QuestId.Value == 4865))
                    currentQuest = new(4865); // DT: To Kozama'uk vs. To Urqopacha

                if (potentialQuests.Count != 1)
                {
                    return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("Multiple potential quests found:") + " " +
                                                                                        string.Join(", ", potentialQuests.Select(x => x.QuestId.Value)));
                }
            }
            else
                currentQuest = (QuestId)potentialQuests.Single().QuestId;
        }

        // if the MSQ is hidden, we generally ignore it
        if (IsQuestAccepted(currentQuest) && state.GetQuestProgress(currentQuest.Value)!.IsHidden)
            return (QuestReference.NoQuest(MainScenarioQuestState.Available), ("Quest accepted but hidden"));

        // The quest returned here can be one just completed; 255 is its end sequence.
        if (IsQuestComplete(currentQuest))
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _LF("Quest {0} complete", currentQuest.Value));

        if (!IsReadyToAcceptQuest(currentQuest, ignoreLevel: true))
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _LF("Not ready to accept quest {0}", currentQuest.Value));

        short currentLevel = state.CurrentLevel;
        if (currentLevel != 0 && catalog.TryGetQuest(currentQuest, out Quest? levelQuest) &&
            levelQuest.Info.Level > currentLevel)
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("Low level"));

        if (!state.HasPlayer)
            return (QuestReference.NoQuest(MainScenarioQuestState.LoadingScreen), _L("In loading screen"));

        if (catalog.TryGetQuest(currentQuest, out Quest? quest)
            && quest.Info.Level > currentLevel)
        {
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("Low level"));
        }

        return (new(currentQuest, state.GetQuestSequence(currentQuest.Value), MainScenarioQuestState.Available), extraData);
    }

    private bool IsOnAlliedSocietyMount()
    {
        ushort mountId = state.CurrentMountId;
        return mountId != 0 && alliedSocietyData.Mounts.ContainsKey(mountId);
    }

    public bool KeepDaily(Quest quest, byte sequence)
    {
        if (!IsQuestAccepted(quest.Id))
            return false;
        if (!quest.Root.Interruptible)
            return true;
        if (!alliedSocietyData.Mounts.TryGetValue(state.CurrentMountId, out AlliedSocietyMount? mount))
            return false;

        return quest.AllSteps().Any(x => x.Sequence.Sequence > 0 && x.Sequence.Sequence < sequence &&
                   x.Step.DataId is { } dataId && mount.IssuerDataIds.Contains(dataId)) &&
               Controller.Steps.QuestCleanUp.UsesSocietyMount(quest);
    }

    public bool CanQueueSociety(ElementId questId) =>
        catalog.TryGetQuestInfo(questId, out IQuestInfo? info) &&
        (info.AlliedSociety == EAlliedSociety.None ||
         catalog.TryGetQuest(questId, out Quest? quest) && CanQueueSociety(quest));

    internal static bool CanQueueSociety(Quest quest) =>
        quest.Info.AlliedSociety == EAlliedSociety.None || !quest.Root.Disabled;

    public string? GetDailyBlock(QuestId questId)
    {
        if (!IsDailyAlliedSocietyQuest(questId) || IsQuestAccepted(questId) || IsPriorityQuestDone(questId) ||
            SocietyAllowances == 0 || !IsSocietyDailyAvailable(questId) ||
            IsReadyToAcceptQuest(questId, ignoreLevel: true))
            return null;

        var (_, reasons) = IsQuestLocked(questId);
        return reasons is { Length: > 0 }
            ? string.Join(", ", reasons)
            : _L("Quest is unavailable for this character.");
    }

    private (ElementId Quest, byte Sequence)? GetWaitingCraft(List<(ElementId Quest, byte Sequence)> quests)
    {
        foreach ((ElementId questId, byte sequence) in quests)
        {
            if (sequence == 255 && catalog.TryGetQuest(questId, out Quest? quest) &&
                quest.FindSequence(255)?.Steps.FirstOrDefault(x => x.InteractionType == EInteractionType.Craft) is { ItemId: { } itemId } &&
                HasCraftMaterials(itemId))
                return (questId, sequence);
        }

        return null;
    }

    // Craft materials are used up by the craft, and leftover crafted items don't count for the quest.
    public bool HasCraftMaterials(uint itemId) => state.HasCraftMaterials(itemId);

    private (ElementId Quest, byte Sequence) PreferThisZone(List<(ElementId Quest, byte Sequence)> quests)
    {
        List<((ElementId Quest, byte Sequence) Entry, uint? Zone)> zones =
            [.. quests.Select(x => (x, StageZone(x.Quest, x.Sequence)))];
        foreach (((ElementId Quest, byte Sequence) entry, uint? zone) in zones)
        {
            if (zone == state.TerritoryType)
                return entry;
        }

        return zones.GroupBy(x => x.Zone).OrderByDescending(x => x.Count()).First().First().Entry;
    }

    private uint? StageZone(ElementId questId, byte sequence)
    {
        if (!catalog.TryGetQuest(questId, out Quest? quest) || quest.FindSequence(sequence) is not { } stage)
            return null;
        return (stage.Steps.FirstOrDefault(x => x.Position != null) ?? stage.Steps.FirstOrDefault())?.TerritoryId;
    }

    private bool IsReadyToCompleteQuest(Quest quest) => state.IsReadyToTurnIn(quest.Info.Name);

    private CachedValue<List<ElementId>> _openQuests = new(ttlSeconds: 2);

    public List<ElementId> OpenQuests => _openQuests.Get(GetOpenQuests);

    private EAlliedSociety CommonTurnIn(ElementId id) =>
        alliedSocietyData.GetCommonTurnIn(catalog.TryGetQuestInfo(id, out IQuestInfo? info) ? info : null);

    private List<ElementId> GetOpenQuests()
    {
        List<(ElementId QuestId, byte Sequence)> openQuests = [];
        foreach ((ushort normalQuestId, byte sequence, bool isHidden) in state.NormalQuests)
        {
            if (isHidden)
                continue;

            QuestId questId = new(normalQuestId);

            if (CommonTurnIn(questId) != EAlliedSociety.None)
                continue;

            if (catalog.TryGetQuest(questId, out Quest? quest) &&
                quest.FindSequence(sequence) != null)
                openQuests.Add((questId, sequence));
        }

        return
        [
            .. openQuests
                .OrderByDescending(x => x.Sequence == 255)
                .Select(x => x.QuestId)
        ];
    }

    private bool IsInteractSequence(ElementId questId, byte sequenceNo, uint[] dataIds)
    {
        if (catalog.TryGetQuest(questId, out Quest? quest))
        {
            QuestSequence? sequence = quest.FindSequence(sequenceNo);
            return sequence != null &&
                   sequence.Steps.All(x =>
                       x is { InteractionType: EInteractionType.WalkTo } ||
                       (x is { InteractionType: EInteractionType.Interact, DataId: { } dataId } &&
                        dataIds.Contains(dataId)));
        }

        return false;
    }

    public QuestProgressInfo? GetQuestProgressInfo(ElementId elementId) => state.GetQuestProgressInfo(elementId);

    private CachedValue<List<PriorityQuestInfo>> _nextPriorityQuests = new(ttlSeconds: 2);
    public List<PriorityQuestInfo> NextPriorityQuests => _nextPriorityQuests.Get(GetNextPriorityQuests);
    private List<PriorityQuestInfo> GetNextPriorityQuests()
    {
        int gil = state.Gil;

        return GetPriorityQuests()
            .Where(x => IsReadyToAcceptQuest(x))
            .Select(x =>
            {
                if (!catalog.TryGetQuest(x, out Quest? quest))
                    return new(x, _L("Unknown quest"));

                QuestStep? firstStep = quest.FindSequence(0)?.FindStep(0);
                if (firstStep == null)
                    return new(x, _L("No sequence 0 with steps"));

                if (!state.IsTeleportUnlocked)
                {
                    if (!CanBeDoneWithoutTeleport(quest))
                        return new(x, _L("Teleport not unlocked"));
                }
                else
                {
                    int teleportCosts = GetTeleportCost(quest);
                    if (gil < teleportCosts)
                    {
                        return new(x,
                            _LF("Not enough gil, estimated cost: {0:N0}{1}", teleportCosts, SeIconChar.Gil.ToIconString()));
                    }
                }

                EAetheryteLocation? firstLockedAetheryte = GetFirstLockedAetheryte(quest);
                if (firstLockedAetheryte != null)
                    return new(x, _LF("Aetheryte locked: {0}", firstLockedAetheryte));

                return new PriorityQuestInfo(x);
            })
            .ToList();
    }

    private (ElementId Msq, ElementId Quest)? _classQuestFirst;

    private ElementId? GetClassQuestBeforeMsq(ElementId msq)
    {
        if (_classQuestFirst is { } kept && kept.Msq == msq &&
            (IsQuestAccepted(kept.Quest) || NextPriorityQuests.Any(x => x.QuestId == kept.Quest && x.IsAvailable)))
            return kept.Quest;
        _classQuestFirst = null;

        List<ElementId> classQuests = GetPriorityQuests(onlyClassAndRoleQuests: true);
        QuestStep? msqStart = catalog.TryGetQuest(msq, out Quest? msqData) ? msqData.FindSequence(0)?.FindStep(0) : null;
        ElementId? pick = classQuests.FirstOrDefault(IsQuestAccepted) ?? NextPriorityQuests
            .Where(x => x.IsAvailable && classQuests.Contains(x.QuestId) && IsFreeClassQuestDetour(x.QuestId, msqStart))
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (pick != null)
        {
            _classQuestFirst = (msq, pick);
            Svc.Log.Information($"Doing class quest {pick} before story quest {msq}");
        }

        return pick;
    }

    public byte GetQuestSequence(ElementId questId) => state.GetQuestSequence(questId.Value);

    public bool IsFreeClassQuestDetour(ElementId questId, QuestStep? teleportStep) =>
        catalog.TryGetQuest(questId, out Quest? quest) && quest.FindSequence(0)?.FindStep(0) is { } start &&
        QuestSelectionController.IsFreeClassQuestDetour(start, state.TerritoryType, teleportStep);

    private IEnumerable<(uint From, uint To)> ZoneChanges(Quest quest)
    {
        uint current = state.TerritoryType;
        foreach ((QuestSequence _, int _, QuestStep step) in quest.AllSteps())
        {
            // territory 1 marks steps that never travel
            if (step.TerritoryId is 0 or 1)
                continue;
            if (step.TerritoryId != current)
                yield return (current, step.TerritoryId);
            current = step.TargetTerritoryId ?? step.TerritoryId;
        }
    }

    private bool IsWalk((uint From, uint To) change) => state.RegionOf(change.From) == state.RegionOf(change.To);

    // A new character's level 1 class quest starts in another part of the city than the opening quests.
    internal bool CanBeDoneWithoutTeleport(Quest quest) =>
        quest.FindSequence(0)?.FindStep(0)?.TerritoryId == state.TerritoryType && ZoneChanges(quest).All(IsWalk);

    internal EAetheryteLocation? GetFirstLockedAetheryte(Quest quest)
    {
        HashSet<EAetheryteLocation> attunedByQuest =
        [
            .. quest.AllSteps()
                .Select(x => x.Step)
                .Where(x => x is { InteractionType: EInteractionType.AttuneAetheryte, Aetheryte: not null })
                .Select(x => x.Aetheryte!.Value)
        ];

        foreach ((uint From, uint To) change in ZoneChanges(quest).Where(x => !IsWalk(x)))
        {
            IReadOnlyList<EAetheryteLocation> aetherytes = state.AetherytesIn(change.To);
            if (aetherytes.Count > 0 && aetherytes.All(x => !state.IsAetheryteUnlocked(x) && !attunedByQuest.Contains(x)))
                return aetherytes[0];
        }

        return null;
    }

    private string _lastCostLog = string.Empty;

    // Overestimates: MappingTheRealm may walk some of these instead of teleporting.
    internal int GetTeleportCost(Quest quest)
    {
        if (state.TeleportCosts is not { } teleportCosts)
            return 0;

        List<(EAetheryteLocation Aetheryte, uint Cost)> teleports =
        [
            .. ZoneChanges(quest)
                .Where(x => !QuestSelectionController.IsSameCity(x.From, x.To))
                .Select(x => state.AetherytesIn(x.To)
                    .Where(a => teleportCosts.ContainsKey((uint)a))
                    .Select(a => (a, teleportCosts[(uint)a]))
                    .OrderBy(a => a.Item2)
                    .FirstOrDefault())
                .Where(x => x.Item2 > 0)
        ];
        if (teleports.Count == 0)
            return 0;

        var msg = $"TeleportCosts: {string.Join(", ", teleports.Select(x => $"{x.Aetheryte}={x.Cost}"))}";
        if (msg != _lastCostLog)
            Svc.Log.Debug(msg);
        _lastCostLog = msg;
        return teleports.Sum(x => (int)x.Cost);
    }

    public List<ElementId> GetPriorityQuests(bool onlyClassAndRoleQuests = false)
    {
        List<ElementId> priorityQuests = [];
        if (!onlyClassAndRoleQuests)
        {
            if (!configuration.Advanced.SkipARealmRebornHardModePrimals)
            {
                priorityQuests.AddRange(QuestData.HardModePrimals.Skip(1));
            }

            if (!configuration.Advanced.SkipCrystalTowerRaids)
                priorityQuests.AddRange(QuestData.CrystalTowerQuests);

            if (!configuration.Advanced.SkipFeistyLittleChocobo)
                priorityQuests.Add(new QuestId(1162));
        }

        if (!configuration.Advanced.SkipClassJobQuests)
        {
            Job classJob = (Job?)state.CurrentClassJobId ?? Job.ADV;
            uint[] shadowbringersChapters = QuestData.AllRoleQuestChapters.Select(x => x[0]).ToArray();
            if (classJob != Job.ADV)
            {
                priorityQuests.AddRange(catalog.GetKnownClassJobQuests(classJob)
                    .Where(x =>
                    {
                        if (!catalog.TryGetQuest(x.QuestId, out Quest? quest) ||
                            quest.Info is not QuestInfo questInfo)
                        {
                            return false;
                        }

                        if (shadowbringersChapters.Contains(questInfo.NewGamePlusChapter))
                            return !QuestData.FinalShadowbringersRoleQuests.Any(IsQuestComplete);

                        if (QuestData.AllRoleQuestChapters.Any(y => y.Contains(questInfo.NewGamePlusChapter)))
                            return false;

                        // Later job quests can lock skills behind them (e.g. Reaper, Sage).

                        return true;
                    })
                    .Select(x => x.QuestId));
            }
        }

        return priorityQuests
            .Where(catalog.IsKnownQuest)
            .ToList();
    }

    public bool IsReadyToAcceptQuest(ElementId questId, bool ignoreLevel = false)
    {
        catalog.TryGetQuest(questId, out Quest? quest);
        IQuestInfo? info = quest?.Info;
        if (info == null)
            catalog.TryGetQuestInfo(questId, out info);
        if (info is { IsRepeatable: true })
        {
            if (IsQuestAccepted(questId))
                return false;

            if (questId is QuestId qId && IsDailyAlliedSocietyQuest(qId))
            {
                if (state.IsDailyQuestCompleted(questId.Value) || state.SocietyAllowances == 0)
                    return false;

                if (!IsSocietyDailyAvailable(qId))
                    return false;
            }
            else
            {
                if (IsQuestComplete(questId))
                    return false;
            }
        }
        else
        {
            if (IsQuestAcceptedOrComplete(questId))
                return false;
        }

        if (!ignoreLevel)
        {
            short currentLevel = state.CurrentLevel;
            if (currentLevel != 0 && quest != null && quest.Info.Level > currentLevel)
                return false;
        }

        if (IsQuestUnobtainable(questId))
            return false;

        if (IsQuestLocked(questId) is (bool isLocked, var _))
            return !isLocked;

        return true;
    }

    public bool IsQuestAcceptedOrComplete(ElementId elementId) => IsQuestComplete(elementId) || IsQuestAccepted(elementId);

    public bool IsQuestAccepted(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestAccepted(questId);
        if (elementId is AlliedSocietyDailyId)
            return false;
        if (elementId is UnlockLinkId or AetherCurrentId)
            return false;

        throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    public bool IsQuestAccepted(QuestId questId) => state.IsQuestAccepted(questId.Value);

    public bool IsQuestComplete(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestComplete(questId);
        if (elementId is AlliedSocietyDailyId)
            return false;
        if (elementId is UnlockLinkId unlockLinkId)
            return IsUnlockLinkUnlocked(unlockLinkId);
        if (elementId is AetherCurrentId zoneId)
            return GetMissingAetherCurrents(zoneId).Count == 0;

        throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    public IReadOnlyList<uint> GetMissingAetherCurrents(AetherCurrentId zoneId) =>
        [.. ((AetherCurrentZoneInfo)catalog.GetQuestInfo(zoneId)).CurrentIds.Where(x => !state.IsAetherCurrentUnlocked(x))];

    public bool IsQuestComplete(QuestId questId) => state.IsQuestComplete(questId.Value);

    public bool IsUnlockLinkUnlocked(UnlockLinkId unlockLinkId) => state.IsUnlockLinkUnlocked(unlockLinkId.Value);

    public bool IsPriorityQuestDone(ElementId elementId)
    {
        if (elementId is QuestId questId && IsDailyAlliedSocietyQuest(questId))
        {
            if (IsQuestAccepted(questId))
                return false;
            return state.IsDailyQuestCompleted(questId.Value);
        }

        return IsQuestComplete(elementId);
    }

    public (bool isLocked, string[]? reasons) IsQuestLocked(ElementId elementId, ElementId? extraCompletedQuest = null)
    {
        if (elementId is QuestId questId)
            return IsQuestLocked(questId, extraCompletedQuest);
        if (elementId is AlliedSocietyDailyId alliedSocietyDailyId)
            return (IsQuestLocked(alliedSocietyDailyId), null);
        if (elementId is UnlockLinkId unlockLinkId)
            return (IsQuestLocked(unlockLinkId), null);
        if (elementId is AetherCurrentId zoneId)
            return (!ArePreviousQuestsMet(catalog.GetQuestInfo(zoneId), extraCompletedQuest), null);

        throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    private (bool isLocked, string[]? reasons) IsQuestLocked(QuestId questId, ElementId? extraCompletedQuest = null)
    {
        List<string> lockedReason = [];

        QuestInfo questInfo = (QuestInfo)catalog.GetQuestInfo(questId);
        if (questInfo.GrandCompany != GrandCompany.None)
        {
            if (questInfo.GrandCompany != GetGrandCompany())
                lockedReason.Add(_L("Grand Company"));
            if (questInfo.GrandCompanyRank > GetGrandCompanyRank())
                lockedReason.Add(_L("Grand Company rank"));
        }

        bool isClassQuest = questInfo.NewGamePlusChapter != 0 &&
            QuestData.JobToClassQuestChapterIds.Values
                .Any(x => x.Contains(questInfo.NewGamePlusChapter));
        if (isClassQuest && questInfo.ClassJobs.Count >= 1 && questInfo.ClassJobs.Count <= 2)
        {
            var levels = state.ClassJobLevels;
            var index = questInfo.ClassJobs[0].GetData().ExpArrayIndex;
            if (index >= 0 && levels.Count > index && levels[index] < questInfo.Level)
                lockedReason.Add($"{_L("Low level")} ({questInfo.ClassJobs[0]})");
        }
        if (isClassQuest && questInfo.ClassJobs.Count > 5)
        {
            var levels = state.ClassJobLevels;
            var highest = levels.ToList().IndexOf(levels.Max());
            var highestJob = questInfo.ClassJobs.Where(x => x.GetData().ExpArrayIndex == highest).FirstOrNull();
            if (highestJob != null && levels[highest] < questInfo.Level)
                lockedReason.Add($"{_L("Low level")} ({ClassJobUtils.ClassToJobStone(highestJob.Value).Item1})");
        }

        if (questInfo.AlliedSociety != EAlliedSociety.None)
        {
            if (questInfo.IsRepeatable)
            {
                if (!IsSocietyDailyAvailable(questId))
                    lockedReason.Add(_L("Daily unavailable"));
            }
            else if (!IsSocietyStoryAvailable(questId))
            {
                lockedReason.Add(_L("Society reputation"));
            }
        }

        if (QuestData.DeliveryMoogleQuests.Contains(questInfo.QuestId))
        {
            byte currentDeliveryLevel = state.DeliveryLevel;
            if (extraCompletedQuest != null &&
                catalog.TryGetQuestInfo(extraCompletedQuest, out IQuestInfo? extraQuestInfo) &&
                extraQuestInfo is QuestInfo { IsMoogleDeliveryQuest: true })
                currentDeliveryLevel++;

            if (questInfo.MoogleDeliveryLevel > currentDeliveryLevel)
                lockedReason.Add(_L("Carrier level"));
        }

        // "an ill-conceived venture" requires to have retainers unlocked
        if ((new ushort[] { 1432, 1433, 1434 }).Contains(questId.Value))
        {
            if (!state.HasRetainers)
                lockedReason.Add(_L("Retainers"));
        }

        if (!ArePreviousQuestsMet(questInfo, extraCompletedQuest))
            lockedReason.Add(PreviousQuestReason.Format(questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne,
                questInfo.PreviousQuests
                    .Where(x => !IsPreviousQuestMet(x) && !x.QuestId.Equals(extraCompletedQuest))
                    .Select(x => catalog.TryGetQuestInfo(x.QuestId, out IQuestInfo? info) ? info.Name : x.QuestId.ToString())
                    .ToList()));
        if (!ArePreviousInstancesMet(questInfo))
            lockedReason.Add(_L("Previous duty"));

        if (catalog.TryGetQuest(questId, out Quest? quest) &&
            GetFirstLockedAetheryte(quest) is EAetheryteLocation firstLockedAetheryte)
            lockedReason.Add(_LF("Aetheryte locked: {0}", firstLockedAetheryte));

        if (QuestData.SpecialPrerequisites.TryGetValue(questId.Value, out QuestPrerequisite? prerequisite) &&
            prerequisite.GetUnmetReason(state) is { } unmetReason)
            lockedReason.Add(unmetReason);

        if (questInfo.IsSeasonalEvent && !questInfo.IsRunning(state.ActiveFestivals))
            lockedReason.Add(_L("Limited time event"));

        return (lockedReason.Count > 0, lockedReason.ToArray());
    }

    private bool IsQuestLocked(AlliedSocietyDailyId alliedSocietyDailyId)
    {
        byte currentRank = state.GetAlliedSocietyRank(alliedSocietyDailyId.AlliedSociety);
        return currentRank == 0 || currentRank < alliedSocietyDailyId.Rank;
    }

    private bool IsQuestLocked(UnlockLinkId unlockLinkId) => IsQuestUnobtainable(unlockLinkId);

    public bool IsDailyAlliedSocietyQuest(QuestId questId)
    {
        QuestInfo questInfo = (QuestInfo)catalog.GetQuestInfo(questId);
        return questInfo.AlliedSociety != EAlliedSociety.None && questInfo.IsRepeatable;
    }

    public uint SocietyAllowances => state.SocietyAllowances;

    public IReadOnlyList<(ushort Id, ushort Phase)> ActiveFestivals => state.ActiveFestivals;

    public bool IsOutOfSocietyAllowances(ElementId questId) =>
        questId is QuestId qId && IsDailyAlliedSocietyQuest(qId) && state.SocietyAllowances == 0;

    public bool IsSocietyDailyAvailable(QuestId questId)
    {
        if (!IsDailyAlliedSocietyQuest(questId))
            return false;

        QuestInfo questInfo = (QuestInfo)catalog.GetQuestInfo(questId);
        return state.GetAvailableSocietyQuests(questInfo.AlliedSociety).Contains(questId);
    }

    public bool IsSocietyStoryAvailable(QuestId questId)
    {
        QuestInfo questInfo = (QuestInfo)catalog.GetQuestInfo(questId);
        (EAlliedSocietyRank currentRank, ushort currentRep, ushort neededRep) = GetRankAndReputation(questInfo.AlliedSociety);
        return currentRank > questInfo.AlliedSocietyRank || currentRep >= neededRep;
    }

    public (EAlliedSocietyRank, ushort, ushort) GetRankAndReputation(EAlliedSociety alliedSociety) =>
        ((EAlliedSocietyRank)state.GetAlliedSocietyRank((byte)alliedSociety),
        state.GetReputation((byte)alliedSociety),
        state.GetNeededReputation((byte)alliedSociety));

    public bool IsQuestUnobtainable(ElementId elementId, ElementId? extraCompletedQuest = null)
    {
        if (elementId is QuestId questId)
            return IsQuestUnobtainable(questId, extraCompletedQuest);
        if (elementId is UnlockLinkId unlockLinkId)
            return IsQuestUnobtainable(unlockLinkId);

        return false;
    }

    public bool IsQuestUnobtainable(QuestId questId, ElementId? extraCompletedQuest = null)
    {
        QuestInfo questInfo = (QuestInfo)catalog.GetQuestInfo(questId);
        if (questInfo.Expansion > (EExpansionVersion)state.MaxExpansion)
            return true;

        // unobtainable on free trial
        if (state.MaxLevel == 80 &&
            (EExpansionVersion)state.MaxExpansion == EExpansionVersion.Shadowbringers &&
            (new ushort[]
            {
                1432, 1433, 1434
            }).Contains(questId.Value))
            return true;

        if (questInfo.QuestLocks.Count > 0)
        {
            int completedQuests = questInfo.QuestLocks.Count(x => IsQuestComplete(x) || x.Equals(extraCompletedQuest));
            if (questInfo.QuestLockJoin == EQuestJoin.All && questInfo.QuestLocks.Count == completedQuests)
                return true;

            if (questInfo.QuestLockJoin == EQuestJoin.AtLeastOne && completedQuests > 0)
                return true;
        }

        if (catalog.GetLockedClassQuests().Contains(questId))
            return true;

        byte startingCity = state.StartTown;
        if (questInfo.StartingCity > 0 && questInfo.StartingCity != startingCity)
            return true;

        if (questId.Value == 674 && startingCity == 3)
            return true;
        if (questId.Value == 673 && startingCity != 3)
            return true;

        Dictionary<ushort, Job> closeToHomeQuests = new()
        {
            { 108, Job.MRD },
            { 109, Job.ACN },
            { 85, Job.LNC },
            { 123, Job.ARC },
            { 124, Job.CNJ },
            { 568, Job.GLA },
            { 569, Job.PGL },
            { 570, Job.THM }
        };

        // The starter-city NPC offers every Close to Home quest; once one started, the others are unavailable.
        if (closeToHomeQuests.TryGetValue(questId.Value, out Job neededStartingClass) &&
            closeToHomeQuests.Any(x => IsQuestAcceptedOrComplete(new QuestId(x.Key))))
        {
            Job actualStartingClass = (Job)state.FirstClass;
            if (actualStartingClass != neededStartingClass)
                return true;
        }

        if (IsQuestRemoved(questId))
            return true;

        return false;
    }

    private bool IsQuestUnobtainable(UnlockLinkId unlockLinkId)
    {
        if (unlockLinkId.Value == 506)
            return !state.IsFestivalActive(160, 2);
        if (unlockLinkId.Value == 568)
            return !state.IsFestivalActive(160, 3);

        return true;
    }

    public static bool IsQuestRemoved(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestRemoved(questId);

        return false;
    }

    private static bool IsQuestRemoved(QuestId questId) => questId.Value is 487 or 1428 or 1429;

    private bool ArePreviousQuestsMet(IQuestInfo questInfo, ElementId? extraCompletedQuest)
    {
        if (questInfo.PreviousQuests.Count == 0)
            return true;

        int count = questInfo.PreviousQuests.Count(x =>
            IsPreviousQuestMet(x) || x.QuestId.Equals(extraCompletedQuest));
        if (questInfo.PreviousQuestJoin == EQuestJoin.All &&
            questInfo.PreviousQuests.Count == count)
            return true;
        if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne && count > 0)
            return true;

        return false;
    }

    internal bool IsPreviousQuestMet(PreviousQuestInfo previousQuestInfo)
    {
        if (IsQuestComplete(previousQuestInfo.QuestId))
            return true;

        if (previousQuestInfo.Sequence != 0 && IsQuestAccepted(previousQuestInfo.QuestId))
        {
            QuestProgressInfo? progress = GetQuestProgressInfo(previousQuestInfo.QuestId);
            return progress != null && progress.Sequence >= previousQuestInfo.Sequence;
        }

        return false;
    }

    private bool ArePreviousInstancesMet(QuestInfo questInfo)
    {
        if (questInfo.PreviousInstanceContent.Count == 0)
        {
            return true;
        }

        int completedInstances = questInfo.PreviousInstanceContent.Count(state.IsInstanceContentCompleted);

        if (questInfo.PreviousInstanceContentJoin == EQuestJoin.All &&
            questInfo.PreviousInstanceContent.Count == completedInstances)
            return true;
        if (questInfo.PreviousInstanceContentJoin == EQuestJoin.AtLeastOne && completedInstances > 0)
            return true;

        return false;
    }


    public bool HasClassJobAtQuestLevel(ElementId questId)
    {
        if (!catalog.TryGetQuestInfo(questId, out IQuestInfo? questInfo))
            return false;

        if (questInfo.ClassJobs.Count == 0)
            return state.CurrentLevel >= questInfo.Level;

        return questInfo.ClassJobs.Any(job => GetClassJobLevel(job) >= questInfo.Level);
    }

    public short GetClassJobLevel(Job classJob) => state.GetClassJobLevel(classJob);

    public GrandCompany GetGrandCompany() => (GrandCompany)state.GrandCompany;
    public EGrandCompanyRank GetGrandCompanyRank() => (EGrandCompanyRank)state.GrandCompanyRank;

    public bool IsMainScenarioComplete() => IsQuestComplete(catalog.LastMainScenarioQuestId);
}
