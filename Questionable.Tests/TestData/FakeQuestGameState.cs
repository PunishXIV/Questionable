using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Tests.TestData;

internal sealed class FakeQuestGameState : IQuestGameState
{
    public HashSet<ushort> Accepted { get; } = [];
    public HashSet<ushort> Complete { get; } = [];
    public HashSet<ushort> Hidden { get; } = [];
    public HashSet<ushort> DailyCompleted { get; } = [];
    public Dictionary<ushort, byte> Sequences { get; } = [];
    public HashSet<ushort> UnlockLinks { get; } = [];
    public HashSet<ushort> CompletedInstances { get; } = [];
    public HashSet<EAetheryteLocation> LockedAetherytes { get; } = [];
    public Dictionary<EAlliedSociety, List<QuestId>> AvailableAlliedSocietyQuests { get; } = [];

    public bool HasQuestManager { get; set; } = true;
    public bool IsQuestAccepted(ushort questId) => Accepted.Contains(questId);
    public bool IsQuestComplete(ushort questId) => Complete.Contains(questId);
    public byte GetQuestSequence(ushort questId) => Sequences.GetValueOrDefault(questId);

    public QuestProgressInfo? GetQuestProgress(ushort questId) =>
        Accepted.Contains(questId)
            ? new QuestProgressInfo(new QuestWork { QuestId = questId, Sequence = GetQuestSequence(questId), IsHidden = Hidden.Contains(questId) })
            : null;

    public bool IsDailyQuestCompleted(ushort questId) => DailyCompleted.Contains(questId);
    public IReadOnlyList<ushort> TrackedQuestIds { get; set; } = [];
    public IReadOnlyList<(ushort QuestId, byte Sequence, bool IsHidden)> NormalQuests { get; set; } = [];
    public bool IsReadyToTurnIn(string questName) => false;

    public short CurrentLevel { get; set; } = 100;
    public short MaxLevel { get; set; } = 100;
    public byte CurrentClassJobId { get; set; }
    public IReadOnlyList<short> ClassJobLevels { get; set; } = [];
    public short GetClassJobLevel(Job classJob) => CurrentLevel;
    public byte StartTown { get; set; } = 1;
    public byte MaxExpansion { get; set; } = (byte)EExpansionVersion.Dawntrail;
    public byte FirstClass { get; set; }
    public byte GrandCompany { get; set; }
    public byte GrandCompanyRank { get; set; }
    public byte DeliveryLevel { get; set; }
    public Dictionary<byte, (byte Rank, ushort Reputation, ushort Needed)> AlliedSocieties { get; } = [];
    public byte GetAlliedSocietyRank(byte alliedSociety) => AlliedSocieties.GetValueOrDefault(alliedSociety).Rank;
    public ushort GetAlliedSocietyReputation(byte alliedSociety) => AlliedSocieties.GetValueOrDefault(alliedSociety).Reputation;
    public ushort GetAlliedSocietyNeededReputation(byte alliedSociety) => AlliedSocieties.GetValueOrDefault(alliedSociety).Needed;

    public IReadOnlyCollection<QuestId> GetAvailableAlliedSocietyQuests(EAlliedSociety alliedSociety) =>
        AvailableAlliedSocietyQuests.GetValueOrDefault(alliedSociety) ?? [];

    public bool HasRetainers { get; set; } = true;
    public bool IsInstanceContentCompleted(ushort instanceContentId) => CompletedInstances.Contains(instanceContentId);
    public bool IsFestivalActive(ushort festivalId, ushort? phase) => false;
    public bool IsAetheryteUnlocked(EAetheryteLocation aetheryte) => !LockedAetherytes.Contains(aetheryte);
    public bool IsTeleportUnlocked { get; set; } = true;
    public int Gil { get; set; } = 100_000;
    public IReadOnlyDictionary<uint, uint>? TeleportCosts { get; set; }
    public uint TerritoryType { get; set; }
    public bool HasPlayer { get; set; } = true;
    public ushort CurrentMountId { get; set; }

    public ScenarioGuide Guide { get; set; } = new(EScenarioGuideState.Ready);
    public ScenarioGuide ReadScenarioGuide() => Guide;
    public ushort? NewGamePlusQuestId { get; set; }

