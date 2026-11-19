using ECommons.ExcelServices;

namespace Questionable.Functions;

internal interface IQuestGameState : IPrerequisiteState
{
    bool HasQuestManager { get; }
    bool IsQuestAccepted(ushort questId);
    bool IsQuestComplete(ushort questId);
    byte GetQuestSequence(ushort questId);
    QuestProgressInfo? GetQuestProgress(ushort questId);

    QuestProgressInfo? GetQuestProgressInfo(ElementId elementId) =>
        elementId is QuestId questId ? GetQuestProgress(questId.Value) : null;
    bool IsDailyQuestCompleted(ushort questId);
    uint SocietyAllowances { get; }
    IReadOnlyList<(ushort Id, ushort Phase)> ActiveFestivals { get; }

    // Normal quests in to-do list order.
    IReadOnlyList<ushort> TrackedQuestIds { get; }
    IReadOnlyList<(ushort QuestId, byte Sequence, bool IsHidden)> NormalQuests { get; }
    bool IsReadyToTurnIn(string questName);
    bool HasCraftMaterials(uint itemId);

    short CurrentLevel { get; }
    short MaxLevel { get; }
    byte CurrentClassJobId { get; }
    IReadOnlyList<short> ClassJobLevels { get; }
    short GetClassJobLevel(Job classJob);
    byte StartTown { get; }
    byte MaxExpansion { get; }
    byte FirstClass { get; }
    byte GrandCompany { get; }
    byte GrandCompanyRank { get; }
    byte DeliveryLevel { get; }
    byte GetAlliedSocietyRank(byte alliedSociety);
    ushort GetReputation(byte alliedSociety);
    ushort GetNeededReputation(byte alliedSociety);
    IReadOnlyCollection<QuestId> GetAvailableSocietyQuests(EAlliedSociety alliedSociety);

    bool HasRetainers { get; }
    bool IsInstanceContentCompleted(ushort instanceContentId);
    bool IsFestivalActive(ushort festivalId, ushort? phase);
    bool IsAetheryteUnlocked(EAetheryteLocation aetheryte);
    bool IsAetherCurrentUnlocked(uint aetherCurrentId);
    bool IsTeleportUnlocked { get; }
    int Gil { get; }
    IReadOnlyDictionary<uint, uint>? TeleportCosts { get; }
    IReadOnlyList<EAetheryteLocation> AetherytesIn(uint territoryId);
    // Zones in one region are walkable from each other (city halves, a city and its neighbouring zones).
    uint RegionOf(uint territoryId);

    uint TerritoryType { get; }
    bool HasPlayer { get; }
    ushort CurrentMountId { get; }

    ScenarioGuide ReadScenarioGuide();
    ushort? NewGamePlusQuestId { get; }
}

internal enum EScenarioGuideState
{
    Missing,
    Loading,
    Ready,
}

internal sealed record ScenarioGuide(EScenarioGuideState State, ushort QuestId = 0);
