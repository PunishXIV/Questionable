using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.ExcelServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Utils;
using static Questionable.Utils.CacheUtils;
using static Questionable.Utils.LocalizeShortcut;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using Quest = Questionable.Model.Quest;

namespace Questionable.Functions;

internal sealed unsafe class QuestFunctions
(
    QuestRegistry questRegistry,
    QuestData questData,
    AetheryteFunctions aetheryteFunctions,
    AlliedSocietyQuestFunctions alliedSocietyQuestFunctions,
    AlliedSocietyData alliedSocietyData,
    Configuration configuration,
    IDataManager dataManager,
    IClientState clientState,
    IObjectTable objectTable,
    IGameGuiAdapter gameGui,
    IChatGui chatGui)
{
    internal static readonly int[] questsThatUseWhiteWolfGate = [439, 1080, 3870, 33];
    internal Dictionary<ushort, HashSet<IQuestInfo>> prereqCache = [];

    public QuestReference GetCurrentQuest(bool allowNewMsq = true)
    {
        QuestReference internalQuest = GetCurrentQuestInternal(allowNewMsq);
        (ElementId? currentQuest, byte sequence, MainScenarioQuestState questState) = internalQuest;
        PlayerState* playerState = PlayerState.Instance();

        if (currentQuest == null || currentQuest.Value == 0)
        {
            if (clientState.TerritoryType == 181) // Starting in Limsa
            {
                return new(new QuestId(107), 0, MainScenarioQuestState.Available);
            }

            if (clientState.TerritoryType == 182) // Starting in Ul'dah
            {
                return new(new QuestId(594), 0, MainScenarioQuestState.Available);
            }

            if (clientState.TerritoryType == 183) // Starting in Gridania
            {
                return new(new QuestId(39), 0, MainScenarioQuestState.Available);
            }

            return QuestReference.NoQuest(questState);
        }
        else if (currentQuest.Value == 681)
        {
            // if we have already picked up the GC quest, just return the progress for it
            if (IsQuestAccepted(currentQuest) || IsQuestComplete(currentQuest))
                return new(currentQuest, sequence, questState);

            // The company you keep...
            return configuration.General.GrandCompany switch
            {
                GrandCompany.TwinAdder => new(new QuestId(680), 0, questState),
                GrandCompany.Maelstrom => new(new QuestId(681), 0, questState),
                GrandCompany.ImmortalFlames => new(new QuestId(682), 0, questState),
                var _ => QuestReference.NoQuest(MainScenarioQuestState.Unavailable)
            };
        }
        else if (currentQuest.Value == 3856 && !playerState->IsMountUnlocked(1)) // we come in peace
        {
            ushort chocoboQuest = (GrandCompany)playerState->GrandCompany switch
            {
                GrandCompany.TwinAdder => 700,
                GrandCompany.Maelstrom => 701,
                GrandCompany.ImmortalFlames => 702,
                var _ => 0
            };

            if (chocoboQuest != 0 && !QuestManager.IsQuestComplete(chocoboQuest))
                return new(new QuestId(chocoboQuest), QuestManager.GetQuestSequence(chocoboQuest), questState);
        }
        else if (questsThatUseWhiteWolfGate.Contains(currentQuest.Value) &&
                 !aetheryteFunctions.IsAetheryteUnlocked(EAetheryteLocation.GridaniaWhiteWolfGate))
        {
            // quests that use white wolf gate, finish 'broadening horizons' to unlock it
            if (!aetheryteFunctions.IsAetheryteUnlocked(EAetheryteLocation.GridaniaBlueBadgerGate))
            {
                chatGui.PrintError(_L("This quest uses the White Wolf Gate, which requires that you unlock all aethernet shards in Gridania.\n" +
                               "This should have happened as part of the quest \"Close To Home\" if starting in Gridania, or \"The Ul'dahn/Lominsan Envoy\" for the other cities.\n" +
                               "Please unlock the aethernet shards, or complete the current quest sequence manually before continuing."),
                               CommandHandler.MessageTag, CommandHandler.TagColor);
                throw new RequiredTeleportLockedException("Required aethernet shard not unlocked");
            }

            QuestId broadeningHorizons = new(802);
            return new(broadeningHorizons, QuestManager.GetQuestSequence(broadeningHorizons.Value), questState);
        }

        return internalQuest;
    }

    public QuestReference GetCurrentQuestInternal(bool allowNewMsq)
    {
        QuestManager* questManager = QuestManager.Instance();
        if (questManager == null)
            return QuestReference.NoQuest(MainScenarioQuestState.Unavailable);

        // always prioritize accepting MSQ quests, to make sure we don't turn in one MSQ quest and then go off to do
        // side quests until the end of time.
        (QuestReference msqQuest, string? _) = GetMainScenarioQuest();
        if (msqQuest.CurrentQuest != null && !questRegistry.IsKnownQuest(msqQuest.CurrentQuest))
            msqQuest = QuestReference.NoQuest(msqQuest.State);

        if (msqQuest.CurrentQuest != null && !IsQuestAccepted(msqQuest.CurrentQuest))
        {
            if (allowNewMsq)
                return msqQuest;
            else
                msqQuest = QuestReference.NoQuest(msqQuest.State);
        }

        // Use the quests in the same order as they're shown in the to-do list, e.g. if the MSQ is the first item,
        // do the MSQ; if a side quest is the first item do that side quest.
        //
        // If no quests are marked as 'priority', accepting a new quest adds it to the top of the list.
        List<(ElementId Quest, byte Sequence)> trackedQuests = [];
        for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
        {
            ElementId currentQuest;
            TrackingWork trackedQuest = questManager->TrackedQuests[i];
            switch (trackedQuest.QuestType)
            {
                case 1: // normal quest
                    currentQuest = new QuestId(questManager->NormalQuests[trackedQuest.Index].QuestId);
                    if (questRegistry.IsKnownQuest(currentQuest))
                        trackedQuests.Add((currentQuest, QuestManager.GetQuestSequence(currentQuest.Value)));
                    break;

                case 2: // leve
                    break;
            }
        }

        if (configuration.General.SkipLowPriorityDuties && trackedQuests.Count > 0)
        {
            IReadOnlyList<(uint ContentFinderConditionId, ElementId QuestId, int Sequence)> lowPriorityQuests = questRegistry.LowPriorityContentFinderConditionQuests;
            trackedQuests.RemoveAll(x => lowPriorityQuests.Any(y => x.Quest == y.QuestId && x.Sequence == y.Sequence));
        }

        if (trackedQuests.Count > 0)
        {
            // if we have multiple quests to turn in for an allied society, try and complete all of them
            (ElementId firstTrackedQuest, byte firstTrackedSequence) = trackedQuests.First();
            EAlliedSociety firstTrackedAlliedSociety =
                alliedSocietyData.GetCommonAlliedSocietyTurnIn(firstTrackedQuest);
            if (firstTrackedAlliedSociety != EAlliedSociety.None)
            {
                List<(ElementId Quest, byte Sequence)> alliedQuestsForSameSociety = trackedQuests.Skip(1)
                    .Where(quest =>
                        alliedSocietyData.GetCommonAlliedSocietyTurnIn(quest.Quest) == firstTrackedAlliedSociety)
                    .ToList();
                if (alliedQuestsForSameSociety.Count > 0)
                {
                    if (firstTrackedSequence == 255)
                    {
                        foreach ((ElementId questId, byte sequence) in alliedQuestsForSameSociety)
                        {
                            // only if the other quest isn't ready to be turned in
                            if (sequence != 255 || (questRegistry.TryGetQuest(questId, out Quest? quest) && !IsReadyToCompleteQuest(quest)))
                                return new(questId, sequence, msqQuest.State);
                        }
                    }
                    else if (!IsOnAlliedSocietyMount())
                    {
                        // a few of the vanu quests require you to talk to one of the npcs near the issuer, so we
                        // give priority to those

                        // also include the first quest in the list for those
                        alliedQuestsForSameSociety.Insert(0, (firstTrackedQuest, firstTrackedSequence));

                        alliedSocietyData.GetCommonAlliedSocietyNpcs(firstTrackedAlliedSociety,
                            out uint[]? normalNpcs,
                            out uint[] _);

                        if (normalNpcs.Length > 0)
                        {
                            (ElementId, byte)? talkToNormalNpcs = alliedQuestsForSameSociety
                                .Where(x => x.Sequence < 255)
                                .Where(x => IsInteractSequence(x.Quest, x.Sequence, normalNpcs))
                                .Cast<(ElementId, byte)?>()
                                .FirstOrDefault();
                            if (talkToNormalNpcs != null)
                                return new(talkToNormalNpcs.Value.Item1, talkToNormalNpcs.Value.Item2, msqQuest.State);
                        }

                        /*
                             * TODO: If you have e.g. a mount quest in the middle of 3, it should temporarily make you
                             *       do that quest first, even if it isn't the first in the list. Otherwise, the logic
                             *       here won't make much sense.
                             *
                             * TODO: This also won't work if two or three daily quests use a mount.
                            if (mountNpcs.Length > 0)
                            {
                                var talkToMountNpc = alliedQuestsForSameSociety
                                    .Where(x => x.Sequence < 255)
                                    .Where(x => IsInteractStep(x.Quest, x.Sequence, mountNpcs))
                                    .Cast<(ElementId, byte)?>()
                                    .FirstOrDefault();
                                if (talkToMountNpc != null)
                                    return talkToMountNpc.Value;
                            }
                            */
                    }
                }
            }

            return new(firstTrackedQuest, firstTrackedSequence, msqQuest.State);
        }

        ElementId? priorityQuest = NextPriorityQuestsThatCanBeAccepted
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();
        if (priorityQuest != null)
        {
            // if we have an accepted msq quest, and know of no quest of those currently in the to-do list...
            // (1) try and find a priority quest to do
            return new(priorityQuest, QuestManager.GetQuestSequence(priorityQuest.Value), msqQuest.State);
        }
        else if (msqQuest.CurrentQuest != null)
        {
            // (2) just do a normal msq quest
            return msqQuest;
        }

        return QuestReference.NoQuest(msqQuest.State);
    }

    public (QuestReference, string?) GetMainScenarioQuest()
    {
        if (QuestManager.IsQuestComplete(3759)) // Memories Rekindled
        {
            AgentInterface* questRedoHud = AgentModule.Instance()->GetAgentByInternalId(AgentId.QuestRedoHud);
            if (questRedoHud != null && questRedoHud->IsAgentActive())
            {
                // there's surely better ways to check this, but the one in the OOB Plugin was even less reliable
                if (gameGui.TryGetAddonByName<AtkUnitBase>("QuestRedoHud", out AtkUnitBase* addon) &&
                    addon->AtkValuesCount == 4 &&
                    // 0 seems to be active,
                    // 1 seems to be paused,
                    // 2 is unknown, but it happens e.g. before the quest 'Alzadaal's Legacy'
                    // 3 seems to be having /ng+ open while active,
                    // 4 seems to be when (a) suspending the chapter, or (b) having turned in a quest
                    addon->AtkValues[0].UInt is 0 or 2 or 3 or 4)
                {
                    // redoHud+44 is chapter
                    // redoHud+46 is quest
                    ushort questId = MemoryHelper.Read<ushort>((nint)questRedoHud + 46);
                    return (new(new QuestId(questId), QuestManager.GetQuestSequence(questId), MainScenarioQuestState.Available), "NG+");
                }
            }
        }

        AgentScenarioTree* scenarioTree = AgentScenarioTree.Instance();
        if (scenarioTree == null)
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("No Scenario Tree"));

        if (scenarioTree->Data == null)
            return (QuestReference.NoQuest(MainScenarioQuestState.LoadingScreen), _L("Scenario Tree Data is null"));

        QuestId currentQuest = new(scenarioTree->Data->MainScenarioQuestIds[0]);
        string extraData = $"sq: {currentQuest}";
        if (currentQuest.Value == 0)
        {
            if (IsMainScenarioQuestComplete())
                return (QuestReference.NoQuest(MainScenarioQuestState.Complete), _L("Main Scenario is complete"));

            // fallback lookup; find a quest which isn't completed but where all prequisites are met
            // excluding branching quests

            List<QuestInfo> potentialQuests = questData.MainScenarioQuests
                .Where(x => x.StartingCity == 0 || x.StartingCity == PlayerState.Instance()->StartTown)
                .Where(q => IsReadyToAcceptQuest(q.QuestId, true))
                .ToList();
            if (potentialQuests.Count == 0)
                return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("No potential quests found"));
            else if (potentialQuests.Count > 1)
            {
                // for all of these (except the GC quests), questionable normally auto-picks the next quest based on the
                // agent data. This should (hopefully) pick the exact same quest when the agent does not know for a bit.
                if (potentialQuests.All(x => x.QuestId.Value is 680 or 681 or 682))
                {
                    currentQuest =
                        new(
                            681); // The Company You Keep; actual quest will be resolved later in GetCurrentQuest
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
        QuestManager* questManager = QuestManager.Instance();
        if (IsQuestAccepted(currentQuest) && questManager->GetQuestById(currentQuest.Value)->IsHidden)
            return (QuestReference.NoQuest(MainScenarioQuestState.Available), ("Quest accepted but hidden"));

        // it can sometimes happen (although this isn't reliably reproducible) that the quest returned here
        // is one you've just completed. We return 255 as sequence here, since that is the end of said quest;
        // but this is just really hoping that this breaks nothing.
        if (IsQuestComplete(currentQuest))
            return (new(currentQuest, 255, MainScenarioQuestState.Available), _LF("Quest {0} complete",currentQuest.Value));
        else if (!IsReadyToAcceptQuest(currentQuest))
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _LF("Not ready to accept quest {0}",currentQuest.Value));

        short currentLevel = PlayerState.Instance()->CurrentLevel;

        // are we in a loading screen?
        if (objectTable[0] == null)
            return (QuestReference.NoQuest(MainScenarioQuestState.LoadingScreen), _L("In loading screen"));

        // if we're not at a high enough level to continue, we also ignore it
        if (questRegistry.TryGetQuest(currentQuest, out Quest? quest)
            && quest.Info.Level > currentLevel)
        {
            return (QuestReference.NoQuest(MainScenarioQuestState.Unavailable), _L("Low level"));
        }

        return (new(currentQuest, QuestManager.GetQuestSequence(currentQuest.Value), MainScenarioQuestState.Available), extraData);
    }

    private bool IsOnAlliedSocietyMount()
    {
        BattleChara* battleChara = (BattleChara*)(objectTable[0]?.Address ?? 0);
        return battleChara != null &&
               battleChara->Mount.MountId != 0 &&
               alliedSocietyData.Mounts.ContainsKey(battleChara->Mount.MountId);
    }

    private bool IsReadyToCompleteQuest(Quest quest)
    {
        ToDoListStringArray* toDoListSA = ToDoListStringArray.Instance();
        ToDoListNumberArray* toDoListNA = ToDoListNumberArray.Instance();
        for (int i = 0; i < toDoListNA->QuestTypeIcon.Length; i++)
        {
            if (quest.Info.Name == Encoding.UTF8.GetString(toDoListSA->QuestTexts[i].AsSpan()))
                return toDoListNA->QuestTypeIcon[i] == 71025; // green checkmark quest icon
        }
        return false;
    }

    private bool IsInteractSequence(ElementId questId, byte sequenceNo, uint[] dataIds)
    {
        if (questRegistry.TryGetQuest(questId, out Quest? quest))
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

    public static QuestProgressInfo? GetQuestProgressInfo(ElementId elementId)
    {
        if (elementId is QuestId questId)
        {
            QuestWork* questWork = QuestManager.Instance()->GetQuestById(questId.Value);
            return questWork != null ? new QuestProgressInfo(*questWork) : null;
        }
        else
            return null;
    }

    private CachedValue<List<PriorityQuestInfo>> _nextPriorityQuests = new(ttlSeconds: 2);
    public List<PriorityQuestInfo> NextPriorityQuestsThatCanBeAccepted => _nextPriorityQuests.Get(GetNextPriorityQuestsThatCanBeAccepted);
    private List<PriorityQuestInfo> GetNextPriorityQuestsThatCanBeAccepted()
    {
        // ideally, we'd also be able to afford *some* teleports
        // this implicitly makes sure we're not starting one of the lv1 class quests if we can't afford to teleport back
        //
        // Of course, they can still be accepted manually.
        InventoryManager* inventoryManager = InventoryManager.Instance();
        int gil = inventoryManager->GetItemCountInContainer(1, InventoryType.Currency);

        return GetPriorityQuests()
            .Where(x => IsReadyToAcceptQuest(x))
            .Select(x =>
            {
                if (!questRegistry.TryGetQuest(x, out Quest? quest))
                    return new(x, _L("Unknown quest"));

                QuestStep? firstStep = quest.FindSequence(0)?.FindStep(0);
                if (firstStep == null)
                    return new(x, _L("No sequence 0 with steps"));

                // all priority quests assume we're able to teleport to the beginning (and for e.g. class quests, the end)
                // ideally without having to wait 15m for Return.
                // TODO This should be tweaked for level 1-5 class quests so that all of them can be done without teleport unlocked; the latest we unlock teleport is level 10 for Gridania characters
                //      That probably means that (a) level 1-5 class quests should be doable without any teleports at all, and (b) the return point after being interrupted should be in the main city
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    return new(x, _L("Teleport not unlocked"));

                if (!firstStep.IsTeleportableForPriorityQuests())
                    return new(x, _L("Can't teleport to start"));

                int teleportCosts = TeleportCosts(quest);
                if (gil < teleportCosts)
                {
                    return new(x,
                        _LF("Not enough gil, estimated cost: {0:N0}{1}",teleportCosts,SeIconChar.Gil.ToIconString()));
                }

                EAetheryteLocation? firstLockedAetheryte = quest.AllSteps()
                    .Select(y =>
                    {
                        if (y.Step.AetheryteShortcut is { } aetheryteShortcut &&
                            !aetheryteFunctions.IsAetheryteUnlocked(aetheryteShortcut))
                        {
                            if ((y.Step.SkipConditions?.AetheryteShortcutIf?.AetheryteLocked) != aetheryteShortcut)
                                return aetheryteShortcut;
                        }

                        if (y.Step.AethernetShortcut is { } aethernetShortcut)
                        {
                            if (!aetheryteFunctions.IsAetheryteUnlocked(aethernetShortcut.From))
                                return aethernetShortcut.From;

                            if (!aetheryteFunctions.IsAetheryteUnlocked(aethernetShortcut.To))
                                return aethernetShortcut.To;
                        }

                        return (EAetheryteLocation?)null;
                    })
                    .FirstOrDefault(y => y != null);
                if (firstLockedAetheryte != null)
                {
                    // if quest requires white wolf gate, and unlock quest is available, don't report locked
                    if (firstLockedAetheryte == EAetheryteLocation.GridaniaWhiteWolfGate && IsReadyToAcceptQuest(new QuestId(802)))
                        return new(x);
                    return new(x, _LF("Aetheryte locked: {0}",firstLockedAetheryte));
                }

                return new PriorityQuestInfo(x);
            })
            .ToList();
    }

    private int TeleportCosts(Quest quest)
    {
        List<EAetheryteLocation> teleportTargets = quest.AllSteps()
            .Where(x => x.Step.AetheryteShortcut != null)
            .Select(x => x.Step.AetheryteShortcut!.Value)
            .ToList();
        if (teleportTargets.Count == 0)
            return 0;

        Telepo* telepo = Telepo.Instance();
        if (telepo == null)
            return 0;

        Dictionary<uint, uint> teleportCosts = [];
        foreach (TeleportInfo info in telepo->TeleportList)
            teleportCosts.TryAdd(info.AetheryteId, info.GilCost);

        return teleportTargets.Sum(x => (int)teleportCosts.GetValueOrDefault((uint)x, 999u));
    }

    public List<ElementId> GetPriorityQuests(bool onlyClassAndRoleQuests = false)
    {
        List<ElementId> priorityQuests = [];
        if (!onlyClassAndRoleQuests)
        {
            if (!configuration.Advanced.SkipARealmRebornHardModePrimals)
            {
                // Ifrit quest is handled as a pickup during MSQ
                priorityQuests.AddRange(QuestData.HardModePrimals.Skip(1));
            }

            if (!configuration.Advanced.SkipCrystalTowerRaids)
                priorityQuests.AddRange(QuestData.CrystalTowerQuests);
        }

        if (!configuration.Advanced.SkipClassJobQuests)
        {
            Job classJob = (Job?)PlayerState.Instance()->CurrentClassJobId ?? Job.ADV;
            uint[] shadowbringersRoleQuestChapters = QuestData.AllRoleQuestChapters.Select(x => x[0]).ToArray();
            if (classJob != Job.ADV)
            {
                priorityQuests.AddRange(questRegistry.GetKnownClassJobQuests(classJob)
                    .Where(x =>
                    {
                        if (!questRegistry.TryGetQuest(x.QuestId, out Quest? quest) ||
                            quest.Info is not QuestInfo questInfo)
                        {
                            return false;
                        }

                        // if no shadowbringers role quest is complete, (at least one) is required
                        if (shadowbringersRoleQuestChapters.Contains(questInfo.NewGamePlusChapter))
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
            .Where(questRegistry.IsKnownQuest)
            .ToList();
    }

    public bool IsReadyToAcceptQuest(ElementId questId, bool ignoreLevel = false)
    {
        questRegistry.TryGetQuest(questId, out Quest? quest);
        if (quest is { Info.IsRepeatable: true })
        {
            if (IsQuestAccepted(questId))
                return false;

            if (questId is QuestId qId && IsDailyAlliedSocietyQuest(qId))
            {
                if (QuestManager.Instance()->IsDailyQuestCompleted(questId.Value))
                    return false;

                if (!IsDailyAlliedSocietyQuestAndAvailableToday(qId))
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
            // if we're not at a high enough level to continue, we also ignore it
            short currentLevel = PlayerState.Instance()->CurrentLevel;
            if (currentLevel != 0 && quest != null && quest.Info.Level > currentLevel)
                return false;
        }

        if (IsQuestLocked(questId) is (bool isLocked, var _))
            return !isLocked;

        return true;
    }

    public bool IsQuestAcceptedOrComplete(ElementId elementId) => IsQuestComplete(elementId) || IsQuestAccepted(elementId);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static")]
    public bool IsQuestAccepted(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestAccepted(questId);
        else if (elementId is SatisfactionSupplyNpcId)
            return false;
        else if (elementId is AlliedSocietyDailyId)
            return false;
        else if (elementId is UnlockLinkId)
            return false;
        else
            throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    public static bool IsQuestAccepted(QuestId questId)
    {
        QuestManager* questManager = QuestManager.Instance();
        return questManager->IsQuestAccepted(questId.Value);
    }

    public bool IsQuestComplete(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestComplete(questId);
        else if (elementId is SatisfactionSupplyNpcId)
            return false;
        else if (elementId is AlliedSocietyDailyId)
            return false;
        else if (elementId is UnlockLinkId unlockLinkId)
            return IsQuestComplete(unlockLinkId);
        else
            throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    public bool IsQuestComplete(QuestId questId) => QuestManager.IsQuestComplete(questId.Value);

    public bool IsQuestComplete(UnlockLinkId unlockLinkId) => UIState.Instance()->IsUnlockLinkUnlocked(unlockLinkId.Value);

    public (bool,string[]?) IsQuestLocked(ElementId elementId, ElementId? extraCompletedQuest = null)
    {
        if (elementId is QuestId questId)
            return IsQuestLocked(questId, extraCompletedQuest);
        else if (elementId is SatisfactionSupplyNpcId satisfactionSupplyNpcId)
            return (IsQuestLocked(satisfactionSupplyNpcId),null);
        else if (elementId is AlliedSocietyDailyId alliedSocietyDailyId)
            return (IsQuestLocked(alliedSocietyDailyId),null);
        else if (elementId is UnlockLinkId unlockLinkId)
            return (IsQuestLocked(unlockLinkId),null);
        else
            throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    private (bool,string[]) IsQuestLocked(QuestId questId, ElementId? extraCompletedQuest = null)
    {
        Dictionary<string,bool> lockedReason = [];
        lockedReason.Add("Unobtainable", IsQuestUnobtainable(questId, extraCompletedQuest));

        QuestInfo questInfo = (QuestInfo)questData.GetQuestInfo(questId);
        if (questInfo.GrandCompany != GrandCompany.None)
            lockedReason.Add("Grand company mismatch", questInfo.GrandCompany != GetGrandCompany());

        if (questInfo.AlliedSociety != EAlliedSociety.None)
            if (questInfo.IsRepeatable)
                lockedReason.Add("Daily society quest is not available today", !IsDailyAlliedSocietyQuestAndAvailableToday(questId));
            else
                lockedReason.Add("Society story quest is not available", !IsAlliedSocietyStoryQuestAvailable(questId));

        if (questInfo.IsMoogleDeliveryQuest)
        {
            byte currentDeliveryLevel = PlayerState.Instance()->DeliveryLevel;
            if (extraCompletedQuest != null &&
                questData.TryGetQuestInfo(extraCompletedQuest, out IQuestInfo? extraQuestInfo) &&
                extraQuestInfo is QuestInfo { IsMoogleDeliveryQuest: true })
                currentDeliveryLevel++;

            lockedReason.Add("Moogle quest delivery level is too low", questInfo.MoogleDeliveryLevel > currentDeliveryLevel);
        }

        // "an ill-conceived venture" requires to have retainers unlocked
        if ((new ushort[]{ 1432,1433,1434 }).Contains(questId.Value))
        {
            var retainerManager = RetainerManager.Instance();
            lockedReason.Add("Retainers are not unlocked", retainerManager->MaxRetainerEntitlement == 0);
        }

        lockedReason.Add("Prev quests not completed", !HasCompletedPreviousQuests(questInfo, extraCompletedQuest));
        lockedReason.Add("Prev instances not completed", !HasCompletedPreviousInstances(questInfo));
        return (lockedReason.Values.Any(x => x),lockedReason.Keys.ToArray());
    }

    private bool IsQuestLocked(SatisfactionSupplyNpcId satisfactionSupplyNpcId)
    {
        SatisfactionSupplyInfo questInfo = (SatisfactionSupplyInfo)questData.GetQuestInfo(satisfactionSupplyNpcId);
        return !HasCompletedPreviousQuests(questInfo, null);
    }

    private bool IsQuestLocked(AlliedSocietyDailyId alliedSocietyDailyId)
    {
        PlayerState* playerState = PlayerState.Instance();
        byte currentRank = playerState->GetBeastTribeRank(alliedSocietyDailyId.AlliedSociety);
        return currentRank == 0 || currentRank < alliedSocietyDailyId.Rank;
    }

    private static bool IsQuestLocked(UnlockLinkId unlockLinkId) => IsQuestUnobtainable(unlockLinkId);

    public bool IsDailyAlliedSocietyQuest(QuestId questId)
    {
        QuestInfo questInfo = (QuestInfo)questData.GetQuestInfo(questId);
        return questInfo.AlliedSociety != EAlliedSociety.None && questInfo.IsRepeatable;
    }

    public bool IsDailyAlliedSocietyQuestAndAvailableToday(QuestId questId)
    {
        if (!IsDailyAlliedSocietyQuest(questId))
            return false;

        QuestInfo questInfo = (QuestInfo)questData.GetQuestInfo(questId);
        return alliedSocietyQuestFunctions.GetAvailableAlliedSocietyQuests(questInfo.AlliedSociety).Contains(questId);
    }

    public bool IsAlliedSocietyStoryQuestAvailable(QuestId questId)
    {
        QuestInfo questInfo = (QuestInfo)questData.GetQuestInfo(questId);
        (EAlliedSocietyRank currentRank, ushort currentRep, ushort neededRep) = GetAlliedSocietyRankAndRep(questInfo.AlliedSociety);
        return currentRank > questInfo.AlliedSocietyRank || currentRep >= neededRep;
    }

    public (EAlliedSocietyRank, ushort, ushort) GetAlliedSocietyRankAndRep(EAlliedSociety alliedSociety) =>
        ((EAlliedSocietyRank)PlayerState.Instance()->GetBeastTribeRank((byte)alliedSociety),
        PlayerState.Instance()->GetBeastTribeCurrentReputation((byte)alliedSociety),
        PlayerState.Instance()->GetBeastTribeNeededReputation((byte)alliedSociety));

    public bool IsQuestUnobtainable(ElementId elementId, ElementId? extraCompletedQuest = null)
    {
        if (elementId is QuestId questId)
            return IsQuestUnobtainable(questId, extraCompletedQuest);
        else if (elementId is UnlockLinkId unlockLinkId)
            return IsQuestUnobtainable(unlockLinkId);
        else
            return false;
    }

    public bool IsQuestUnobtainable(QuestId questId, ElementId? extraCompletedQuest = null)
    {
        QuestInfo questInfo = (QuestInfo)questData.GetQuestInfo(questId);
        if (questInfo.Expansion > (EExpansionVersion)PlayerState.Instance()->MaxExpansion)
            return true;

        if (questInfo.QuestLocks.Count > 0)
        {
            int completedQuests = questInfo.QuestLocks.Count(x => IsQuestComplete(x) || x.Equals(extraCompletedQuest));
            if (questInfo.QuestLockJoin == EQuestJoin.All && questInfo.QuestLocks.Count == completedQuests)
                return true;
            else if (questInfo.QuestLockJoin == EQuestJoin.AtLeastOne && completedQuests > 0)
                return true;
        }

        if (questData.GetLockedClassQuests().Contains(questId))
            return true;

        byte startingCity = PlayerState.Instance()->StartTown;
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

        // The starting class experience is a bit confusing. If you start in Gridania, the MSQ next quest data will
        // always select 'Close to Home (Lancer)' even if starting as Conjurer/Archer. However, if we always mark the
        // Lancer quest as unobtainable, it'll not get picked up as Conjurer/Archer, and thus will stop questing.
        //
        // While the NPC offers all 3 quests, there's no manual selection, and interacting will automatically select the
        // quest for your current class, then switch you from a dead-ish intro zone to the actual starting city
        // (so that you can't come back later to pick up another quest).
        if (closeToHomeQuests.TryGetValue(questId.Value, out Job neededStartingClass) &&
            closeToHomeQuests.Any(x => IsQuestAcceptedOrComplete(new QuestId(x.Key))))
        {
            Job actualStartingClass = (Job)PlayerState.Instance()->FirstClass;
            if (actualStartingClass != neededStartingClass)
                return true;
        }

        if (IsQuestRemoved(questId))
            return true;

        return false;
    }

    /// <summary>
    ///     All unlock links (presumably) have unique conditions, be that quests or otherwise.
    /// </summary>
    private static bool IsQuestUnobtainable(UnlockLinkId unlockLinkId)
    {
        if (unlockLinkId.Value == 506)
            return !IsFestivalActive(160, 2);
        else if (unlockLinkId.Value == 568)
            return !IsFestivalActive(160, 3);
        else
            return true;
    }

    private static bool IsFestivalActive(ushort id, ushort? phase = null)
    {
        for (int i = 0; i < GameMain.Instance()->ActiveFestivals.Length; ++i)
        {
            GameMain.Festival festival = GameMain.Instance()->ActiveFestivals[i];
            if (festival.Id == id)
                return phase == null || festival.Phase == phase;
        }

        return false;
    }

    public static bool IsQuestRemoved(ElementId elementId)
    {
        if (elementId is QuestId questId)
            return IsQuestRemoved(questId);
        else
            return false;
    }

    private static bool IsQuestRemoved(QuestId questId) => questId.Value is 487 or 1428 or 1429;

    private bool HasCompletedPreviousQuests(IQuestInfo questInfo, ElementId? extraCompletedQuest)
    {
        if (questInfo.PreviousQuests.Count == 0)
            return true;

        int completedQuests = questInfo.PreviousQuests.Count(x =>
            HasEnoughProgressOnPreviousQuest(x) || x.QuestId.Equals(extraCompletedQuest));
        if (questInfo.PreviousQuestJoin == EQuestJoin.All &&
            questInfo.PreviousQuests.Count == completedQuests)
            return true;
        else if (questInfo.PreviousQuestJoin == EQuestJoin.AtLeastOne && completedQuests > 0)
            return true;
        else
            return false;
    }

    private bool HasEnoughProgressOnPreviousQuest(PreviousQuestInfo previousQuestInfo)
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

    private static bool HasCompletedPreviousInstances(QuestInfo questInfo)
    {
        if (questInfo.PreviousInstanceContent.Count == 0)
        {
            return true;
        }

        int completedInstances = questInfo.PreviousInstanceContent.Count(x => UIState.IsInstanceContentCompleted(x));

        if (questInfo.PreviousInstanceContentJoin == EQuestJoin.All &&
            questInfo.PreviousInstanceContent.Count == completedInstances)
            return true;
        else if (questInfo.PreviousInstanceContentJoin == EQuestJoin.AtLeastOne && completedInstances > 0)
            return true;
        else
            return false;
    }

    public bool IsClassJobUnlocked(Job classJob)
    {
        ClassJob classJobRow = dataManager.GetExcelSheet<ClassJob>().GetRow((uint)classJob);
        ushort questId = (ushort)classJobRow.UnlockQuest.RowId;
        if (questId != 0)
            return IsQuestComplete(new QuestId(questId));

        PlayerState* playerState = PlayerState.Instance();
        return playerState != null && playerState->ClassJobLevels[classJobRow.ExpArrayIndex] > 0;
    }

    public bool IsJobUnlocked(Job classJob)
    {
        ClassJob classJobRow = dataManager.GetExcelSheet<ClassJob>().GetRow((uint)classJob);
        return IsClassJobUnlocked((Job)classJobRow.ClassJobParent.RowId);
    }

    public GrandCompany GetGrandCompany() => (GrandCompany)PlayerState.Instance()->GrandCompany;

    public bool IsMainScenarioQuestComplete() => IsQuestComplete(questData.LastMainScenarioQuestId);
}

public sealed record QuestReference(ElementId? CurrentQuest, byte Sequence, MainScenarioQuestState State)
{
    public static QuestReference NoQuest(MainScenarioQuestState state) => new(null, 0, state);
}

public enum MainScenarioQuestState
{
    Unavailable,
    Available,
    Complete,
    LoadingScreen
}

internal sealed record PriorityQuestInfo(ElementId QuestId, string? UnavailableReason = null)
{
    public bool IsAvailable => UnavailableReason == null;
}

public class RequiredTeleportLockedException : Exception
{
    public RequiredTeleportLockedException()
    {
    }

    public RequiredTeleportLockedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public RequiredTeleportLockedException(string message) : base(message)
    {
    }
}