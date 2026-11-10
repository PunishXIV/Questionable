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
                chatGui.PrintError(_L("This quest uses the White Wolf Gate, which requires that you unlock all aethernet shards in Gridania.\n") +
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

        // always prioritize accepting MSQ quests, to make sure we don't turn in one MSQ quest and then go off to do
        // side quests until the end of time.
        (QuestReference msqQuest, string? _) = GetMainScenarioQuest();
        if (msqQuest.CurrentQuest != null && !catalog.IsKnownQuest(msqQuest.CurrentQuest))
            msqQuest = QuestReference.NoQuest(msqQuest.State);

        if (msqQuest.CurrentQuest != null && !IsQuestAccepted(msqQuest.CurrentQuest))
        {
            if (allowNewMsq)
                return msqQuest;

            msqQuest = QuestReference.NoQuest(msqQuest.State);
        }

        // Use the quests in the same order as they're shown in the to-do list, e.g. if the MSQ is the first item,
        // do the MSQ; if a side quest is the first item do that side quest.
        //
        // If no quests are marked as 'priority', accepting a new quest adds it to the top of the list.
        List<(ElementId Quest, byte Sequence)> trackedQuests = [];
        foreach (ushort trackedQuestId in state.TrackedQuestIds)
        {
            ElementId currentQuest = new QuestId(trackedQuestId);
            if (catalog.IsKnownQuest(currentQuest))
                trackedQuests.Add((currentQuest, state.GetQuestSequence(trackedQuestId)));
        }

        if (configuration.General.SkipLowPriorityDuties && trackedQuests.Count > 0)
        {
            IReadOnlyList<(uint CfcId, ElementId QuestId, int Sequence)> lowPriorityQuests = catalog.LowPriorityCfcQuests;
            trackedQuests.RemoveAll(x => lowPriorityQuests.Any(y => x.Quest == y.QuestId && x.Sequence == y.Sequence));
        }

        if (trackedQuests.Count > 0)
        {
            // if we have multiple quests to turn in for an allied society, try and complete all of them
            (ElementId firstTrackedQuest, byte firstTrackedSequence) = trackedQuests[0];
            EAlliedSociety firstTrackedAlliedSociety =
                alliedSocietyData.GetCommonTurnIn(firstTrackedQuest);
            if (firstTrackedAlliedSociety != EAlliedSociety.None)
            {
                List<(ElementId Quest, byte Sequence)> sameSocietyQuests = trackedQuests.Skip(1)
                    .Where(quest =>
                        alliedSocietyData.GetCommonTurnIn(quest.Quest) == firstTrackedAlliedSociety)
                    .ToList();
                if (sameSocietyQuests.Count > 0)
                {
                    if (firstTrackedSequence == 255)
                    {
                        foreach ((ElementId questId, byte sequence) in sameSocietyQuests)
                        {
                            // only if the other quest isn't ready to be turned in
                            if (sequence != 255 || (catalog.TryGetQuest(questId, out Quest? quest) && !IsReadyToCompleteQuest(quest)))
                                return new(questId, sequence, msqQuest.State);
                        }
                    }
                    else if (!IsOnAlliedSocietyMount())
                    {
                        // a few of the vanu quests require you to talk to one of the npcs near the issuer, so we
                        // give priority to those

                        // also include the first quest in the list for those
                        sameSocietyQuests.Insert(0, (firstTrackedQuest, firstTrackedSequence));

                        alliedSocietyData.GetCommonNpcs(firstTrackedAlliedSociety,
                            out uint[]? normalNpcs,
                            out uint[] _);

                        if (normalNpcs.Length > 0)
                        {
                            (ElementId, byte)? talkToNormalNpcs = sameSocietyQuests
                                .Where(x => x.Sequence < 255 && IsInteractSequence(x.Quest, x.Sequence, normalNpcs))
                                .Cast<(ElementId, byte)?>()
                                .FirstOrDefault();
                            if (talkToNormalNpcs != null)
                                return new(talkToNormalNpcs.Value.Item1, talkToNormalNpcs.Value.Item2, msqQuest.State);
                        }

                    }
                }
            }

            return new(firstTrackedQuest, firstTrackedSequence, msqQuest.State);
        }

        ElementId? priorityQuest = skipPriority ? null : NextPriorityQuests
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (priorityQuest != null)
        {
            // if we have an accepted msq quest, and know of no quest of those currently in the to-do list...
            // (1) try and find a priority quest to do
            return new(priorityQuest, state.GetQuestSequence(priorityQuest.Value), msqQuest.State);
        }

        if (msqQuest.CurrentQuest != null)
        {
            // (2) just do a normal msq quest
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

            // fallback lookup; find a quest which isn't completed but where all prequisites are met
            // excluding branching quests

            List<QuestInfo> potentialQuests = catalog.MainScenarioQuests
                    .Where(x => (x.StartingCity == 0 || x.StartingCity == state.StartTown) &&
                                IsReadyToAcceptQuest(x.QuestId, ignoreLevel: true) &&
                                x.Expansion <= (EExpansionVersion)state.MaxExpansion)
                    .ToList();
            if (potentialQuests.Count == 0)
                return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("No potential quests found"));

            if (potentialQuests.Count > 1)
            {
                // for all of these (except the GC quests), questionable normally auto-picks the next quest based on the
                // agent data. This should (hopefully) pick the exact same quest when the agent does not know for a bit.
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

        // it can sometimes happen (although this isn't reliably reproducible) that the quest returned here
        // is one you've just completed. We return 255 as sequence here, since that is the end of said quest;
        // but this is just really hoping that this breaks nothing.
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

    private bool IsReadyToCompleteQuest(Quest quest) => state.IsReadyToTurnIn(quest.Info.Name);

    private CachedValue<List<ElementId>> _openQuests = new(ttlSeconds: 2);

    public List<ElementId> OpenQuests => _openQuests.Get(GetOpenQuests);

    private List<ElementId> GetOpenQuests()
    {
        List<(ElementId QuestId, byte Sequence)> openQuests = [];
        foreach ((ushort normalQuestId, byte sequence, bool isHidden) in state.NormalQuests)
        {
            if (isHidden)
                continue;

            QuestId questId = new(normalQuestId);

            if (alliedSocietyData.GetCommonTurnIn(questId) != EAlliedSociety.None)
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
        // ideally, we'd also be able to afford *some* teleports
        // this implicitly makes sure we're not starting one of the lv1 class quests if we can't afford to teleport back
        //
        // Of course, they can still be accepted manually.
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

                // all priority quests assume we're able to teleport to the beginning (and for e.g. class quests, the end)
                // ideally without having to wait 15m for Return. Teleport can unlock as late as level 10 (Gridania), so
                // until then we still allow quests that never need it, which covers most level 1-5 class quests.
                if (!state.IsTeleportUnlocked)
                {
                    if (!CanBeDoneWithoutTeleport(quest))
                        return new(x, _L("Teleport not unlocked"));
                }
                else
                {
                    if (!firstStep.IsTeleportableForPriorityQuests())
                        return new(x, _L("Can't teleport to start"));

                    int teleportCosts = GetTeleportCost(quest);
                    if (gil < teleportCosts)
                    {
                        return new(x,
                            _LF("Not enough gil, estimated cost: {0:N0}{1}", teleportCosts, SeIconChar.Gil.ToIconString()));
                    }
                }

                EAetheryteLocation? firstLockedAetheryte = GetFirstLockedAetheryte(quest);
                if (firstLockedAetheryte != null)
                {
                    // if quest requires white wolf gate, and unlock quest is available, don't report locked
                    if (firstLockedAetheryte == EAetheryteLocation.GridaniaWhiteWolfGate && IsReadyToAcceptQuest(new QuestId(802)))
                        return new(x);
                    return new(x, _LF("Aetheryte locked: {0}", firstLockedAetheryte));
                }

                return new PriorityQuestInfo(x);
            })
            .ToList();
    }

    private bool CanBeDoneWithoutTeleport(Quest quest)
    {
        uint? currentTerritory = state.TerritoryType;
        bool isFirstStep = true;
        foreach ((QuestSequence _, int _, QuestStep step) in quest.AllSteps())
        {
            if (isFirstStep && step.TerritoryId != currentTerritory)
                return false;
            if (step.AetheryteShortcut != null && step.TerritoryId != currentTerritory)
                return false;

            currentTerritory = step.TargetTerritoryId ?? step.TerritoryId;
            isFirstStep = false;
        }

        return true;
    }

    internal EAetheryteLocation? GetFirstLockedAetheryte(Quest quest)
    {
        // aetherytes attuned by an earlier step of the same quest don't count as locked
        HashSet<EAetheryteLocation> attunedByQuest = [];
        foreach ((QuestSequence _, int _, QuestStep step) in quest.AllSteps())
        {
            if (step is { InteractionType: EInteractionType.AttuneAetheryte, Aetheryte: { } aetheryte })
                attunedByQuest.Add(aetheryte);
            if (step is { InteractionType: EInteractionType.AttuneAethernetShard, AethernetShard: { } aethernetShard })
                attunedByQuest.Add(aethernetShard);

            if (step.AetheryteShortcut is { } aetheryteShortcut && IsLocked(aetheryteShortcut) &&
                step.SkipConditions?.AetheryteShortcutIf?.AetheryteLocked != aetheryteShortcut)
                return aetheryteShortcut;

            if (step.AethernetShortcut is { } aethernetShortcut)
            {
                if (IsLocked(aethernetShortcut.From))
                    return aethernetShortcut.From;
                if (IsLocked(aethernetShortcut.To))
                    return aethernetShortcut.To;
            }
        }

        return null;

        bool IsLocked(EAetheryteLocation location) =>
            !state.IsAetheryteUnlocked(location) && !attunedByQuest.Contains(location);
    }

    private string _lastCostLog = string.Empty;
    private int GetTeleportCost(Quest quest)
    {
        List<EAetheryteLocation> teleportTargets = quest.AllSteps()
            .Where(x => x.Step.AetheryteShortcut != null)
            .Select(x => x.Step.AetheryteShortcut!.Value)
            .ToList();
        if (teleportTargets.Count == 0)
            return 0;

        if (state.TeleportCosts is not { } teleportCosts)
            return 0;

        var msg = $"TeleportCosts: {string.Join('+', teleportTargets)} " +
            $"({string.Join(", ", teleportCosts.Where(x => teleportTargets.Contains((EAetheryteLocation)x.Key)).Select(x => $"{(EAetheryteLocation)x.Key}={x.Value}"))})";
        if (msg != _lastCostLog)
            Svc.Log.Debug(msg);
        _lastCostLog = msg;
        return teleportTargets.Sum(x => (int)teleportCosts.GetValueOrDefault((uint)x, 200u));
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

                        // if no shadowbringers role quest is complete, (at least one) is required
                        if (shadowbringersChapters.Contains(questInfo.NewGamePlusChapter))
                            return !QuestData.FinalShadowbringersRoleQuests.Any(IsQuestComplete);

                        // ignore all other role quests
                        if (QuestData.AllRoleQuestChapters.Any(y => y.Contains(questInfo.NewGamePlusChapter)))
                            return false;

                        // even job quests for the later expacs (after role quests were introduced) might have skills locked
                        // behind them, e.g. reaper and sage

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
        if (quest is { Info.IsRepeatable: true })
        {
            if (IsQuestAccepted(questId))
                return false;

            if (questId is QuestId qId && IsDailyAlliedSocietyQuest(qId))
            {
                if (state.IsDailyQuestCompleted(questId.Value))
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
        if (elementId is SatisfactionSupplyNpcId)
            return false;
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
        if (elementId is SatisfactionSupplyNpcId)
            return false;
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
        if (elementId is SatisfactionSupplyNpcId satisfactionSupplyNpcId)
            return (IsQuestLocked(satisfactionSupplyNpcId), null);
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
                lockedReason.Add(_L("GC"));
            if (questInfo.GrandCompanyRank > GetGrandCompanyRank())
                lockedReason.Add(_L("Rank"));
        }

        // quest is in a ng+ chapter, and the chapter id is a known class quest group
        bool isClassQuest = questInfo.NewGamePlusChapter != 0 &&
            QuestData.JobToClassQuestChapterIds.Values
                .Any(x => x.Contains(questInfo.NewGamePlusChapter));
        // if this is a class quest with only one or two acceptable jobs (i.e class or job)
        if (isClassQuest && questInfo.ClassJobs.Count >= 1 && questInfo.ClassJobs.Count <= 2)
        {
            var levels = state.ClassJobLevels;
            var index = questInfo.ClassJobs[0].GetData().ExpArrayIndex;
            if (index >= 0 && levels.Count > index && levels[index] < questInfo.Level)
                lockedReason.Add($"{_L("Low level")} ({questInfo.ClassJobs[0]})");
        }
        // if this is a class quest with a wider range of acceptable jobs (i.e an unlock quest)
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
                lockedReason.Add(_L("Society rep"));
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
            lockedReason.Add(_L("Prev instance"));

        if (catalog.TryGetQuest(questId, out Quest? quest) &&
            GetFirstLockedAetheryte(quest) is EAetheryteLocation firstLockedAetheryte)
        {
            if (firstLockedAetheryte.TrySpecialAethernet(out EAetheryteLocation? cityState) &&
                cityState is EAetheryteLocation loc)
            {
                if (QuestData.AethernetUnlockQuests.TryGetValue(loc, out var entry) &&
                        !entry.QuestIds.FromNumericListOfQuests()
                        .Any(q => q == questId || IsQuestAcceptedOrComplete(q)))
                    lockedReason.Add(_LF("Aethernet locked ({0}): {1}", entry.Letter, firstLockedAetheryte));
            }
            else
                lockedReason.Add(_LF("Aetheryte locked: {0}", firstLockedAetheryte));
        }

        if (QuestData.SpecialPrerequisites.TryGetValue(questId.Value, out QuestPrerequisite? prerequisite) &&
            prerequisite.GetUnmetReason(state) is { } unmetReason)
            lockedReason.Add(unmetReason);

        if (questInfo.IsSeasonalEvent &&
                !EventInfoComponent.EventQuests.Any(eq =>
                    eq.QuestIds.Contains(questId) &&
                    (eq.StartsAtUtc == null || eq.StartsAtUtc < DateTime.UtcNow) &&
                    eq.EndsAtUtc >= DateTime.UtcNow))
            lockedReason.Add(_L("Limited time event"));

        return (lockedReason.Count > 0, lockedReason.ToArray());
    }


    private bool IsQuestLocked(SatisfactionSupplyNpcId satisfactionSupplyNpcId)
    {
        SatisfactionSupplyInfo questInfo = (SatisfactionSupplyInfo)catalog.GetQuestInfo(satisfactionSupplyNpcId);
        return !ArePreviousQuestsMet(questInfo, extraCompletedQuest: null);
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

        // The starter-city NPC offers every Close to Home quest but selects the current class's quest.
        // Once one has started, treat the other starting-class quests as unavailable.
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

    private bool IsPreviousQuestMet(PreviousQuestInfo previousQuestInfo)
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

    public bool IsClassJobUnlocked(Job classJob)
    {
        (ushort questId, int expArrayIndex) = catalog.GetClassJobUnlock(classJob);
        if (questId != 0)
            return IsQuestComplete(new QuestId(questId));

        IReadOnlyList<short> levels = state.ClassJobLevels;
        return expArrayIndex >= 0 && expArrayIndex < levels.Count && levels[expArrayIndex] > 0;
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
