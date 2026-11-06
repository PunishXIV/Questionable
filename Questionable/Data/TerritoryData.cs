using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Game;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Quest = Lumina.Excel.Sheets.Quest;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class TerritoryData
{
    private readonly ImmutableDictionary<uint, ContentFinderConditionData> _contentFinderConditions;
    private readonly ImmutableDictionary<uint, uint> _dutyTerritories;
    private readonly ImmutableDictionary<uint, string> _instanceNames;
    private readonly ImmutableDictionary<(ElementId QuestId, byte Index), uint> _questBattlesToContentFinderCondition;
    private readonly ImmutableHashSet<uint> _territoriesWithMount;
    private readonly ImmutableDictionary<uint, string> _territoryNames;

    private static TerritoryData _instance;

    public TerritoryData(IDataManager dataManager)
    {
        _instance = this;
        _territoryNames = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0)
            .Select(x =>
                new
                {
                    x.RowId,
                    Name = x.PlaceName.ValueNullable?.Name.ToString() ?? x.PlaceNameZone.ValueNullable?.Name.ToString()
                })
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .ToImmutableDictionary(x => x.RowId, x => x.Name!);

        _territoriesWithMount = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0 && x.Mount)
            .Select(x => x.RowId)
            .ToImmutableHashSet();

        _dutyTerritories = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0 && x.ContentFinderCondition.RowId != 0)
            .ToImmutableDictionary(x => x.RowId, x => x.ContentFinderCondition.Value.ContentType.RowId);

        _instanceNames = dataManager.GetExcelSheet<ContentFinderCondition>()
            .Where(x => x.RowId > 0 && x.Content.RowId != 0 && x.ContentLinkType == 1 && x.ContentType.RowId != 6)
            .ToImmutableDictionary(x => x.Content.RowId, x => x.Name.ToDalamudString().ToString());

        _contentFinderConditions = dataManager.GetExcelSheet<ContentFinderCondition>()
            .Where(x => x.RowId > 0 && x.Content.RowId != 0 && x.ContentLinkType is 1 or 5 && x.ContentType.RowId != 6)
            .Select(x => new ContentFinderConditionData(x, dataManager.Language))
            .ToImmutableDictionary(x => x.ContentFinderConditionId, x => x);

        _questBattlesToContentFinderCondition = dataManager.GetExcelSheet<Quest>()
            .Where(x => x is { RowId: > 0, IssuerLocation.RowId: > 0 })
            .SelectMany(quest => GetQuestBattles(dataManager, quest))
            .Select(x => (x.QuestId, x.Index,
                CfcId: LookupContentFinderConditionForQuestBattle(dataManager, x.QuestBattleId)))
            .Where(x => x.CfcId != 0 && _contentFinderConditions.ContainsKey(x.CfcId))
            .Distinct()
            .ToImmutableDictionary(x => (x.QuestId, x.Index), x => x.CfcId);
    }

    public string? GetName(uint territoryId) => _territoryNames.GetValueOrDefault(territoryId);

    public static string GetNameAndId(uint territoryId) => _instance.GetNameAndIdImpl(territoryId);
    private string GetNameAndIdImpl(uint territoryId)
    {
        string? territoryName = GetName(territoryId);
        if (territoryName != null)
            return $"{territoryName} ({territoryId})";

        return territoryId.ToString(CultureInfo.InvariantCulture);
    }

    public bool CanUseMount(uint territoryId) => _territoriesWithMount.Contains(territoryId);

    public bool IsDutyInstance(uint territoryId) => _dutyTerritories.ContainsKey(territoryId);

    public bool IsQuestBattleInstance(uint territoryId) => _dutyTerritories.TryGetValue(territoryId, out uint contentType) && contentType == 7;

    public string? GetInstanceName(ushort instanceId) => _instanceNames.GetValueOrDefault(instanceId);

    public ContentFinderConditionData? GetContentFinderCondition(uint cfcId) => _contentFinderConditions.GetValueOrDefault(cfcId);

    public bool TryGetContentFinderCondition(uint cfcId,
        [NotNullWhen(true)] out ContentFinderConditionData? contentFinderConditionData) => _contentFinderConditions.TryGetValue(cfcId, out contentFinderConditionData);

    public bool TryGetContentFinderConditionForSoloInstance(ElementId questId, byte index,
        [NotNullWhen(true)] out ContentFinderConditionData? contentFinderConditionData)
    {
        if (_questBattlesToContentFinderCondition.TryGetValue((questId, index), out uint cfcId))
            return _contentFinderConditions.TryGetValue(cfcId, out contentFinderConditionData);

        contentFinderConditionData = null;
        return false;
    }

    public IEnumerable<(ElementId QuestId, byte Index, ContentFinderConditionData Data)> GetAllQuestsWithQuestBattles() => _questBattlesToContentFinderCondition.Select(x => (x.Key.QuestId, x.Key.Index, _contentFinderConditions[x.Value]));

    private static string FixName(string name, ClientLanguage language)
    {
        if (string.IsNullOrEmpty(name) || language != ClientLanguage.English)
            return name;

        return string.Concat(name[0].ToString().ToUpper(CultureInfo.InvariantCulture), name.AsSpan(1));
    }

    private static IEnumerable<(ElementId QuestId, byte Index, uint QuestBattleId)> GetQuestBattles(IDataManager dataManager, Quest quest)
    {
        HashSet<byte> questBattleIndexes = [];
        List<(byte Index, uint Id)> seasonalDuties = [];
        foreach (Quest.QuestParamsStruct t in quest.QuestParams)
        {
            if (t.ScriptInstruction.IsEmpty)
                break;

            string instruction = t.ScriptInstruction.ToString();
            uint? instanceType = instruction.StartsWith("INSTANCEDUNGEON", StringComparison.Ordinal)
                ? dataManager.GetExcelSheet<InstanceContent>().GetRowOrDefault(t.ScriptArg)?.InstanceContentType.RowId
                : null;
            if (TryGetSoloDutyIndex(instruction, instanceType, out byte index))
            {
                if (instruction.StartsWith("QUESTBATTLE", StringComparison.Ordinal))
                {
                    if (questBattleIndexes.Add(index))
                        yield return (QuestId.FromRowId(quest.RowId), index, t.ScriptArg);
                }
                else
                    seasonalDuties.Add((index, t.ScriptArg));
            }
        }
        foreach (var duty in seasonalDuties)
            if (questBattleIndexes.Add(duty.Index))
                yield return (QuestId.FromRowId(quest.RowId), duty.Index, duty.Id);
    }

    internal static bool TryGetSoloDutyIndex(string instruction, uint? instanceType, out byte index)
    {
        index = 0;
        const string questBattle = "QUESTBATTLE";
        const string instanceDungeon = "INSTANCEDUNGEON";
        string prefix;
        if (instruction.StartsWith(questBattle, StringComparison.Ordinal))
            prefix = questBattle;
        else if (instruction.StartsWith(instanceDungeon, StringComparison.Ordinal) &&
                 instanceType == (uint)FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentType.SeasonalDungeon)
            prefix = instanceDungeon;
        else
            return false;
        return byte.TryParse(instruction.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static uint LookupContentFinderConditionForQuestBattle(IDataManager dataManager, uint questBattleId)
    {
        uint cfcId = dataManager.GetExcelSheet<QuestBattleResident>().GetRowOrDefault(questBattleId)?.SoloDuty.RowId ?? 0;
        return cfcId != 0 ? cfcId : dataManager.GetExcelSheet<InstanceContent>().GetRowOrDefault(questBattleId)?.ContentFinderCondition.RowId ?? 0;
    }

    public sealed record ContentFinderConditionData
    (
        uint ContentFinderConditionId,
        string Name,
        uint TerritoryId,
        ushort RequiredItemLevel,
        byte ClassJobLevelSync,
        EContentType? ContentType,
        byte RequiredLevel = 0)
    {
        public ContentFinderConditionData(ContentFinderCondition condition, ClientLanguage clientLanguage)
            : this(condition.RowId, FixName(condition.Name.ToDalamudString().ToString(), clientLanguage),
                condition.TerritoryType.RowId, condition.ItemLevelRequired, condition.ClassJobLevelSync,
                (EContentType?)condition.ContentType.ValueNullable?.RowId, condition.ClassJobLevelRequired)
        {
        }
    }
}
