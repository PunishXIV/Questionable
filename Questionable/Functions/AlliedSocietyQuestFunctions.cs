using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Functions;

internal sealed class AlliedSocietyQuestFunctions
{
    private readonly Dictionary<EAlliedSociety, byte> _alliedSocietyLastSeenRank = [];
    private readonly Dictionary<(uint NpcDataId, byte Seed, bool OutranksAll, bool RankedUp), List<QuestId>> _dailyQuests = [];
    private readonly ILogger<AlliedSocietyQuestFunctions> _logger;
    private readonly QuestData _questData;
    private readonly Dictionary<EAlliedSociety, List<NpcData>> _questsByAlliedSociety = [];

    public AlliedSocietyQuestFunctions(QuestData questData, ILogger<AlliedSocietyQuestFunctions> logger)
    {
        _questData = questData;
        _logger = logger;
        Initialize();
    }

    public void Initialize()
    {
        foreach (EAlliedSociety alliedSociety in Enum.GetValues<EAlliedSociety>().Where(x => x != EAlliedSociety.None))
        {
            List<QuestInfo> allQuests = _questData.GetAllByAlliedSociety(alliedSociety);
            Dictionary<uint, List<QuestInfo>> questsByIssuer = allQuests
                .Where(x => x.IsRepeatable)
                .GroupBy(x => x.IssuerDataId)
                .ToDictionary(x => x.Key,
                    x => x.OrderBy(y => y.AlliedSocietyQuestGroup == 3).ThenBy(y => y.QuestId).ToList());
            foreach ((uint issuerDataId, List<QuestInfo> quests) in questsByIssuer)
            {
                NpcData npcData = new()
                { IssuerDataId = issuerDataId, AllQuests = quests };
                if (_questsByAlliedSociety.TryGetValue(alliedSociety, out List<NpcData>? existingNpcs))
                    existingNpcs.Add(npcData);
                else
                    _questsByAlliedSociety[alliedSociety] = [npcData];
            }
        }
    }

    public void Reload()
    {
        foreach ((uint NpcDataId, byte Seed, bool OutranksAll, bool RankedUp) item in _dailyQuests.Keys)
            _dailyQuests.Remove(item);
    }

    public unsafe List<QuestId> GetAvailableAlliedSocietyQuests(EAlliedSociety alliedSociety)
    {
        byte rankData = QuestManager.Instance()->BeastReputation[(int)alliedSociety - 1].Rank;
        byte currentRank = (byte)(rankData & 0x7F);
        if (currentRank == 0)
            return [];

        bool rankedUp = (rankData & 0x80) != 0;
        byte seed = QuestManager.Instance()->DailyQuestSeed;
        List<QuestId> result = [];
        foreach (NpcData npcData in _questsByAlliedSociety[alliedSociety])
        {
            bool outranksAll = npcData.AllQuests.All(x => currentRank > x.AlliedSocietyRank);
            (uint NpcDataId, byte seed, bool outranksAll, bool rankedUp) key = (NpcDataId: npcData.IssuerDataId, seed, outranksAll, rankedUp);
            bool rankChanged = _alliedSocietyLastSeenRank.ContainsKey(alliedSociety) && _alliedSocietyLastSeenRank[alliedSociety] != currentRank;
            if (rankChanged)
                Reload();
            if (_dailyQuests.TryGetValue(key, out List<QuestId>? questIds))
                result.AddRange(questIds);
            else
            {
                List<QuestId> quests = CalculateAvailableQuests(npcData.AllQuests, seed, outranksAll, currentRank, rankedUp);
                _logger.LogInformation("Available for {Tribe} (Seed: {Seed}, Issuer: {IssuerId}): {Quests}", alliedSociety, seed, npcData.IssuerDataId, string.Join(", ", quests));

                _dailyQuests[key] = quests;
                result.AddRange(quests);
                _alliedSocietyLastSeenRank[alliedSociety] = currentRank;
            }
        }

        return result;
    }

    private static List<QuestId> CalculateAvailableQuests(List<QuestInfo> allQuests, byte seed, bool outranksAll,
        byte currentRank, bool rankedUp)
    {
        List<QuestInfo> eligible = [.. allQuests.Where(q => IsEligible(q, currentRank, rankedUp))];
        List<QuestInfo> available = [];
        if (eligible.Count == 0)
            return [];

        Rng rng = new(seed);
        if (outranksAll)
        {
            for (int i = 0, cnt = Math.Min(eligible.Count, 3); i < cnt; ++i)
            {
                int index = rng.Next(eligible.Count);
                while (available.Contains(eligible[index]))
                {
                    index = (index + 1) % eligible.Count;
                }
                available.Add(eligible[index]);
            }
        }
        else
        {
            int firstExclusive = eligible.FindIndex(q => q.AlliedSocietyQuestGroup == 3);
            if (firstExclusive >= 0)
                available.Add(eligible[firstExclusive + rng.Next(eligible.Count - firstExclusive)]);
            else
                firstExclusive = eligible.Count;
            for (int i = available.Count, cnt = Math.Min(firstExclusive, 3); i < cnt; ++i)
            {
                int index = rng.Next(firstExclusive);
                while (available.Contains(eligible[index]))
                {
                    index = (index + 1) % firstExclusive;
                }
                available.Add(eligible[index]);
            }
        }

        return available.Select(x => (QuestId)x.QuestId).ToList();
    }

    private static bool IsEligible(QuestInfo questInfo, byte currentRank, bool rankedUp) => rankedUp ? questInfo.AlliedSocietyRank == currentRank : questInfo.AlliedSocietyRank <= currentRank;

    private sealed class NpcData
    {
        public required uint IssuerDataId { get; init; }
        public required List<QuestInfo> AllQuests { get; init; } = [];
    }

    private record struct Rng(uint S0, uint S1 = 0, uint S2 = 0, uint S3 = 0)
    {
        public int Next(int range)
        {
            (S0, S1, S2, S3) = (S3, Transform(S0, S1), S1, S2);
            return (int)(S1 % range);
        }

        // returns new value for s1
        private static uint Transform(uint s0, uint s1)
        {
            uint temp = s0 ^ (s0 << 11);
            return s1 ^ temp ^ ((temp ^ (s1 >> 11)) >> 8);
        }
    }
}
