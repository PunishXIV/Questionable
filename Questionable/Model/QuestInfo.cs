using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using ExcelQuest = Lumina.Excel.Sheets.Quest;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using QQuestId = Questionable.Model.Questing.QuestId;

namespace Questionable.Model;

internal sealed class QuestInfo : IQuestInfo
{
    public QuestInfo(ExcelQuest quest, uint newGamePlusChapter, byte startingCity, JournalGenreOverrides journalGenreOverrides)
    {
        QuestId = QQuestId.FromRowId(quest.RowId);

        string suffix = QuestId.Value switch
        {
            85 => " (Lancer)",
            108 => " (Marauder)",
            109 => " (Arcanist)",
            123 => " (Archer)",
            124 => " (Conjurer)",
            568 => " (Gladiator)",
            569 => " (Pugilist)",
            570 => " (Thaumaturge)",
            673 => " (Ul'dah)",
            674 => " (Limsa/Gridania)",
            1432 => " (Gridania)",
            1433 => " (Limsa)",
            1434 => " (Ul'dah)",
            _ => "",
        };

        Name = $"{quest.Name}{suffix}";
        Level = quest.ClassJobLevel[0];
        IssuerDataId = quest.IssuerStart.RowId;
        IsRepeatable = quest.IsRepeatable;
        PreviousQuests =
            new List<PreviousQuestInfo>
                {
                    new(ReplaceOldQuestIds(QQuestId.FromRowId(quest.PreviousQuest[0].RowId)), quest.Unknown7),
                    new(ReplaceOldQuestIds(QQuestId.FromRowId(quest.PreviousQuest[1].RowId))),
                    new(ReplaceOldQuestIds(QQuestId.FromRowId(quest.PreviousQuest[2].RowId)))
                }
                .Where(x => x.QuestId.Value != 0)
                .ToImmutableList();
        PreviousQuestJoin = (EQuestJoin)quest.PreviousQuestJoin;
        QuestLocks = quest.QuestLock
            .Select(x => QQuestId.FromRowId(x.RowId))
            .Where(x => x.Value != 0)
            .ToImmutableList();
        QuestLockJoin = (EQuestJoin)quest.QuestLockJoin;

        ValueTuple<uint?, ushort?> genreAndSortKey = QuestId.Value switch
        {
            >= 1119 and <= 1127 or 1579 => (journalGenreOverrides.ARelicRebornQuests, 0),
            >= 4196 and <= 4209 => (journalGenreOverrides.ThavnairSideQuests, null),
            4173 => (journalGenreOverrides.RadzAtHanSideQuests, null),
            _ => (quest.JournalGenre.ValueNullable?.RowId, null),
        };
        JournalGenre = genreAndSortKey.Item1;
        SortKey = genreAndSortKey.Item2 ?? quest.SortKey;

        IsMainScenarioQuest = quest.JournalGenre.ValueNullable?.Icon == 61412;
        CompletesInstantly = quest.TodoParams[0].ToDoCompleteSeq == 0;
        PreviousInstanceContent = quest.InstanceContent.Select(x => (ushort)x.RowId).Where(x => x != 0).ToList();
        PreviousInstanceContentJoin = (EQuestJoin)quest.InstanceContentJoin;
        GrandCompany = (GrandCompany)quest.GrandCompany.RowId;
        AlliedSociety = (EAlliedSociety)quest.BeastTribe.RowId;
        AlliedSocietyQuestGroup = quest.DailyQuestPool;
        AlliedSocietyRank = (int)quest.BeastReputationRank.RowId;
        ClassJobs = QuestInfoUtils.AsList(quest.ClassJobCategory0.ValueNullable!);
        IsSeasonalEvent = quest.Festival.RowId != 0;
        NewGamePlusChapter = newGamePlusChapter;
        StartingCity = startingCity;
        MoogleDeliveryLevel = (byte)quest.DeliveryQuest.RowId;
        ItemRewards = quest.Reward.Where(x => x.RowId > 0 && x.Is<Item>())
            .Select(x => x.GetValueOrDefault<Item>())
            .Where(x => x != null)
            .Cast<Item>()
            .Where(x => x.IsUntradable)
            .Select(x => ItemReward.CreateFromItem(x, QuestId))
            .Where(x => x != null)
            .Cast<ItemReward>()
            .ToList();
        Expansion = (EExpansionVersion)quest.Expansion.RowId;
    }

    private static QuestId ReplaceOldQuestIds(QuestId questId)
    {
        return questId.Value switch
        {
            524 => new QuestId(4522),
            _ => questId,
        };
    }

    public ElementId QuestId { get; }
    public string Name { get; }
    public ushort Level { get; }
    public uint IssuerDataId { get; }
    public bool IsRepeatable { get; }
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; private set; }
    public EQuestJoin PreviousQuestJoin { get; }
    public ImmutableList<QuestId> QuestLocks { get; private set; }
    public EQuestJoin QuestLockJoin { get; private set; }
    public List<ushort> PreviousInstanceContent { get; }
    public EQuestJoin PreviousInstanceContentJoin { get; }
    public uint? JournalGenre { get; set; }
    public ushort SortKey { get; set; }
    public bool IsMainScenarioQuest { get; }
    public bool CompletesInstantly { get; }
    public GrandCompany GrandCompany { get; }
    public EAlliedSociety AlliedSociety { get; }
    public byte AlliedSocietyQuestGroup { get; }
    public int AlliedSocietyRank { get; }
    public IReadOnlyList<EClassJob> ClassJobs { get; }
    public bool IsSeasonalEvent { get; }
    public uint NewGamePlusChapter { get; }
    public byte StartingCity { get; set; }
    public byte MoogleDeliveryLevel { get; }
    public bool IsMoogleDeliveryQuest => JournalGenre == 87;
    public IReadOnlyList<ItemReward> ItemRewards { get; }
    public EExpansionVersion Expansion { get; }

    public void AddPreviousQuest(PreviousQuestInfo questId)
    {
        PreviousQuests = [..PreviousQuests, questId];
    }

    public void AddQuestLocks(EQuestJoin questJoin, params QuestId[] questId)
    {
        if (QuestLocks.Count > 0 && QuestLockJoin != questJoin)
            throw new InvalidOperationException();

        QuestLockJoin = questJoin;
        QuestLocks = [..QuestLocks, ..questId];
    }
}
