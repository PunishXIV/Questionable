using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using Questionable.Utils;
using ExcelQuest = Lumina.Excel.Sheets.Quest;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using QQuestId = Questionable.Model.Questing.QuestId;

namespace Questionable.Model;

internal sealed class QuestInfo : IQuestInfo
{
    public QuestInfo(ExcelQuest ogquest, uint newGamePlusChapter, byte startingCity, JournalGenreOverrides journalGenreOverrides)
    {
        TempQuest quest = Svc.Data.GetExcelSheet<TempQuest>().GetRow(ogquest.RowId);
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
            674 => " (Limsa-Gridania)",
            1432 => " (Gridania)",
            1433 => " (Limsa)",
            1434 => " (Ul'dah)",
            var _ => ""
        };

        Name = $"{quest.Name}{suffix}";
        BaseName = Name;
        Level = quest.ClassJobLevel[0];
        IssuerDataId = quest.IssuerStart.RowId;
        IssuerLocation = new(quest.IssuerLocation.Value);
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
            var _ => (quest.JournalGenre.ValueNullable?.RowId, null)
        };
        JournalGenre = genreAndSortKey.Item1;
        SortKey = genreAndSortKey.Item2 ?? quest.SortKey;

        IsMainScenarioQuest = quest.JournalGenre.ValueNullable?.Icon == 61412;
        CanCancel = quest.CanCancel;
        NumSequences = quest.TodoParams[0].ToDoCompleteSeq;
        ToDoLocations = quest.TodoParams.SelectMany(param => param.ToDoLocation).Where(rowRef => rowRef.RowId != 0).Select(rowRef => new SheetLevel(rowRef.Value)).ToList();
        CompletesInstantly = quest.TodoParams[0].ToDoCompleteSeq == 0;
        PreviousInstanceContent = quest.InstanceContent.Select(x => (ushort)x.RowId).Where(x => x != 0).ToList();
        PreviousInstanceContentJoin = (EQuestJoin)quest.InstanceContentJoin;
        GrandCompany = (GrandCompany)quest.GrandCompany.RowId;
        AlliedSociety = (EAlliedSociety)quest.BeastTribe.RowId;
        AlliedSocietyQuestGroup = quest.DailyQuestPool;
        AlliedSocietyRank = (EAlliedSocietyRank)quest.BeastReputationRank.RowId;
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
    public ImmutableList<QQuestId> QuestLocks { get; private set; }
    public EQuestJoin QuestLockJoin { get; private set; }
    public List<ushort> PreviousInstanceContent { get; }
    public EQuestJoin PreviousInstanceContentJoin { get; }
    public byte NumSequences { get; }
    public List<SheetLevel> ToDoLocations { get; }
    public bool CompletesInstantly { get; }
    public GrandCompany GrandCompany { get; }
    public byte AlliedSocietyQuestGroup { get; }
    public EAlliedSocietyRank AlliedSocietyRank { get; }
    public bool IsSeasonalEvent { get; }
    public uint NewGamePlusChapter { get; }
    public byte StartingCity { get; set; }
    public byte MoogleDeliveryLevel { get; }
    public bool IsMoogleDeliveryQuest => JournalGenre == 87;
    public IReadOnlyList<ItemReward> ItemRewards { get; }

    public ElementId QuestId { get; }
    public string Name { get; }
    public string BaseName { get; }
    public ushort Level { get; }
    public uint IssuerDataId { get; }
    public SheetLevel IssuerLocation { get; }
    public bool IsRepeatable { get; }
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; private set; }
    public EQuestJoin PreviousQuestJoin { get; }
    public uint? JournalGenre { get; set; }
    public ushort SortKey { get; set; }
    public bool IsMainScenarioQuest { get; }
    public bool CanCancel { get; }
    public EAlliedSociety AlliedSociety { get; }
    public IReadOnlyList<Job> ClassJobs { get; }
    public EExpansionVersion Expansion { get; }
    public string SimplifiedName => BaseName
        .Replace(".", "", StringComparison.Ordinal)
        .Replace("*", "", StringComparison.Ordinal)
        .Replace("\"", "", StringComparison.Ordinal)
        .Replace("/", "", StringComparison.Ordinal)
        .Replace("\\", "", StringComparison.Ordinal)
        .Replace("<", "", StringComparison.Ordinal)
        .Replace(">", "", StringComparison.Ordinal)
        .Replace("|", "", StringComparison.Ordinal)
        .Replace(":", "", StringComparison.Ordinal)
        .Replace("?", "", StringComparison.Ordinal)
        .TrimStart(SeIconChar.QuestSync.ToIconChar(), SeIconChar.QuestRepeatable.ToIconChar(), ' ');

    private static QuestId ReplaceOldQuestIds(QuestId questId)
    {
        return questId.Value switch
        {
            524 => new(4522),
            var _ => questId
        };
    }

    public void AddPreviousQuest(PreviousQuestInfo questId) => PreviousQuests = [.. PreviousQuests, questId];

    public void AddQuestLocks(EQuestJoin questJoin, params QuestId[] questId)
    {
        if (QuestLocks.Count > 0 && QuestLockJoin != questJoin)
            throw new InvalidOperationException();

        QuestLockJoin = questJoin;
        QuestLocks = [.. QuestLocks, .. questId];
    }

    public readonly struct SheetLevel(Level level)
    {
        public readonly Vector3 Position = level.AsVector3();
        public readonly RowRef Object => level.Object;
        public readonly float X => Position.X;
        public readonly float Y => Position.Y;
        public readonly float Z => Position.Z;
        public readonly TerritoryType Territory => Svc.Data.GetExcelSheet<TerritoryType>().GetRow(level.Territory.RowId);
        public readonly Map Map => Svc.Data.GetExcelSheet<Map>().GetRow(level.Map.RowId);
        public readonly Vector3 Game => new(
                WorldPositionToMapCoord(X, Map.SizeFactor, Map.OffsetX),
                0f,
                WorldPositionToMapCoord(Z, Map.SizeFactor, Map.OffsetY)
        );
        public override string? ToString() => $"SheetLevel({X:F2}, {Y:F2}, {Z:F2}, {Territory.RowId}, {Map.RowId}, {Game})";
        private static float WorldPositionToMapCoord(float v, ushort scale, short offset)
            => 41f * ((MathF.Truncate(v) + offset) * (scale / 100f) + 1024f - 1) / 2048f / (scale / 100f) + 1;
    }
}
