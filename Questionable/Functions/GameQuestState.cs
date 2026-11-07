using System.Text;
using Dalamud.Memory;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Achievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace Questionable.Functions;

[RegisterSingleton<IQuestGameState, GameQuestState>]
internal sealed unsafe class GameQuestState(
    AetheryteFunctions aetheryteFunctions,
    AlliedSocietyQuestFunctions alliedSocietyQuests,
    IClientState clientState,
    IObjectTable objectTable,
    IGameGuiAdapter gameGui) : IQuestGameState
{
    private const int TurnInIcon = 71025;

    private readonly AchievementCache _achievements = new(
        () => ProfileController.CurrentContentId,
        () => Achievement.Instance()->IsLoaded(),
        id => Achievement.Instance()->IsComplete(id),
        () => AgentAchievement.Instance()->Show(),
        () => AgentAchievement.Instance()->Hide());

    public bool HasQuestManager => QuestManager.Instance() != null;
    public bool IsQuestAccepted(ushort questId) => QuestManager.Instance()->IsQuestAccepted(questId);
    public bool IsQuestComplete(ushort questId) => QuestManager.IsQuestComplete(questId);
    public byte GetQuestSequence(ushort questId) => QuestManager.GetQuestSequence(questId);

    public QuestProgressInfo? GetQuestProgress(ushort questId)
    {
        QuestWork* questWork = QuestManager.Instance()->GetQuestById(questId);
        return questWork != null ? new QuestProgressInfo(*questWork) : null;
    }

    public bool IsDailyQuestCompleted(ushort questId) => QuestManager.Instance()->IsDailyQuestCompleted(questId);

    public IReadOnlyList<ushort> TrackedQuestIds
    {
        get
        {
            QuestManager* questManager = QuestManager.Instance();
            if (questManager == null)
                return [];

            List<ushort> tracked = [];
            for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
            {
                TrackingWork trackedQuest = questManager->TrackedQuests[i];
                if (trackedQuest.QuestType == 1)
                    tracked.Add(questManager->NormalQuests[trackedQuest.Index].QuestId);
            }

            return tracked;
        }
    }

    public IReadOnlyList<(ushort QuestId, byte Sequence, bool IsHidden)> NormalQuests
    {
        get
        {
            QuestManager* questManager = QuestManager.Instance();
            if (questManager == null)
                return [];

            List<(ushort, byte, bool)> quests = [];
            foreach (QuestWork questWork in questManager->NormalQuests)
            {
                if (questWork.QuestId != 0)
                    quests.Add((questWork.QuestId, questWork.Sequence, questWork.IsHidden));
            }

            return quests;
        }
    }

    public bool IsReadyToTurnIn(string questName)
    {
        ToDoListStringArray* toDoStrings = ToDoListStringArray.Instance();
        ToDoListNumberArray* toDoNumbers = ToDoListNumberArray.Instance();
        for (int i = 0; i < toDoNumbers->QuestTypeIcon.Length; i++)
        {
            if (questName == Encoding.UTF8.GetString(toDoStrings->QuestTexts[i].AsSpan()))
                return toDoNumbers->QuestTypeIcon[i] == TurnInIcon;
        }

        return false;
    }

    public short CurrentLevel => PlayerState.Instance()->CurrentLevel;
    public short MaxLevel => PlayerState.Instance()->MaxLevel;
    public byte CurrentClassJobId => PlayerState.Instance()->CurrentClassJobId;
    public IReadOnlyList<short> ClassJobLevels =>
        PlayerState.Instance() is var playerState && playerState != null ? playerState->ClassJobLevels.ToArray() : [];

    public short GetClassJobLevel(Job classJob)
    {
        PlayerState* playerState = PlayerState.Instance();
        int index = classJob.GetData().ExpArrayIndex;
        return playerState != null && index >= 0 && index < playerState->ClassJobLevels.Length
            ? playerState->ClassJobLevels[index]
            : (short)0;
    }

    public byte StartTown => PlayerState.Instance()->StartTown;
    public byte MaxExpansion => PlayerState.Instance()->MaxExpansion;
    public byte FirstClass => PlayerState.Instance()->FirstClass;
    public byte GrandCompany => PlayerState.Instance()->GrandCompany;
    public byte GrandCompanyRank => PlayerState.Instance()->GetGrandCompanyRank();
    public byte DeliveryLevel => PlayerState.Instance()->DeliveryLevel;
    public byte GetAlliedSocietyRank(byte alliedSociety) => PlayerState.Instance()->GetBeastTribeRank(alliedSociety);
    public ushort GetReputation(byte alliedSociety) => PlayerState.Instance()->GetBeastTribeCurrentReputation(alliedSociety);
    public ushort GetNeededReputation(byte alliedSociety) => PlayerState.Instance()->GetBeastTribeNeededReputation(alliedSociety);

    public IReadOnlyCollection<QuestId> GetAvailableSocietyQuests(EAlliedSociety alliedSociety) =>
        alliedSocietyQuests.GetAvailableQuests(alliedSociety);

    public bool HasRetainers => RetainerManager.Instance()->MaxRetainerEntitlement != 0;
    public bool IsInstanceContentCompleted(ushort instanceContentId) => UIState.IsInstanceContentCompleted(instanceContentId);

    public bool IsFestivalActive(ushort festivalId, ushort? phase)
    {
        for (int i = 0; i < GameMain.Instance()->ActiveFestivals.Length; ++i)
        {
            GameMain.Festival festival = GameMain.Instance()->ActiveFestivals[i];
            if (festival.Id == festivalId)
                return phase == null || festival.Phase == phase;
        }

        return false;
    }

    public bool IsAetheryteUnlocked(EAetheryteLocation aetheryte) => aetheryteFunctions.IsAetheryteUnlocked(aetheryte);
    public bool IsTeleportUnlocked => aetheryteFunctions.IsTeleportUnlocked();
    public int Gil => InventoryManager.Instance()->GetItemCountInContainer(1, InventoryType.Currency);

    public IReadOnlyDictionary<uint, uint>? TeleportCosts
    {
        get
        {
            Telepo* telepo = Telepo.Instance();
            if (telepo == null)
                return null;

            Dictionary<uint, uint> costs = [];
            foreach (TeleportInfo info in telepo->TeleportList)
                costs.TryAdd(info.AetheryteId, info.GilCost);
            return costs;
        }
    }

    public uint TerritoryType => clientState.TerritoryType;
    public bool HasPlayer => objectTable[0] != null;

    public ushort CurrentMountId
    {
        get
        {
            BattleChara* battleChara = (BattleChara*)(objectTable[0]?.Address ?? 0);
            return battleChara != null ? battleChara->Mount.MountId : (ushort)0;
        }
    }

    public ScenarioGuide ReadScenarioGuide()
    {
        AgentScenarioTree* scenarioTree = AgentScenarioTree.Instance();
        if (scenarioTree == null)
            return new(EScenarioGuideState.Missing);
        if (scenarioTree->Data == null)
            return new(EScenarioGuideState.Loading);
        return new(EScenarioGuideState.Ready, scenarioTree->Data->MainScenarioQuestIds[0]);
    }

    public ushort? NewGamePlusQuestId
    {
        get
        {
            AgentInterface* questRedoHud = AgentModule.Instance()->GetAgentByInternalId(AgentId.QuestRedoHud);
            if (questRedoHud == null || !questRedoHud->IsAgentActive())
                return null;

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
                return MemoryHelper.Read<ushort>((nint)questRedoHud + 46);
            }

            return null;
        }
    }

    public byte ChocoboRacingRank => RaceChocoboManager.Instance()->Rank;

    public byte? DomanEnclaveMilestone
    {
        get
        {
            DomanEnclaveManager* enclave = DomanEnclaveManager.Instance();
            return enclave->IsLoaded ? enclave->State.CurrentMilestone : null;
        }
    }

    public bool IsAchievementComplete(int[] achievementIds) => _achievements.IsComplete(achievementIds);
    public bool IsMountUnlocked(ushort mountId) => PlayerState.Instance()->IsMountUnlocked(mountId);
    public bool IsUnlockLinkUnlocked(ushort unlockLinkId) => UIState.Instance()->IsUnlockLinkUnlocked(unlockLinkId);
    public string AchievementName(int achievementId) => _T<Lumina.Excel.Sheets.Achievement>((uint)achievementId);
    public string MountName(ushort mountId) => _T<Mount>(mountId);
}