    public byte ChocoboRacingRank { get; set; }
    public byte? DomanEnclaveMilestone { get; set; }
    public bool IsAchievementComplete(int[] achievementIds) => false;
    public bool IsMountUnlocked(ushort mountId) => false;
    public bool IsUnlockLinkUnlocked(ushort unlockLinkId) => UnlockLinks.Contains(unlockLinkId);
    public string AchievementName(int achievementId) => $"A{achievementId}";
    public string MountName(ushort mountId) => $"M{mountId}";
}

internal sealed class FakeQuestCatalog : IQuestCatalog
{
    private readonly Dictionary<ElementId, Quest> _quests = [];

    public Quest Add(QuestInfo info, params QuestSequence[] sequences)
    {
        var quest = new Quest
        {
            Id = info.QuestId,
            Source = Quest.ESource.Assembly,
            Root = new QuestRoot { QuestSequence = [.. sequences] },
            Info = info,
        };
        _quests[info.QuestId] = quest;
        return quest;
    }

    public bool TryGetQuestInfo(ElementId elementId, [NotNullWhen(true)] out IQuestInfo? questInfo)
    {
        questInfo = _quests.TryGetValue(elementId, out Quest? quest) ? quest.Info : null;
        return questInfo != null;
    }

    public IQuestInfo GetQuestInfo(ElementId elementId) => _quests[elementId].Info;
    public IReadOnlyList<QuestInfo> MainScenarioQuests => [.. _quests.Values.Select(x => x.Info).OfType<QuestInfo>().Where(x => x.IsMainScenarioQuest)];
    public QuestId LastMainScenarioQuestId { get; set; } = new(9999);
    public IReadOnlyCollection<QuestId> GetLockedClassQuests() => [];

    public bool TryGetQuest(ElementId questId, [NotNullWhen(true)] out Quest? quest) => _quests.TryGetValue(questId, out quest);
    public bool IsKnownQuest(ElementId questId) => _quests.ContainsKey(questId);
    public IReadOnlyList<(uint CfcId, ElementId QuestId, int Sequence)> LowPriorityCfcQuests => [];
    public IReadOnlyList<QuestInfo> GetKnownClassJobQuests(Job classJob) => [];
    public (ushort UnlockQuestId, int ExpArrayIndex) GetClassJobUnlock(Job classJob) => (0, 0);
}

// QuestInfo is normally built from a Lumina row; tests fill its backing fields directly.
internal static class TestQuestInfo
{
    public static QuestInfo Create(ushort id, string? name = null, ushort level = 1, bool mainScenario = false,
        EQuestJoin previousJoin = EQuestJoin.All, params PreviousQuestInfo[] previous)
    {
        var info = (QuestInfo)RuntimeHelpers.GetUninitializedObject(typeof(QuestInfo));
        Set(info, nameof(QuestInfo.QuestId), new QuestId(id));
        Set(info, nameof(QuestInfo.Name), name ?? $"Quest {id}");
        Set(info, nameof(QuestInfo.BaseName), name ?? $"Quest {id}");
        Set(info, nameof(QuestInfo.Level), level);
        Set(info, nameof(QuestInfo.IsMainScenarioQuest), mainScenario);
        Set(info, nameof(QuestInfo.PreviousQuests), previous.ToImmutableList());
        Set(info, nameof(QuestInfo.PreviousQuestJoin), previousJoin);
        Set(info, nameof(QuestInfo.QuestLocks), ImmutableList<QuestId>.Empty);
        Set(info, nameof(QuestInfo.PreviousInstanceContent), new List<ushort>());
        Set(info, nameof(QuestInfo.ClassJobs), (IReadOnlyList<Job>)[]);
        Set(info, nameof(QuestInfo.ActionUnlock), new List<string>());
        Set(info, nameof(QuestInfo.ToDoLocations), new List<QuestInfo.SheetLevel>());
        Set(info, nameof(QuestInfo.ItemRewardsRaw), (IReadOnlyList<uint>)[]);
        Set(info, nameof(QuestInfo.ItemRewards), (IReadOnlyList<ItemReward>)[]);
        Set(info, nameof(QuestInfo.TripleTriadCardRewards), (IReadOnlyList<ItemReward>)[]);
        return info;
    }

    public static QuestInfo With<T>(this QuestInfo info, string property, T value)
    {
        Set(info, property, value);
        return info;
    }

    private static void Set<T>(QuestInfo info, string property, T value) =>
        typeof(QuestInfo).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(info, value);
}
